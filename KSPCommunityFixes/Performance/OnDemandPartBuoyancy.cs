using System;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class OnDemandPartBuoyancy : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        private const double CutoffThresholdSquared = 10.0 * 10.0;

        private static Vessel lastVessel;
        private static bool lastVesselIsCloseToSeaLevel;
        private static float lastFixedTime;
        

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartBuoyancy), "FixedUpdate"),
                this));

            GameEvents.onLevelWasLoaded.Add(OnLevelLoaded);
        }

        private void OnLevelLoaded(GameScenes data)
        {
            lastVessel = null;
            lastFixedTime = 0f;
        }

        static bool PartBuoyancy_FixedUpdate_Prefix(PartBuoyancy __instance, Part ___part)
        {
            // - if part was in water previously, always let the integrator run.
            // - always let it run at least once 
            if (__instance.splashed || __instance.depth > 0.0 || __instance.body.IsNullRef())
                return true;

            Vessel vessel = ___part.vessel;

            // let stock code handle that case
            if (vessel.IsNullRef())
                return true;

            // avoid further processing if no ocean, but set PartBuoyancy.body to replicate stock behavior
            if (!vessel.mainBody.ocean)
            {
                __instance.body = vessel.mainBody;
                return false;
            }

            // only check each vessel once per FixedUpdate
            if (lastFixedTime != Time.fixedTime)
            {
                lastFixedTime = Time.fixedTime;
                lastVessel = null;
            }

            if (lastVessel != vessel)
            {
                lastVessel = vessel;
                // is vessel altitude minus its largest possible dimension less than 10m above sea level :
                lastVesselIsCloseToSeaLevel = vessel.altitude < 0.0 || vessel.altitude * vessel.altitude - vessel.vesselSize.sqrMagnitude < CutoffThresholdSquared;
            }

            return lastVesselIsCloseToSeaLevel;
        }
    }
}
