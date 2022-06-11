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

        protected override void ApplyPatches(List<PatchInfo> patches)
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
            if (altimeterPatch != null)
                entryCount++;

            maneuverToolPatch = KSPCommunityFixes.GetPatchInstance<DisableManeuverTool>();
            if (maneuverToolPatch != null)
                entryCount++;

            if (TextureLoaderOptimizations.IsPatchEnabled)
                entryCount++;

            // NoIVA is always enabled
            entryCount++;
        }

        static void GameplaySettingsScreen_DrawMiniSettings_Postfix(ref DialogGUIBase[] __result)
        {
            if (entryCount == 0)
                return;

            int count = __result.Length;

            DialogGUIBase[] modifiedResult = new DialogGUIBase[count + entryCount + 1];
            
            for (int i = 0; i < count; i++)
                modifiedResult[i] = __result[i];

            modifiedResult[count] = new DialogGUIBox(KSPCommunityFixes.LOC_KSPCF_Title, -1f, 18f, null);
            count++;

            if (maneuverToolPatch != null)
            {
                DialogGUIToggle toggle = new DialogGUIToggle(DisableManeuverTool.enableManeuverTool,
                    () => (!DisableManeuverTool.enableManeuverTool) 
                        ? Localizer.Format("#autoLOC_6001071") //"Disabled"
                        : Localizer.Format("#autoLOC_6001072"), //"Enabled"
                    DisableManeuverTool.OnToggleApp, 150f);
                toggle.tooltipText = DisableManeuverTool.LOC_SettingsTooltip;

                modifiedResult[count] = new DialogGUIHorizontalLayout(TextAnchor.MiddleLeft,
                    new DialogGUILabel(() => Localizer.Format("#autoLOC_6006123"), 150f), //"Maneuver Tool"
                    toggle, new DialogGUIFlexibleSpace());
                count++;
            }

            if (altimeterPatch != null)
            {
                DialogGUISlider slider = new DialogGUISlider(() => AltimeterHorizontalPosition.altimeterPosition, 0f, 1f, wholeNumbers: false, 200f, 20f, delegate(float f)
                {
                    AltimeterHorizontalPosition.altimeterPosition = f;
                    AltimeterHorizontalPosition.SetTopFramePosition();
                });
                slider.tooltipText = AltimeterHorizontalPosition.LOC_SettingsTooltip;

                modifiedResult[count] = new DialogGUIHorizontalLayout(TextAnchor.MiddleLeft,
                    new DialogGUILabel(AltimeterHorizontalPosition.LOC_SettingsTitle, 150f),
                    slider, new DialogGUIFlexibleSpace());
                count++;
            }

            if (TextureLoaderOptimizations.IsPatchEnabled)
            {
                DialogGUIToggle toggle = new DialogGUIToggle(TextureLoaderOptimizations.textureCacheEnabled,
                    () => (TextureLoaderOptimizations.textureCacheEnabled) 
                        ? Localizer.Format("#autoLOC_900889") //"Enabled"
                        : Localizer.Format("#autoLOC_900890"), //"Disabled"
                    TextureLoaderOptimizations.OnToggleCacheFromSettings, 150f);
                toggle.tooltipText = TextureLoaderOptimizations.LOC_SettingsTooltip;

                modifiedResult[count] = new DialogGUIHorizontalLayout(TextAnchor.MiddleLeft,
                    new DialogGUILabel(() => TextureLoaderOptimizations.LOC_SettingsTitle, 150f),
                    toggle, new DialogGUIFlexibleSpace());
                count++;
            }

            DialogGUISlider noIVAslider = new DialogGUISlider(NoIVA.PatchStateToFloat, 0f, 2f, true, 100f, 20f, NoIVA.SwitchPatchState);
            noIVAslider.tooltipText = NoIVA.LOC_SettingsTooltip;
            DialogGUILabel valueLabel = new DialogGUILabel(NoIVA.PatchStateTitle);

            modifiedResult[count] = new DialogGUIHorizontalLayout(TextAnchor.MiddleLeft,
                new DialogGUILabel(NoIVA.LOC_SettingsTitle, 150f), noIVAslider, valueLabel, new DialogGUIFlexibleSpace());
            count++;

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

            NoIVA.SaveSettings();
        }
    }
}
