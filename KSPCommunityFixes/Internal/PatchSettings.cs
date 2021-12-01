using HarmonyLib;
using KSP.Localization;
using KSPCommunityFixes.QoL;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes
{
    [PatchPriority(Order = 10)]
    class PatchSettings : BasePatch
    {
        protected override bool IgnoreConfig => true;

        protected override Version VersionMin => new Version(1, 8, 0);

        private static int entryCount = 0;
        private static AltimeterHorizontalPosition altimeterPatch;
        private static DisableManeuverTool maneuverToolPatch;

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(GameplaySettingsScreen), "DrawMiniSettings"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(GameplaySettingsScreen), "ApplySettings"),
                this));

            altimeterPatch = KSPCommunityFixes.GetPatchInstance<AltimeterHorizontalPosition>();
            maneuverToolPatch = KSPCommunityFixes.GetPatchInstance<DisableManeuverTool>();

            if (altimeterPatch != null)
                entryCount++;

            if (maneuverToolPatch != null)
                entryCount++;
        }

        static void GameplaySettingsScreen_DrawMiniSettings_Postfix(GameplaySettingsScreen __instance, ref DialogGUIBase[] __result)
        {
            if (entryCount == 0)
                return;

            int count = __result.Length;

            DialogGUIBase[] modifiedResult = new DialogGUIBase[count + entryCount + 1];
            
            for (int i = 0; i < count; i++)
                modifiedResult[i] = __result[i];

            modifiedResult[count] = new DialogGUIBox("KSP Community Fixes", -1f, 18f, null);
            count++;

            if (maneuverToolPatch != null)
            {
                modifiedResult[count] = new DialogGUIHorizontalLayout(TextAnchor.MiddleLeft,
                    new DialogGUILabel(() => Localizer.Format("#autoLOC_6006123"), 150f),
                    new DialogGUIToggle(DisableManeuverTool.enableManeuverTool, () => (!DisableManeuverTool.enableManeuverTool) ? Localizer.Format("#autoLOC_6001071") : Localizer.Format("#autoLOC_6001072"), DisableManeuverTool.OnToggleApp, 150f), new DialogGUIFlexibleSpace());
                count++;
            }

            if (altimeterPatch != null)
            {
                modifiedResult[count] = new DialogGUIHorizontalLayout(TextAnchor.MiddleLeft,
                    new DialogGUILabel(() => Localizer.Format("Altimeter pos (Left<->Right)"), 150f),
                    new DialogGUISlider(() => AltimeterHorizontalPosition.altimeterPosition, 0f, 1f, wholeNumbers: false, 200f, 20f, delegate (float f)
                    {
                        AltimeterHorizontalPosition.altimeterPosition = f;
                        AltimeterHorizontalPosition.SetTopFramePosition();
                    }), new DialogGUIFlexibleSpace());
                count++;
            }

            __result = modifiedResult;
        }

        static void GameplaySettingsScreen_ApplySettings_Postfix()
        {
            if (maneuverToolPatch != null)
            {
                ConfigNode node = new ConfigNode();
                node.AddValue(nameof(DisableManeuverTool.enableManeuverTool), DisableManeuverTool.enableManeuverTool);
                SaveData<DisableManeuverTool>(node);
            }

            if (altimeterPatch != null)
            {
                ConfigNode node = new ConfigNode();
                node.AddValue(nameof(AltimeterHorizontalPosition.altimeterPosition), AltimeterHorizontalPosition.altimeterPosition);
                SaveData<AltimeterHorizontalPosition>(node);
            }

        }
    }
}
