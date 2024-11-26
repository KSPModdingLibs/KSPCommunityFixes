// Disable logging when the thumbnail is not found.
// Useful for testing with Unity Explorer installed, where debug calls take measurably longer.
#define DISABLE_THUMBNAIL_LOGGING

using HarmonyLib;
using KSP.Localization;
using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.Performance
{
    class CraftBrowserOptimisations : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        // These toggles are here for comparison purposes.
        public static bool patchCheckCraftFileType = true;
        public static bool preventImmediateRebuilds = true;
        public static bool preventDelayedRebuild = true;
        public static bool preventSearchYield = true;
        public static bool useTimeBudget = true;

        private static int lastBuiltFrame;

        public static float searchKeystrokeDelay = 0.1f;

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(CraftBrowserDialog), nameof(CraftBrowserDialog.setbottomButtons));

            AddPatch(PatchType.Prefix, typeof(ShipConstruction), nameof(ShipConstruction.CheckCraftFileType));

            AddPatch(PatchType.Prefix, typeof(CraftBrowserDialog), nameof(CraftBrowserDialog.BuildPlayerCraftList));

            AddPatch(PatchType.Postfix, typeof(CraftBrowserDialog), nameof(CraftBrowserDialog.Start));
            AddPatch(PatchType.Postfix, typeof(CraftBrowserDialog), nameof(CraftBrowserDialog.ReDisplay));

#if DISABLE_THUMBNAIL_LOGGING
            AddPatch(PatchType.Transpiler, typeof(ShipConstruction), nameof(ShipConstruction.GetThumbnail), new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(FileInfo) });
#endif

            AddPatch(PatchType.Prefix, typeof(CraftSearch), nameof(CraftSearch.SearchRoutine));
        }

        // Fix a miscellanous bug where setbottomButtons ignores showMergeOption. This is useful for Halbann/LazySpawner and BD's Vessel Mover.
        static void CraftBrowserDialog_setbottomButtons_Postfix(CraftBrowserDialog __instance) =>
            __instance.btnMerge.gameObject.SetActive(__instance.btnMerge.gameObject.activeSelf && __instance.showMergeOption);

        // Speed up CheckCraftFileType by avoiding loading the whole craft file, instead only checking the file path.
        // Loadmeta could be used here if it turns out that the path does not always contain the facility name.
        static bool ShipConstruction_CheckCraftFileType_Prefix(string filePath, ref EditorFacility __result)
        {
            if (!patchCheckCraftFileType)
                return true;

            if (string.IsNullOrEmpty(filePath))
            {
                __result = EditorFacility.None;
                return false;
            }

            // Search the file path for either "SPH" or "VAB".
            bool sph = false;
            string facilityString = filePath.Split(Path.DirectorySeparatorChar).FirstOrDefault(f => (sph = f == "SPH") || f == "VAB");

            if (string.IsNullOrEmpty(facilityString))
            {
                // Fallback in case the split fails for any reason.
                __result = facilityString.Contains("SPH") ? EditorFacility.SPH :
                    (facilityString.Contains("VAB") ? EditorFacility.VAB : EditorFacility.None);
            }
            else
            {
                __result = sph ? EditorFacility.SPH : EditorFacility.VAB;
            }

            return false;
        }

        // Prevent CraftBrowserDialog from rebuilding the craft list twice in a frame, as happens by default.
        // Only the UI is rebuilt, but it still causes it to take 30% longer to open the dialog.
        // I think a frame check is a simpler alternative to transpile patches.
        static bool CraftBrowserDialog_BuildPlayerCraftList_Prefix(CraftBrowserDialog __instance)
        {
            if (!preventImmediateRebuilds)
                return true;

            if (Time.frameCount == lastBuiltFrame)
                return false;

            lastBuiltFrame = Time.frameCount;

            return true;
        }

        // Prevent the craft list from rebuilding yet again on the next frame due to some poor logic in DirectoryController.
        private static void PreventDelayedRebuild(CraftBrowserDialog dialog)
        {
            if (!preventDelayedRebuild)
                return;

            dialog.directoryController.isEnabledThisFrame = false;
        }

        static void CraftBrowserDialog_Start_Postfix(CraftBrowserDialog __instance)
        {
            PreventDelayedRebuild(__instance);
            CraftSearch.Instance.searchKeystrokeDelay = searchKeystrokeDelay;
        }

        static void CraftBrowserDialog_ReDisplay_Postfix(CraftBrowserDialog __instance) =>
            PreventDelayedRebuild(__instance);

        // Disable the call to Debug.Log when the thumbnail is not found.
        // On a new save with a large number of imported craft, the game will generate a lot of useless log entries.
        static IEnumerable<CodeInstruction> ShipConstruction_GetThumbnail_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo debug = AccessTools.Method(typeof(Debug), nameof(Debug.Log), new Type[] { typeof(object) });

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(debug))
                {
                    instruction.opcode = OpCodes.Pop;
                    instruction.operand = null;
                    break;
                }
            }

            return instructions;
        }

        // Replace the search routine to prevent the search routine from yielding
        // a frame on each craft, and to make some small optimisations.
        static bool CraftSearch_SearchRoutine_Prefix(CraftSearch __instance, ref IEnumerator __result)
        {
            if (!preventSearchYield)
                return true;

            __result = SearchRoutine(__instance);
            return false;
        }

        private static IEnumerator SearchRoutine(CraftSearch __instance)
        {
            // Delay the start of the search routine by the searchKeystrokeDelay.
            while (__instance.searchTimer + __instance.searchKeystrokeDelay > Time.realtimeSinceStartup)
                yield return null;

            bool filtered = false;
            string searchTerm = __instance.searchField.text;
            List<CraftEntry> entries = CraftSearch.craftBrowserDialog.craftList;
            float timer = Time.realtimeSinceStartup;
            float budget = 1f / 60f / 2f; // 8 ms.
            bool searchEmpty = string.IsNullOrWhiteSpace(searchTerm);

#if DEBUG
            Stopwatch stopwatch = Stopwatch.StartNew();
#endif

            foreach (CraftEntry entry in entries)
            {
                bool result = searchEmpty || FasterCraftMatchesSearch(entry, searchTerm);

                if (entry.gameObject.activeSelf != result)
                    entry.gameObject.SetActive(result);

                if (!filtered && !result)
                    filtered = true;

                if (useTimeBudget && Time.realtimeSinceStartup - timer > budget)
                {
                    timer = Time.realtimeSinceStartup;
                    yield return null;
                }
            }

#if DEBUG
            stopwatch.Stop();
            Debug.Log($"[CraftBrowserOptimisations]: Filtered {entries.Count} craft in {stopwatch.Elapsed.Milliseconds:N3} ms.");
#endif

            __instance.hasFiltered?.Invoke(filtered);

            if (string.IsNullOrWhiteSpace(searchTerm) && __instance.IsDifferentSearch)
                __instance.StopSearch();

            __instance.previousSearch = searchTerm;
        }

        private static bool FasterCraftMatchesSearch(CraftEntry craft, string searchTerm)
        {
            if (craft.craftName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) > -1)
                return true;

            if (craft.craftProfileInfo == null || string.IsNullOrEmpty(craft.craftProfileInfo.description))
                return false;

            string text = Localizer.Format(craft.craftProfileInfo.description);
            return text.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) > -1;
        }
    }
}
