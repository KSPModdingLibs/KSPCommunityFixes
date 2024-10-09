using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using static TimeWarp;

namespace KSPCommunityFixes.BugFixes
{
    class TimeWarpBodyCollision : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(TimeWarp), nameof(TimeWarp.ClampRateToOrbitTransitions)),
                this));
        }

        static bool TimeWarp_ClampRateToOrbitTransitions_Prefix(TimeWarp __instance, int rate, Orbit obt, int maxAllowedSOITransitionRate, int secondsBeforeSOItransition, out int __result)
        {
            __result = rate;

            // the stock version of this method is designed to aid in ramping down timewarp when you approach a SOI change.
            // it fails to limit the warp rate if you suddenly jump to a very high warp rate while you have an SOI change in your trajectory
            // - doing this will often warp you all the way through the SOI and sometimes even through the body.
            // Instead, we treat the SOI transition like a "warp to here" point and use the existing logic for limiting the warp rate to get to that point
            if (obt.patchEndTransition != Orbit.PatchTransitionType.FINAL && rate > maxAllowedSOITransitionRate)
            {
                double warpDeltaTime = obt.EndUT - Planetarium.GetUniversalTime() - secondsBeforeSOItransition;
                __instance.getMaxWarpRateForTravel(warpDeltaTime, 1, 4, out var rateIdx);
                if (rate < rateIdx)
                {
                    __result = rate;
                }
                else
                {
                    __result = Math.Max(rateIdx, maxAllowedSOITransitionRate);
                }
            }

            return false;
        }
    }
}
