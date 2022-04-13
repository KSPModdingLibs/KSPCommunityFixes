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

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), "DisplayExtendedInfo"),
                this));
        }

        static bool PartListTooltip_DisplayExtendedInfo_Prefix(PartListTooltip __instance)
        {
            if (__instance.upgrade != null && __instance.HasExtendedInfo)
            {
                __instance.panelExtended.SetActive(false);
                return false;
            }

            return true;
        }
    }
}
