using System;
using System.Collections.Generic;
using HarmonyLib;

namespace KSPCommunityFixes.BugFixes
{
    class StickySplashedFixer : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Vessel), nameof(Vessel.updateSituation)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.Die)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.Die)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.decouple)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.decouple)),
                this));
        }

        static bool Vessel_updateSituation_Prefix(Vessel __instance)
        {
			bool evaOnLadderOnOtherVessel;
            if (__instance.EVALadderVessel != __instance)
            {
                __instance.situation = __instance.evaController.LadderPart.vessel.situation;
                evaOnLadderOnOtherVessel = true;
            }
            else
            {
                evaOnLadderOnOtherVessel = false;
                if (__instance.situation == Vessel.Situations.PRELAUNCH)
                {
                    // Slower speed for leaving prelaunch in water since, well, boats are slow.
                    if (__instance.srfSpeed > (__instance.Splashed ? 1 : 2.5) && !__instance.precalc.isEasingGravity && !__instance.vesselSpawning)
                        __instance.situation = __instance.Splashed ? Vessel.Situations.SPLASHED : Vessel.Situations.LANDED;
                }
                else if (__instance.Landed)
                {
                    __instance.situation = Vessel.Situations.LANDED;
                }
                else if (__instance.Splashed)
                {
                    __instance.situation = Vessel.Situations.SPLASHED;
                }
                else
                {
                    if (__instance.staticPressurekPa > 0.0)
                    {
                        __instance.situation = Vessel.Situations.FLYING;
                    }
                    else if (__instance.orbit.eccentricity < 1.0 && __instance.orbit.ApR < __instance.mainBody.sphereOfInfluence)
                    {
                        if (__instance.orbit.PeA < (__instance.mainBody.atmosphere ? __instance.mainBody.atmosphereDepth : 0))
                        {
                            __instance.situation = Vessel.Situations.SUB_ORBITAL;
                        }
                        else
                        {
                            __instance.situation = Vessel.Situations.ORBITING;
                        }
                    }
                    else
                    {
                        __instance.situation = Vessel.Situations.ESCAPING;
                    }
                }
            }

            if (__instance.situation != __instance.lastSituation)
            {
                GameEvents.onVesselSituationChange.Fire(new GameEvents.HostedFromToAction<Vessel, Vessel.Situations>(__instance, __instance.lastSituation, __instance.situation));
                __instance.lastSituation = __instance.situation;
            }

            if (__instance.wasLadder != evaOnLadderOnOtherVessel)
            {
                __instance.wasLadder = evaOnLadderOnOtherVessel;
            }
            
            return false;
        }

        // We could optimize the below by setting up a coroutine that runs later in the frame
        // so a vessel isn't processed more than once if, say, multiple parts detach on the
        // same frame. But the landed/splash check isn't very expensive so I'm not worried.

        static void Part_Die_Prefix(Part __instance, out Vessel __state)
        {
            __state = __instance.vessel;
        }

        static void Part_Die_Postfix(Vessel __state)
        {
            if (__state.IsNotNullOrDestroyed() && __state.state != Vessel.State.DEAD)
                __state.UpdateLandedSplashed();
        }

        static void Part_decouple_Prefix(Part __instance, out Vessel __state)
        {
            __state = __instance.vessel;
        }

        static void Part_decouple_Postfix(Vessel __state)
        {
            if (__state.IsNotNullOrDestroyed() && __state.state != Vessel.State.DEAD)
                __state.UpdateLandedSplashed();

            // New vessel (on __instance) will run Initialize.
        }
    }
}
