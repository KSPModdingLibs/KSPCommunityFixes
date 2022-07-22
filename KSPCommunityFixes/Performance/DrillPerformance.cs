using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KSPCommunityFixes.Performance
{
    class DrillPerformance : BasePatch
    {
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
