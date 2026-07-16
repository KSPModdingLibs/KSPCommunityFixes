using HarmonyLib;
using KSP.Localization;
using KSPCommunityFixes.Performance;
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
        private static OptionalMakingHistoryDLCFeatures disableMHPatch;
        private static TextureStreaming textureStreamingPatch;

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(GameplaySettingsScreen), "DrawMiniSettings");

            AddPatch(PatchType.Postfix, typeof(GameplaySettingsScreen), "ApplySettings");

            altimeterPatch = KSPCommunityFixes.GetPatchInstance<AltimeterHorizontalPosition>();
            if (altimeterPatch != null)
                entryCount++;

            maneuverToolPatch = KSPCommunityFixes.GetPatchInstance<DisableManeuverTool>();
            if (maneuverToolPatch != null)
                entryCount++;

            disableMHPatch = KSPCommunityFixes.GetPatchInstance<OptionalMakingHistoryDLCFeatures>();
            if (disableMHPatch != null)
                entryCount++;

            textureStreamingPatch = KSPCommunityFixes.GetPatchInstance<TextureStreaming>();
            if (textureStreamingPatch != null)
                entryCount += 2;

            // NoIVA is always enabled
            entryCount++;
        }

        static void GameplaySettingsScreen_DrawMiniSettings_Postfix(ref DialogGUIBase[] __result)
        {
            if (entryCount == 0)
                return;

            int count = __result.Length;

            // +1 for the KSPCF title box; entryCount already accounts for every added row (the streaming
            // patch contributes 2, see ApplyPatches).
            DialogGUIBase[] modifiedResult = new DialogGUIBase[count + entryCount + 1];
            
            for (int i = 0; i < count; i++)
                modifiedResult[i] = __result[i];

            modifiedResult[count] = new DialogGUIBox(KSPCommunityFixes.LOC_KSPCF_Title, -1f, 18f, null);
            count++;

            if (disableMHPatch != null)
            {
                DialogGUIToggle toggle = new DialogGUIToggle(OptionalMakingHistoryDLCFeatures.isMHEnabled,
                    () => (!OptionalMakingHistoryDLCFeatures.isMHEnabled)
                        ? Localizer.Format("#autoLOC_6001071") //"Disabled"
                        : Localizer.Format("#autoLOC_6001072"), //"Enabled"
                    b => OptionalMakingHistoryDLCFeatures.isMHEnabled = b, 150f);
                toggle.tooltipText = OptionalMakingHistoryDLCFeatures.LOC_SettingsTooltip;
                toggle.OptionInteractableCondition = () => !OptionalMakingHistoryDLCFeatures.isMHDisabledFromConfig;

                modifiedResult[count] = new DialogGUIHorizontalLayout(TextAnchor.MiddleLeft,
                    new DialogGUILabel(() => Localizer.Format(OptionalMakingHistoryDLCFeatures.LOC_MHDLC), 150f), //"Maneuver Tool"
                    toggle, new DialogGUIFlexibleSpace());
                count++;
            }

            if (maneuverToolPatch != null)
            {
                DialogGUIToggle toggle = new DialogGUIToggle(DisableManeuverTool.enableManeuverTool,
                    () => (!DisableManeuverTool.enableManeuverTool) 
                        ? Localizer.Format("#autoLOC_6001071") //"Disabled"
                        : Localizer.Format("#autoLOC_6001072"), //"Enabled"
                    DisableManeuverTool.OnToggleApp, 150f);
                toggle.tooltipText = DisableManeuverTool.LOC_SettingsTooltip;
                toggle.OptionInteractableCondition = () => !DisableManeuverTool.alwaysDisabled;

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

            DialogGUISlider noIVAslider = new DialogGUISlider(NoIVA.PatchStateToFloat, 0f, 2f, true, 100f, 20f, NoIVA.SwitchPatchState);
            noIVAslider.tooltipText = NoIVA.LOC_SettingsTooltip;
            DialogGUILabel valueLabel = new DialogGUILabel(NoIVA.PatchStateTitle);

            modifiedResult[count] = new DialogGUIHorizontalLayout(TextAnchor.MiddleLeft,
                new DialogGUILabel(NoIVA.LOC_SettingsTitle, 150f), noIVAslider, valueLabel, new DialogGUIFlexibleSpace());
            count++;

            if (textureStreamingPatch != null)
            {
                DialogGUIToggle streamingToggle = new(
                    TextureStreaming.MipmapStreamingEnabled,
                    () => (!TextureStreaming.MipmapStreamingEnabled)
                        ? Localizer.Format("#autoLOC_6001071")  //"Disabled"
                        : Localizer.Format("#autoLOC_6001072"), //"Enabled"
                    b => TextureStreaming.MipmapStreamingEnabled = b, 150f);
                streamingToggle.tooltipText = TextureStreaming.LOC_StreamingEnabledTooltip;

                modifiedResult[count] = new DialogGUIHorizontalLayout(
                    TextAnchor.MiddleLeft,
                    new DialogGUILabel(TextureStreaming.LOC_StreamingEnabledTitle, 150f),
                    streamingToggle,
                    new DialogGUIFlexibleSpace());
                count++;

                // Streaming memory budget, in MB (0 .. total VRAM). Only interactable while streaming is on.
                float budgetMax = Math.Max(1024, SystemInfo.graphicsMemorySize);
                DialogGUISlider budgetSlider = new(
                    () => TextureStreaming.MipmapStreamingBudgetMb,
                    0f,
                    budgetMax,
                    wholeNumbers: true,
                    128f,
                    20f,
                    budget => TextureStreaming.MipmapStreamingBudgetMb = (int)budget);
                budgetSlider.tooltipText = TextureStreaming.LOC_StreamingBudgetTooltip;
                budgetSlider.OptionInteractableCondition = () => TextureStreaming.MipmapStreamingEnabled;
                DialogGUILabel budgetValue = new(() => Localizer.Format(TextureStreaming.LOC_F_StreamingBudgetValue, TextureStreaming.MipmapStreamingBudgetMb));

                modifiedResult[count] = new DialogGUIHorizontalLayout(
                    TextAnchor.MiddleLeft,
                    new DialogGUILabel(TextureStreaming.LOC_StreamingBudgetTitle, 150f),
                    budgetSlider,
                    budgetValue,
                    new DialogGUIFlexibleSpace());
                count++;
            }

            __result = modifiedResult;
        }

        static void GameplaySettingsScreen_ApplySettings_Postfix()
        {
            if (disableMHPatch != null)
            {
                ConfigNode node = new ConfigNode();
                node.AddValue(nameof(OptionalMakingHistoryDLCFeatures.isMHEnabled), OptionalMakingHistoryDLCFeatures.isMHEnabled);
                SaveData<OptionalMakingHistoryDLCFeatures>(node);
            }

            if (maneuverToolPatch != null)
            {
                ConfigNode node = new ConfigNode();
                node.AddValue(nameof(DisableManeuverTool.enableManeuverTool), DisableManeuverTool.enableManeuverTool);
                SaveData<DisableManeuverTool>(node);
            }

            if (altimeterPatch != null)
            {
                ConfigNode node = new();
                node.AddValue(nameof(AltimeterHorizontalPosition.altimeterPosition), AltimeterHorizontalPosition.altimeterPosition);
                SaveData<AltimeterHorizontalPosition>(node);
            }

            NoIVA.SaveSettings();

            ConfigNode streamingNode = new();
            streamingNode.AddValue(nameof(TextureStreaming.MipmapStreamingEnabled), TextureStreaming.MipmapStreamingEnabled);
            streamingNode.AddValue(nameof(TextureStreaming.MipmapStreamingBudgetMb), TextureStreaming.MipmapStreamingBudgetMb);
            SaveData<TextureStreaming>(streamingNode);
            
        }
    }
}
