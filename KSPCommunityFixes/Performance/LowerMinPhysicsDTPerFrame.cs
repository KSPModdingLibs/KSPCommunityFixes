// Implement min value of 0.02 instead of 0.03 for the "Max Physics Delta-Time Per Frame" main menu setting.
// See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/175

using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.UI.Screens.Settings;
using UnityEngine;
using static KSP.UI.Screens.Settings.ReflectedSettingsWindow;

namespace KSPCommunityFixes.Performance
{
    internal class LowerMinPhysicsDTPerFrame : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(
                new PatchInfo(PatchMethodType.Prefix,
                    AccessTools.Method(typeof(SettingsScreen), nameof(SettingsScreen.Awake)),
                    this));
        }
        static void SettingsScreen_Awake_Prefix(SettingsScreen __instance)
        {
            try
            {
                ReflectedSettingsWindow settingsWindow = (ReflectedSettingsWindow)__instance.setupPrefab.setup.tabs[0].window.prefab;

                ControlWrapper control = settingsWindow.tabs[1].tabs[1].controls[9];

                if (control.settingName != "PHYSICS_FRAME_DT_LIMIT")
                    throw new Exception("PHYSICS_FRAME_DT_LIMIT control not found");

                ValueWrapper controlValue = control.values[0];
                if (controlValue.name != "minValue")
                    throw new Exception("minValue control value not found");

                controlValue.value = "0.02";

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
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}