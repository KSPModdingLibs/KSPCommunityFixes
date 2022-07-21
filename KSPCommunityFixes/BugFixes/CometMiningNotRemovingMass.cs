// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/66

using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    class CometMiningNotRemovingMass : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 2);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleComet), nameof(ModuleComet.GetModuleMass)),
                this));
        }

        static bool ModuleComet_GetModuleMass_Prefix(ModuleComet __instance, float defaultMass, out float __result)
        {
            __result = __instance.cometMass - defaultMass;
            return false;
        }
    }
}
