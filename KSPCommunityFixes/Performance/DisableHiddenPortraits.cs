// See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/84

// KerbalPortrait.OnEnable() will be called anytime the flight UI is re-activated,
// for example when going out of map view, screenshot mode, or IVA...
// It will forcefully start the coroutine responsible for rendering the portraits,
// without checking if the portrait is actually visible. We implement such a check
// here, using the last state of the RectContainementDetector, which *should* match
// the state before the flight UI was disabled as long as the whole PortraitViewport
// downward hierarchy is always enabled/disabled in a single atomic operation.
// It's a bit hacky, and *might* cause side issues, but I can't find a viable alternative.
// I tried triggering a delayed containement check from a coroutine, but this occasionally
// ends up with the portraits getting stuck in the "noise" state, presumably because it
// disable the portraits while they haven't transitionned yet out of the noise state...

using HarmonyLib;
using KSP.UI.Screens.Flight;
using KSP.UI.Util;
using System.Collections.Generic;

namespace KSPCommunityFixes.Performance
{
    internal class DisableHiddenPortraits : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(KerbalPortrait), nameof(KerbalPortrait.OnEnable));
        }

        private static bool KerbalPortrait_OnEnable_Prefix(KerbalPortrait __instance)
        {
            if (__instance.portraitMode == KerbalPortraitGallery.GalleryMode.EVA)
                return __instance.rectContainment.level >= RectUtil.ContainmentLevel.Full;

            return __instance.rectContainment.level >= RectUtil.ContainmentLevel.Partial;
        }
    }
}
