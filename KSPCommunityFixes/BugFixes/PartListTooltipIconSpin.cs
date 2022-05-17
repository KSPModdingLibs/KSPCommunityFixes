using System;
using HarmonyLib;
using KSP.UI.Screens.Editor;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    class PartListTooltipIconSpin : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartListTooltipController), nameof(PartListTooltipController.CreateTooltip)),
                this));
        }

        static void PartListTooltipController_CreateTooltip_Postfix(PartListTooltipController __instance, PartListTooltip tooltip)
        {
            if (!__instance.isGrey)
                tooltip.isGrey = false;
        }

    }
}
