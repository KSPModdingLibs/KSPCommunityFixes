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

        private static bool useStockCheck = true;

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(TimeWarp), nameof(TimeWarp.getMaxOnRailsRateIdx)),
                this));
        }

        static bool TimeWarp_getMaxOnRailsRateIdx_Prefix(TimeWarp __instance, int tgtRateIdx, bool lookAhead, out ClearToSaveStatus reason, out int __result)
        {
            reason = ClearToSaveStatus.CLEAR;
            __result = tgtRateIdx;

            if (useStockCheck)
                return true;

            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (tgtRateIdx <= 0 || !HighLogic.LoadedSceneIsFlight || activeVessel.IsNullOrDestroyed())
                return false;

            // timewarp not allowed when EVA on a ladder
            List<Vessel> loadedVessels = FlightGlobals.VesselsLoaded;
            for (int i = loadedVessels.Count; i-- > 0;)
            {
                Vessel vessel = loadedVessels[i];
                if (vessel.isEVA && vessel.GetComponent<KerbalEVA>().OnALadder)
                {
                    reason = ClearToSaveStatus.NOT_WHILE_ON_A_LADDER;
                    __result = 0;
                    return false;
                }
            }

            CelestialBody mainBody = activeVessel.mainBody;

            if (activeVessel.LandedOrSplashed)
            {
                if (activeVessel.srf_velocity.sqrMagnitude > 0.090000003576278687)
                {
                    reason = ClearToSaveStatus.NOT_WHILE_MOVING_OVER_SURFACE;
                    __result = 0;
                }
                return false;
            }

            if (activeVessel.geeForce > GThreshold)
            {
                reason = ClearToSaveStatus.NOT_UNDER_ACCELERATION;
                __result = 0;
                return false;
            }

            double vesselAltitude = FlightGlobals.getAltitudeAtPos(activeVessel.transform.position, mainBody);
            if (FlightGlobals.getStaticPressure(vesselAltitude, mainBody) > 0.0)
            {
                reason = ClearToSaveStatus.NOT_IN_ATMOSPHERE;
                __result = 0;
                return false;
            }

            bool vesselHasPatchedConics = activeVessel.PatchedConicsAttached;
            if (GameSettings.ORBIT_WARP_DOWN_AT_SOI && vesselHasPatchedConics && activeVessel.orbit.patchEndTransition != Orbit.PatchTransitionType.FINAL)
            {
                int maxRateAtOrbitTransition = __instance.ClampRateToOrbitTransitions(tgtRateIdx, activeVessel.orbit, 3, 50);
                if (maxRateAtOrbitTransition != tgtRateIdx)
                {
                    reason = ClearToSaveStatus.ORBIT_EVENT_IMMINENT;
                    Debug.Log("Orbit event imminent. Dropping TimeWarp to max limit: " + __instance.warpRates[3] + "x");
                    __result = maxRateAtOrbitTransition;
                    return false;
                }
            }

            if (lookAhead && vesselHasPatchedConics)
            {
                Orbit encounterPatch = null;
                bool encounterPatchIsOrbit = true;
                double encounterRadius = 0.0;
                Orbit currentPatch = activeVessel.orbit;
                while (currentPatch != null)
                {
                    CelestialBody body = currentPatch.referenceBody;

                    encounterRadius = body.Radius;

                    if (body.atmosphere)
                        encounterRadius += body.atmosphereDepth;

                    if (body.hasSolidSurface && body.pqsController.radiusMax > encounterRadius)
                        encounterRadius = body.pqsController.radiusMax;

                    encounterRadius += GameSettings.ORBIT_WARP_PEMODE_SURFACE_MARGIN;

                    double periapsis = currentPatch.PeR;
                    if (double.IsFinite(periapsis) && periapsis < encounterRadius)
                    {
                        encounterPatch = currentPatch;
                        break;
                    }

                    if (currentPatch.patchEndTransition == Orbit.PatchTransitionType.FINAL)
                        break;

                    currentPatch = currentPatch.nextPatch;
                    encounterPatchIsOrbit = false;
                }

                if (encounterPatch != null)
                {
                    double ut = Planetarium.GetUniversalTime();
                    CelestialBody encounterBody = encounterPatch.referenceBody;
                    double encounterBodyRadius = encounterBody.Radius;
                    int rateIdx = tgtRateIdx;

                    double tgtRateAltitudeLimit = __instance.GetAltitudeLimit(rateIdx, encounterBody);
                    double tgtRateRadiusLimit = encounterBodyRadius + tgtRateAltitudeLimit;

                    double tgtRateTA = currentPatch.TrueAnomalyAtRadiusSimple(tgtRateRadiusLimit);
                    double tgtRateUT = double.MaxValue;
                    if (double.IsFinite(tgtRateTA)) // will be NaN if no intercept
                    {
                        double tgtRateNextDT = currentPatch.GetDTforTrueAnomaly(-tgtRateTA, 0.0); // negate the TA to get the first intercept
                        if (double.IsFinite(currentPatch.period))
                            tgtRateNextDT = WrapAround(tgtRateNextDT, currentPatch.period);

                        double patchEpoch = encounterPatchIsOrbit ? ut : encounterPatch.epoch;
                        tgtRateUT = patchEpoch + tgtRateNextDT;
                    }

                    double fixedDT = Time.fixedDeltaTime;
                    double nextUT = ut + __instance.warpRates[rateIdx] * fixedDT;
                    double nextAltitude = encounterPatch.getRelativePositionAtUT(nextUT).magnitude - encounterBodyRadius;
                    while (rateIdx > 0 && (nextAltitude < tgtRateAltitudeLimit || nextUT > tgtRateUT))
                    {
                        rateIdx--;
                        nextUT = ut + __instance.warpRates[rateIdx] * fixedDT;
                        nextAltitude = encounterPatch.getRelativePositionAtUT(nextUT).magnitude - encounterBodyRadius;
                        tgtRateAltitudeLimit = __instance.GetAltitudeLimit(rateIdx, encounterBody);
                    }

                    if (rateIdx < tgtRateIdx)
                    {
                        __instance.minPeAllowed = encounterRadius - encounterBodyRadius;
                        reason = ClearToSaveStatus.NOT_WHILE_ABOUT_TO_CRASH;
                        __result = rateIdx;
                        return false;
                    }

                    if (encounterBody.atmosphere)
                    {
                        double nextStaticPressure = FlightGlobals.getStaticPressure(nextAltitude, mainBody);
                        while (rateIdx > 0 && nextStaticPressure > 0.0)
                        {
                            rateIdx--;
                            nextUT = ut + __instance.warpRates[rateIdx] * fixedDT;
                            nextAltitude = encounterPatch.getRelativePositionAtUT(nextUT).magnitude - encounterBodyRadius;
                            nextStaticPressure = FlightGlobals.getStaticPressure(nextAltitude, mainBody);
                        }

                        if (rateIdx < tgtRateIdx)
                        {
                            reason = ClearToSaveStatus.NOT_IN_ATMOSPHERE;
                            __result = rateIdx;
                            return false;
                        }
                    }
                }
            }

            return false;
        }

        public static double WrapAround(double value, double around)
        {
            return (value % around + around) % around;
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FixWarpThroughBodies : MonoBehaviour
    {
        private double nextEncounterUT1;
        private double nextEncounterUT2;
        private bool setWarp = false;

        void FixedUpdate()
        {

            TimeWarp timeWarp = TimeWarp.fetch;
            Vessel v = FlightGlobals.ActiveVessel;
            Orbit currentPatch = v.orbit;
            Orbit encounterPatch = currentPatch;

            double patchEpoch = Planetarium.GetUniversalTime(); // first patch needs this
            nextEncounterUT1 = double.MaxValue;
            nextEncounterUT2 = double.MaxValue;
            while (currentPatch != null)
            {
                CelestialBody body = currentPatch.referenceBody;

                double bodyCollisionRadius = body.Radius;

                if (body.atmosphere)
                    bodyCollisionRadius += body.atmosphereDepth;

                // pqsController.radiusMax is I think guranteed to be greater than the radius at the highest peak,
                // and not be a huge margin (200 - 2000m) unless there is a lot of unused range on the heightmap
                // An exact value is available in meshVertMax, but only if the PQS is active, which would make things
                // inconsistent when checking against other bodies than the main body.
                if (body.pqsController.IsNotNullOrDestroyed() && body.pqsController.radiusMax > bodyCollisionRadius)
                    bodyCollisionRadius = body.pqsController.radiusMax;

                // Now we want to replicate stock max warp behavior, assuming that GameSettings.ORBIT_WARP_MAXRATE_MODE
                // is set to the default MaxRailsRateMode.PeAltitude mode. There is no ingame switch for this setting,
                // outside of manual editing the config file, and the other mode is just silly, I doubt anybody would
                // ever want to use it.

                double periapsis = currentPatch.PeR;
                if (double.IsFinite(periapsis) && periapsis <= bodyCollisionRadius)
                {
                    double encounterTA = currentPatch.TrueAnomalyAtRadiusSimple(bodyCollisionRadius);
                    if (double.IsFinite(encounterTA)) // will be NaN if no intercept
                    {
                        double encounter1DT = currentPatch.GetDTforTrueAnomaly(encounterTA, 0.0);
                        double encounter2DT = currentPatch.GetDTforTrueAnomaly(-encounterTA, 0.0);

                        if (double.IsFinite(currentPatch.period))
                        {
                            encounter1DT = WrapAround(encounter1DT, currentPatch.period);
                            encounter2DT = WrapAround(encounter2DT, currentPatch.period);
                        }


                        //double patchEncounterUT = patchEpoch + Math.Min(encounter1DT, encounter2DT);
                        nextEncounterUT1 = patchEpoch + encounter1DT;
                        nextEncounterUT2 = patchEpoch + encounter2DT;



                        //double patchEncounterUT = patchEpoch + encounter1DT;

                        //if (patchEncounterUT < nextEncounterUT)
                        //{
                        //    nextEncounterUT = patchEncounterUT;
                        //    encounterPatch = currentPatch;
                        //}
                    }
                }

                if (currentPatch.patchEndTransition == Orbit.PatchTransitionType.FINAL)
                    break;

                currentPatch = currentPatch.nextPatch;
                patchEpoch = currentPatch.epoch;
            }

            if (nextEncounterUT1 == double.MaxValue)
                nextEncounterUT1 = double.NaN;

            if (nextEncounterUT2 == double.MaxValue)
                nextEncounterUT2 = double.NaN;

            //double fixedDT = Time.fixedDeltaTime;
            //double nextDT = timeWarp.warpRates[timeWarp.current_rate_index] * fixedDT;
            //double ut = Planetarium.GetUniversalTime();
            //double nextUT = ut + nextDT;

            //if (setWarp && nextUT >= nextEncounterUT)
            //{
            //    double encounterAltitude = encounterPatch.getRelativePositionAtUT(nextEncounterUT).magnitude - encounterPatch.referenceBody.Radius;
            //    int maxWarpRateIdx = timeWarp.GetMaxRateForAltitude(encounterAltitude, encounterPatch.referenceBody);
            //    if (maxWarpRateIdx < timeWarp.current_rate_index)
            //        timeWarp.setRate(maxWarpRateIdx, true, true, true, true);
            //}
        }

        void OnGUI()
        {
            GUI.Label(new Rect(50, 120, 400, 20), "E1: " + KSPUtil.PrintDateDelta(nextEncounterUT1 - Planetarium.GetUniversalTime(), true, true));
            GUI.Label(new Rect(50, 140, 400, 20), "E2: " + KSPUtil.PrintDateDelta(nextEncounterUT2 - Planetarium.GetUniversalTime(), true, true));
        }

        public static double WrapAround(double value, double around)
        {
            return ((value % around) + around) % around;
        }
    }
}
