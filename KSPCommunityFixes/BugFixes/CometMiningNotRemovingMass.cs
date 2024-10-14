// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/66

using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    class CometMiningNotRemovingMass : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 2);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(ModuleComet), nameof(ModuleComet.GetModuleMass));
        }

        static bool ModuleComet_GetModuleMass_Prefix(ModuleComet __instance, float defaultMass, out float __result)
        {
            __result = __instance.cometMass - defaultMass;
            return false;
        }
    }
}
