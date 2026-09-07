// see https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/67

// The stock ModuleAsteroidDrill and ModuleCometDrill do a lot of unnecessary iterating over all
// PartModules on the vessel to see if an asteroid or comet is attached : IsSituationValid() is
// called from OnUpdate() and GetAttachedPotato() from PrepareRecipe(), and both walk every module
// of every part on the vessel.
// This patch replaces those lookups with the vessel wide module cache, which keeps the result
// (including the "there is no asteroid/comet on this vessel" one, the expensive case) until the
// vessel or the module list of one of its parts is modified.

// Note from @Got : this introduce a behavior change in which asteroid/comet will be selected by drills
// on a multi-asteroid vessel when a new asteroid is attached. But since the whole multi-asteroid
// situation isn't handled correctly by stock anyway (all drills on the vessel will mine the first
// found one regardless of which asteroid part the drill is actually in contact with), we don't care.
// Note that on such a vessel the selected asteroid/comet can also differ from the stock one, as the
// cache returns the first match in the vessel part list while stock searches that list backwards.

using System;

namespace KSPCommunityFixes.Performance
{
    class AsteroidAndCometDrillCache : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(ModuleAsteroidDrill), nameof(ModuleAsteroidDrill.IsSituationValid));

            AddPatch(PatchType.Override, typeof(ModuleAsteroidDrill), nameof(ModuleAsteroidDrill.GetAttachedPotato));

            AddPatch(PatchType.Override, typeof(ModuleCometDrill), nameof(ModuleCometDrill.IsSituationValid));

            AddPatch(PatchType.Override, typeof(ModuleCometDrill), nameof(ModuleCometDrill.GetAttachedPotato));
        }

        static bool ModuleAsteroidDrill_IsSituationValid_Override(ModuleAsteroidDrill __instance)
        {
            return __instance._part.vessel.HasPartModuleImplementingFast<ModuleAsteroid>();
        }

        static Part ModuleAsteroidDrill_GetAttachedPotato_Override(ModuleAsteroidDrill __instance)
        {
            // easy check: if the current potato is still attached, we're valid
            if (__instance._potato.IsNotNullOrDestroyed() && __instance._potato.vessel == __instance._part.vessel)
                return __instance._potato;

            ModuleAsteroid moduleAsteroid = __instance._part.vessel.FindPartModuleImplementingFast<ModuleAsteroid>();
            return moduleAsteroid.IsNullOrDestroyed() ? null : moduleAsteroid.part;
        }

        static bool ModuleCometDrill_IsSituationValid_Override(ModuleCometDrill __instance)
        {
            return __instance._part.vessel.HasPartModuleImplementingFast<ModuleComet>();
        }

        static Part ModuleCometDrill_GetAttachedPotato_Override(ModuleCometDrill __instance)
        {
            // easy check: if the current potato is still attached, we're valid
            if (__instance._potato.IsNotNullOrDestroyed() && __instance._potato.vessel == __instance._part.vessel)
                return __instance._potato;

            ModuleComet moduleComet = __instance._part.vessel.FindPartModuleImplementingFast<ModuleComet>();
            return moduleComet.IsNullOrDestroyed() ? null : moduleComet.part;
        }
    }
}
