// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/59

using System;
using HarmonyLib;
using System.Collections.Generic;

namespace KSPCommunityFixes.Performance
{
    class DisableMapOrbitsInFlight : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(OrbitRendererBase), nameof(OrbitRendererBase.LateUpdate)),
                this));
        }

        static bool OrbitRendererBase_LateUpdate_Prefix()
        {
            return MapView.fetch.updateMap;
        }
    }
}
