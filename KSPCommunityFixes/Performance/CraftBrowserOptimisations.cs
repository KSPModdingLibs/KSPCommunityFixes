// Disable logging when the thumbnail is not found. Only useful for testing with Unity Explorer installed, where debug calls take many times longer.
#define DISABLE_THUMBNAIL_LOGGING

using HarmonyLib;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    class CraftBrowserOptimisations : BasePatch
    {
        // These toggles are here for comparison purposes.

        public static bool patchCheckCraftFileType = true;

        public static bool preventImmediateRebuilds = true;
        public static int lastFrame;
        public static HashSet<int> builtThisFrame = new HashSet<int>();

        public static bool preventDelayedRebuild = true;

        protected override Version VersionMin => new Version(1, 12, 0);

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

            AddPatch(PatchType.Transpiler, typeof(CraftBrowserDialog), nameof(CraftBrowserDialog.BuildPlayerCraftList));
            AddPatch(PatchType.Postfix, typeof(CraftBrowserDialog), nameof(CraftBrowserDialog.OnDisable));
            AddPatch(PatchType.Prefix, typeof(CraftProfileInfo), nameof(CraftProfileInfo.GetSaveData));
        }

        // Fix a miscellanous bug where setbottomButtons ignores showMergeOption. This is useful for Halbann/LazySpawner and BD's Vessel Mover.
        static void CraftBrowserDialog_setbottomButtons_Postfix(CraftBrowserDialog __instance) =>
            __instance.btnMerge.gameObject.SetActive(__instance.btnMerge.gameObject.activeSelf && __instance.showMergeOption);

        // Speed up CheckCraftFileType by avoiding loading the whole craft file, instead only checking the file path.
        // Loadmeta could be used here if it turns out that the path does not always contian the facility name.
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

            if (Time.frameCount != lastFrame)
            {
                builtThisFrame.Clear();
                lastFrame = Time.frameCount;
            }

            int hash = __instance.GetHashCode();
            if (builtThisFrame.Contains(hash))
                return false;

            builtThisFrame.Add(hash);

            return true;
        }

        // Prevent the craft list from rebuilding yet again on the next frame due to some poor logic in DirectoryController.
        private static void PreventDelayedRebuild(CraftBrowserDialog dialog)
        {
            if (!preventDelayedRebuild)
                return;

            dialog.directoryController.isEnabledThisFrame = false;
        }

        static void CraftBrowserDialog_Start_Postfix(CraftBrowserDialog __instance) => PreventDelayedRebuild(__instance);

        static void CraftBrowserDialog_ReDisplay_Postfix(CraftBrowserDialog __instance) => PreventDelayedRebuild(__instance);

        static IEnumerable<CodeInstruction> ShipConstruction_GetThumbnail_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Disable the call to Debug.Log when the thumbnail is not found.

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
    }
}
