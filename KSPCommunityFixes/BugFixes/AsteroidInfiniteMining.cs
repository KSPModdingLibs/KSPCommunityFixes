// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/51

using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    class AsteroidInfiniteMining : BasePatch
    {
        protected override Version VersionMin => new Version(1, 10, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.PropertySetter(typeof(ModuleSpaceObjectInfo), nameof(ModuleSpaceObjectInfo.currentMassVal)),
                this, nameof(ModuleSpaceObjectInfo_currentMassVal_Setter_Prefix)));
        }

        static bool ModuleSpaceObjectInfo_currentMassVal_Setter_Prefix(ModuleSpaceObjectInfo __instance, double value)
        {
            if (value <= 1e-9)
                value = 1e-8;

            __instance.currentMass = value.ToString("G17");
            return false;
        }
    }
}
