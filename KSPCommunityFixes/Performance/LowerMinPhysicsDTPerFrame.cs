// Implement min value of 0.02 instead of 0.03 for the "Max Physics Delta-Time Per Frame" main menu setting.
// See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/175

using HarmonyLib;
using KSP.UI.Screens.Settings;
using KSP.UI.TooltipTypes;
using System;
using System.Collections.Generic;
using UnityEngine;
using static KSP.UI.Screens.Settings.ReflectedSettingsWindow;

namespace KSPCommunityFixes.Performance
{
    internal class LowerMinPhysicsDTPerFrame : BasePatch
    {
        public static string LOC_SettingsTooltip = 
            "How the game handle lag in CPU bound situations.\n" +
            "Mostly relevant with large part count vessels.\n" +
            "\n" +
            "Lower value :\n" +
            "Higher and smoother FPS, but game time might advance slower than real time.\n" +
            "\n" +
            "Higher value :\n" +
            "Lower and choppier FPS, but game time will advance closer to real time.";

        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(SettingsScreen), nameof(SettingsScreen.Awake));

            AddPatch(PatchType.Prefix, typeof(ReflectedSettingsWindow), nameof(SetupReflectionValues));
        }

        static void SettingsScreen_Awake_Prefix(SettingsScreen __instance)
        {
            try
            {
                ReflectedSettingsWindow settingsWindow = (ReflectedSettingsWindow)__instance.setupPrefab.setup.tabs[0].window.prefab;

                ControlWrapper controlWrapper = settingsWindow.tabs[1].tabs[1].controls[9];

                if (controlWrapper.settingName != "PHYSICS_FRAME_DT_LIMIT")
                    throw new Exception("PHYSICS_FRAME_DT_LIMIT control not found");

                ValueWrapper controlValue = controlWrapper.values[0];
                if (controlValue.name != "minValue")
                    throw new Exception("minValue control value not found");

                controlValue.value = "0.02";

                controlWrapper.values.Add(new ValueWrapper {name = "tooltipText", value = LOC_SettingsTooltip });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KSPCF:LowerMinPhysicsDTPerFrame] Control not found at predetermined index, falling back to manual search ({e.Message})...");
                foreach (SettingsSetup.MainTab tab in __instance.setupPrefab.setup.tabs)
                {
                    if (tab.window.prefab is ReflectedSettingsWindow settingsWindow)
                    {
                        foreach (TabWrapper tabWrapper in settingsWindow.tabs)
                        {
                            foreach (SubTabWrapper subTabWrapper in tabWrapper.tabs)
                            {
                                foreach (ControlWrapper controlWrapper in subTabWrapper.controls)
                                {
                                    if (controlWrapper.settingName == "PHYSICS_FRAME_DT_LIMIT")
                                    {
                                        foreach (ValueWrapper controlWrapperValue in controlWrapper.values)
                                        {
                                            if (controlWrapperValue.name == "minValue")
                                            {
                                                controlWrapperValue.value = "0.02";
                                                return;
                                            }
                                        }

                                        controlWrapper.values.Add(new ValueWrapper { name = "tooltipText", value = LOC_SettingsTooltip });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        static void ReflectedSettingsWindow_SetupReflectionValues_Prefix(object instance, List<ValueWrapper> values)
        {
            for (int i = values.Count; i-- > 0;)
            {
                ValueWrapper valueWrapper = values[i];
                if (valueWrapper.name == "tooltipText")
                {
                    TooltipController_Text tooltip = StaticHelpers.AddUITooltip(((Component)instance).gameObject);
                    tooltip.continuousUpdate = false;
                    tooltip.textString = valueWrapper.value;
                    values.RemoveAt(i);
                    break;
                }
            }
        }
    }
}