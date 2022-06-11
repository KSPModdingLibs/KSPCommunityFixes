using HarmonyLib;
using KSP.UI.Screens.Editor;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class HidePartUpgradeExtendedInfo : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartListTooltipController), "CreateTooltip"),
                this));
        }

        static void PartListTooltipController_CreateTooltip_Postfix(ref PartListTooltip tooltip, ref KSP.UI.Screens.EditorPartIcon partIcon, PartListTooltipController __instance)
        {
            if (!partIcon.isPart)
            {
                tooltip.hasExtendedInfo = false;
                tooltip.DisplayExtendedInfo(false, __instance.GetTooltipHintText(tooltip));
            }
        }
    }
}
