using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    class StockAlarmDescPreserveLineBreak : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(AlarmTypeBase), nameof(AlarmTypeBase.OnSave)),
                this));
        }

        static void AlarmTypeBase_OnSave_Prefix(AlarmTypeBase __instance)
        {
            __instance.description = __instance.description.Replace("\r", @"\r");
            __instance.description = __instance.description.Replace("\n", @"\n");
            __instance.description = __instance.description.Replace("\t", @"\t");
        }
    }
}
