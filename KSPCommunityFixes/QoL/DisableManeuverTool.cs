using HarmonyLib;
using KSP.UI;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.QoL
{
    class DisableManeuverTool : BasePatch
    {
        public static string LOC_SettingsTooltip = 
            "The stock maneuver tool can cause severe lag and stutter issues," +
            "\nespecially with Kopernicus modified systems." +
            "\nThis option allow to disable it entirely";

        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            ConfigNode defaultsNode = KSPCommunityFixes.SettingsNode.GetNode("MANEUVER_TOOL_DEFAULTS");

            if (defaultsNode != null)
            {
                defaultsNode.TryGetValue("ManeuverToolEnabledByDefault", ref enableManeuverTool);
                defaultsNode.TryGetValue("ManeuverToolAlwaysDisabled", ref alwaysDisabled);
            }

            if (alwaysDisabled)
                enableManeuverTool = false;

            AddPatch(PatchType.Prefix, typeof(ManeuverTool), "OnAppAboutToStart");
        }

        public static bool alwaysDisabled = false;
        public static bool enableManeuverTool = true;

        protected override void OnLoadData(ConfigNode node)
        {
            if (alwaysDisabled)
                return;

            node.TryGetValue(nameof(enableManeuverTool), ref enableManeuverTool);
        }

        private static bool ManeuverTool_OnAppAboutToStart_Prefix(ref bool __result)
        {
            if (enableManeuverTool)
                return true;

            __result = false;
            return false;
        }

        public static void OnToggleApp(bool enabled)
        {
            enableManeuverTool = enabled;

            if (!enableManeuverTool && ManeuverTool.Instance.IsNotNullOrDestroyed())
            {
                UnityEngine.Object.Destroy(ManeuverTool.Instance.gameObject);
            }
            else if (enableManeuverTool && ManeuverTool.Instance.IsNullOrDestroyed())
            {
                UIAppSpawner appSpawner = UIMasterController.Instance.transform.Find("PrefabSpawners")?.GetComponentInChildren<UIAppSpawner>();
                if (appSpawner.IsNotNullOrDestroyed())
                {
                    foreach (UIAppSpawner.AppWrapper appWrapper in appSpawner.apps)
                    {
                        if (appWrapper.prefab.GetComponent<ManeuverTool>().IsNotNullOrDestroyed() && appWrapper.scenes.Contains(HighLogic.LoadedScene))
                        {
                            appWrapper.instantiatedApp = UnityEngine.Object.Instantiate(appWrapper.prefab);
                            appWrapper.instantiatedApp.GetComponent<ManeuverTool>().ForceAddToAppLauncher();
                        }
                    }
                }
            }
        }
    }
}
