using HarmonyLib;
using KSP.UI.Screens;
using KSP.Localization;
using Contracts;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.QoL
{
    class ShowContractFinishDates : BasePatch
    {

        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix, 
                AccessTools.Method(typeof(MissionControl), "UpdateInfoPanelContract"), 
                this));
        }

        private static void MissionControl_UpdateInfoPanelContract_Postfix(MissionControl __instance, Contract contract)
        {
            if (__instance.displayMode == MissionControl.DisplayMode.Archive)
            {
                // Find an autoloc for the status
                string stateStr = string.Empty;
                switch (contract.ContractState)
                {
                    case Contract.State.Failed: stateStr = "#autoLOC_900708"; break;
                    case Contract.State.Cancelled: stateStr = "#autoLOC_900711"; break;
                    case Contract.State.OfferExpired: stateStr = "#autoLOC_900714"; break;
                    case Contract.State.DeadlineExpired: stateStr = "#autoLOC_900715"; break;
                    case Contract.State.Declined: stateStr = "#autoLOC_900716"; break;
                    default:
                    case Contract.State.Completed: stateStr = "#autoLOC_900710"; break;
                }

                // Find the Prestige string. It's the first Param, so we find the first
                // place where text is formatted with the Params color
                string searchStr = "<b><color=#" + RUIutils.ColorToHex(RichTextUtil.colorParams) + ">";
                int idx = __instance.contractText.text.IndexOf(searchStr);
                if (idx >= 0)
                {
                    // Now skip after the double-newline
                    int insertionIdx = __instance.contractText.text.IndexOf("\n\n", idx);
                    if (insertionIdx > idx)
                    {
                        // Success! Splice around the date.
                        __instance.contractText.text = __instance.contractText.text.Substring(0, insertionIdx + 2)
                            + KSPRichTextUtil.TextDate(Localizer.Format("#autoLOC_266539"), contract.DateFinished)
                            + KSPRichTextUtil.TextDate(Localizer.Format(stateStr), contract.DateFinished)
                            + __instance.contractText.text.Substring(insertionIdx + 2);
                    }
                }
            }
        }
    }
}
