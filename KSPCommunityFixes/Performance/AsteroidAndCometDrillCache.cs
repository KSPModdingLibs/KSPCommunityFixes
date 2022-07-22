// see https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/67

// The stock ModuleAsteroidDrill and ModuleCometDrill do a lot of unnecessary iterating over all
// PartModules on the vessel to see if an asteroid or comet is attached.
// ModuleResourceHarvester already keeps a cache of this information, and all the stock drills
// have all 3 modules.  This patch replaces the logic in the asteroid and comet drills with 
// lookups in the cache.  This has a bigger effect the more drills and parts/partmodules on the ship.

// Note from @Got : this introduce a behavior change in which asteroid/comet will be selected by drills
// on a multi-asteroid vessel when a new asteroid is attached. But since the whole multi-asteroid
// situation isn't handled correctly by stock anyway (all drills on the vessel will mine the first
// found one regardless of which asteroid part the drill is actually in contact with), we don't care.

using System;
using HarmonyLib;
using System.Collections.Generic;

namespace KSPCommunityFixes.Performance
{
    class AsteroidAndCometDrillCache : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(
                new PatchInfo(PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleAsteroidDrill), nameof(ModuleAsteroidDrill.IsSituationValid)),
                this));

            patches.Add(
                new PatchInfo(PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleAsteroidDrill), nameof(ModuleAsteroidDrill.GetAttachedPotato)),
                this));

            patches.Add(
                new PatchInfo(PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleCometDrill), nameof(ModuleCometDrill.IsSituationValid)),
                this));

            patches.Add(
                new PatchInfo(PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleCometDrill), nameof(ModuleCometDrill.GetAttachedPotato)),
                this));
        }

        static bool ModuleAsteroidDrill_IsSituationValid_Prefix(ModuleAsteroidDrill __instance, ref bool __result)
        {
            // easy check: if the current potato is still attached, we're valid
            if (__instance._potato.IsNotNullOrDestroyed() && __instance._potato.vessel == __instance._part.vessel)
            {
                __result = true;
                return false;
            }

            // the resource harvester module keeps a cache of whether the vessel has a comet or asteroid attached
            var resourceHarvester = __instance.part.FindModuleImplementing<ModuleResourceHarvester>();
            if (resourceHarvester.IsNotNullOrDestroyed() && resourceHarvester.partCountCache == __instance._part.vessel.parts.Count)
            {
                __result = !resourceHarvester.cachedWasNotAsteroid;
                return false;
            }

            // if the cache isn't available, just let the original run
            return true;
        }

        static bool ModuleAsteroidDrill_GetAttachedPotato_Prefix(ModuleAsteroidDrill __instance, ref Part __result)
        {
            // easy check: if the current potato is still attached, we're valid
            if (__instance._potato.IsNotNullOrDestroyed() && __instance._potato.vessel == __instance._part.vessel)
            {
                __result = __instance._potato;
                return false;
            }

            // the resource harvester module keeps a cache of whether the vessel has a comet or asteroid attached
            var resourceHarvester = __instance.part.FindModuleImplementing<ModuleResourceHarvester>();
            if (resourceHarvester.IsNotNullOrDestroyed() && resourceHarvester.partCountCache == __instance._part.vessel.parts.Count)
            {
                // if the cache says there's no asteroid on board, we're done
                if (resourceHarvester.cachedWasNotAsteroid)
                {
                    __result = null;
                    return false;
                }
            }

            // if the cache isn't available, just let the original run
            return true;
        }

        static bool ModuleCometDrill_IsSituationValid_Prefix(ModuleCometDrill __instance, ref bool __result)
        {
            // easy check: if the current potato is still attached, we're valid
            if (__instance._potato.IsNotNullOrDestroyed() && __instance._potato.vessel == __instance._part.vessel)
            {
                __result = true;
                return false;
            }

            // the resource harvester module keeps a cache of whether the vessel has a comet or asteroid attached
            var resourceHarvester = __instance.part.FindModuleImplementing<ModuleResourceHarvester>();
            if (resourceHarvester.IsNotNullOrDestroyed() && resourceHarvester.partCountCache == __instance._part.vessel.parts.Count)
            {
                __result = !resourceHarvester.cachedWasNotComet;
                return false;
            }

            // if the cache isn't available, just let the original run
            return true;
        }

        static bool ModuleCometDrill_GetAttachedPotato_Prefix(ModuleCometDrill __instance, ref Part __result)
        {
            // easy check: if the current potato is still attached, we're valid
            if (__instance._potato.IsNotNullOrDestroyed() && __instance._potato.vessel == __instance._part.vessel)
            {
                __result = __instance._potato;
                return false;
            }

            // the resource harvester module keeps a cache of whether the vessel has a comet or asteroid attached
            var resourceHarvester = __instance.part.FindModuleImplementing<ModuleResourceHarvester>();
            if (resourceHarvester.IsNotNullOrDestroyed() && resourceHarvester.partCountCache == __instance._part.vessel.parts.Count)
            {
                // if the cache says there's no Comet on board, we're done
                if (resourceHarvester.cachedWasNotComet)
                {
                    __result = null;
                    return false;
                }
            }

            // if the cache isn't available, just let the original run
            return true;
        }
    }
}
