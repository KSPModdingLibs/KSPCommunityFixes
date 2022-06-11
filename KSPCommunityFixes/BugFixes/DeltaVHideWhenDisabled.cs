using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.UI.Screens;
using KSP.UI.Screens.Flight;

namespace KSPCommunityFixes
{
    public class DeltaVHideWhenDisabled : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(StageGroup), "Awake"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(StageManager), "Awake"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(StageGroup), "ToggleInfoPanel", new Type[] { typeof(bool) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(NavBallBurnVector), "onGameSettingsApplied"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(NavBallBurnVector), "onGameSettingsApplied"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(NavBallBurnVector), "LateUpdate"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(NavBallBurnVector), "LateUpdate"),
                this));
        }

        static void StageGroup_Awake_Postfix(StageGroup __instance)
        {
            if (GameSettings.DELTAV_APP_ENABLED == false && GameSettings.DELTAV_CALCULATIONS_ENABLED == false)
            {
                __instance.DisableDeltaVHeading();
            }
        }

        static void StageManager_Awake_Postfix()
        {
            if (GameSettings.DELTAV_APP_ENABLED == false && GameSettings.DELTAV_CALCULATIONS_ENABLED == false)
            {
                // Note: not using __instance here because the singleton pattern
                // destroys the new object in favor of the old, if Instance
                // is non-null during Awake.
                StageManager.Instance.DisableDeltaVTotal();
                StageManager.Instance.deltaVTotalButton.gameObject.SetActive(false);
                // hiding instead of disabling because stock re-enable it from a bunch of places :
                StageManager.Instance.deltaVTotalSection.preferredHeight = 0f; 
            }
        }

        static bool StageGroup_ToggleInfoPanel_Prefix(ref bool showPanel)
        {
            // If we don't have delta V enabled, just short-circuit any attempt to toggle the info panel.
            if (showPanel && GameSettings.DELTAV_APP_ENABLED == false && GameSettings.DELTAV_CALCULATIONS_ENABLED == false)
                return false;

            // but Just In Case let it close.
            return true;
        }

        static void NavBallBurnVector_onGameSettingsApplied_Prefix(out bool __state)
        {
            __state = GameSettings.EXTENDED_BURNTIME;

            if (GameSettings.DELTAV_APP_ENABLED == false && GameSettings.DELTAV_CALCULATIONS_ENABLED == false)
                GameSettings.EXTENDED_BURNTIME = false;
        }

        static void NavBallBurnVector_onGameSettingsApplied_Postfix(bool __state)
        {
            GameSettings.EXTENDED_BURNTIME = __state;
        }

        static void NavBallBurnVector_LateUpdate_Prefix(out bool __state)
        {
            __state = GameSettings.EXTENDED_BURNTIME;

            if (GameSettings.DELTAV_APP_ENABLED == false && GameSettings.DELTAV_CALCULATIONS_ENABLED == false)
                GameSettings.EXTENDED_BURNTIME = false;
        }

        static void NavBallBurnVector_LateUpdate_Postfix(bool __state)
        {
            GameSettings.EXTENDED_BURNTIME = __state;
        }
    }
}
