using HarmonyLib;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;

namespace KSPCommunityFixes.QoL
{
    class AutoSavedCraftNameAtLaunch : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Constructor(typeof(VesselSpawnDialog.VesselDataItem), new Type[] {typeof(FileInfo), typeof(bool), typeof(bool)}),
                this, nameof(VesselDataItem_Ctor_Postfix)));
        }

        static void VesselDataItem_Ctor_Postfix(VesselSpawnDialog.VesselDataItem __instance, FileInfo fInfo, bool steamItem)
        {
            if (steamItem)
                return;

            string fileName = fInfo.Name.Replace(fInfo.Extension, "");
            if (!string.Equals(fileName, __instance.name, StringComparison.OrdinalIgnoreCase))
            {
                __instance.name = $"{__instance.name} [{fileName}]";
            }
        }
    }
}
