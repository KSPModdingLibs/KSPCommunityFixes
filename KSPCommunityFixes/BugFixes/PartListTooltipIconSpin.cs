using System;
using HarmonyLib;
using KSP.UI.Screens.Editor;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    class PartListTooltipIconSpin : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(PartListTooltipController), nameof(PartListTooltipController.CreateTooltip));
        }

        static void PartListTooltipController_CreateTooltip_Postfix(PartListTooltipController __instance, PartListTooltip tooltip)
        {
            if (!__instance.isGrey)
                tooltip.isGrey = false;
        }

    }
}
