// The stock ModuleAsteroidDrill, ModuleCometDrill, and ModuleResourceHarvester
// each do a couple expensive operations twice per FixedUpdate. These don't change
// while FixedUpdate is running, so we can save quite a bit of time by caching
// them.
//
// The two main thing we cache are:
// * Each and every ModuleResourceHarvester makes two calls to ResourceMap.GetAbundance
//   every FixedUpdate. The result of this only really depends on the vessel, resource
//   name, and harvester type, so we cache it so that it only gets done once per
//   frame (for a given cache key) instead of redoing it for every single module.
// * All 3 drill modules do two raycasts per FixedUpdate to check if they are touching
//   the target/ground. This doesn't change while they are running so we add a cache
//   that avoids the duplicate.

using HarmonyLib;
using KSPCommunityFixes.Library;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    internal class ResourceConverterPerf : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Transpiler, typeof(ModuleResourceHarvester), nameof(ModuleResourceHarvester.PrepareRecipe));
            AddPatch(PatchType.Override, typeof(ModuleResourceHarvester), nameof(ModuleResourceHarvester.CheckForImpact));
            AddPatch(PatchType.Override, typeof(ModuleAsteroidDrill), nameof(ModuleAsteroidDrill.CheckForImpact));
            AddPatch(PatchType.Override, typeof(ModuleCometDrill), nameof(ModuleCometDrill.CheckForImpact));
        }

        #region Abundance Cache
        struct AbundanceCacheKey : IEquatable<AbundanceCacheKey>
        {
            public int VesselID;
            public HarvestTypes ResourceType;
            public string ResourceName;

            public readonly bool Equals(AbundanceCacheKey other)
                => VesselID == other.VesselID
                && ResourceType == other.ResourceType
                && ResourceName == other.ResourceName;
            public readonly override bool Equals(object obj) => obj is AbundanceCacheKey key && Equals(key);
            public readonly override int GetHashCode()
                => HashCode.Combine(VesselID, (int)ResourceType, ResourceName.GetHashCode());
        }

        private static int AbundanceCacheFrame = -1;
        private static readonly Dictionary<AbundanceCacheKey, float> AbundanceCache = new Dictionary<AbundanceCacheKey, float>();

        static float GetAbundanceCached(
            ResourceMap map,
            AbundanceRequest request,
            PartModule module)
        {
            var vessel = module.vessel;
            var cacheKey = new AbundanceCacheKey
            {
                VesselID = vessel.GetInstanceID(),
                ResourceType = request.ResourceType,
                ResourceName = request.ResourceName
            };

            var frameCount = Time.frameCount;
            if (AbundanceCacheFrame != frameCount)
            {
                AbundanceCache.Clear();
                AbundanceCacheFrame = frameCount;
            }
            else if (AbundanceCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var abundance = map.GetAbundance(request);
            AbundanceCache.Add(cacheKey, abundance);
            return abundance;
        }

        static IEnumerable<CodeInstruction> ModuleResourceHarvester_PrepareRecipe_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getAbundanceMethod = SymbolExtensions.GetMethodInfo<ResourceMap>(map => map.GetAbundance(default));
            var checkForImpactMethod = SymbolExtensions.GetMethodInfo<ModuleResourceHarvester>(module => module.CheckForImpact());
            var getAbundanceCached = SymbolExtensions.GetMethodInfo(() => GetAbundanceCached(null, default, null));

            var matcher = new CodeMatcher(instructions);

            // Replace GetAbundance with our own cached version
            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Callvirt, getAbundanceMethod))
                .ThrowIfInvalid("Unable to find call to ResourceMap.GetAbundance")
                .RemoveInstruction()
                .Insert(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, getAbundanceCached)
                );

            return matcher.Instructions();
        }
        #endregion

        #region Drill Impact Cache
        static float ImpactCacheTime = float.NaN;
        static Vector3 ImpactPosition = new Vector3(float.NaN, float.NaN, float.NaN);
        static Vector3 ImpactDirection = new Vector3(float.NaN, float.NaN, float.NaN);
        static float ImpactRange = float.NaN;
        static int ImpactLayerMask = 0;

        static RaycastHit? ImpactCacheValue = null;

        static RaycastHit? CheckForImpactCached(Transform transform, float range, int layerMask = -5)
        {
            if (transform.IsNullOrDestroyed())
                return null;

            var position = transform.position;
            var direction = transform.forward;
            var fixedTime = Time.fixedTime;
            
            if (ImpactCacheTime == fixedTime
                && ImpactPosition == position
                && ImpactDirection == direction
                && ImpactRange == range
                && ImpactLayerMask == layerMask)
            {
                return ImpactCacheValue;
            }

            var result = Physics.Raycast(
                new Ray(position, direction),
                out var hitInfo,
                range,
                layerMask);

            ImpactCacheTime = fixedTime;
            ImpactPosition = position;
            ImpactDirection = direction;
            ImpactRange = range;
            ImpactLayerMask = layerMask;

            if (result)
                ImpactCacheValue = hitInfo;
            else
                ImpactCacheValue = null;

            return ImpactCacheValue;
        }

        static bool ModuleResourceHarvester_CheckForImpact_Override(ModuleResourceHarvester module)
        {
            if (string.IsNullOrEmpty(module.ImpactTransform))
                return true;
            if (module.impactTransformCache.IsNullOrDestroyed())
                return true;

            var hitInfo = CheckForImpactCached(
                module.impactTransformCache,
                module.ImpactRange,
                1 << 15);

            if (hitInfo is RaycastHit hit)
            {
                module.impactHitInfo = hit;
                return true;
            }
            else
            {
                module.impactHitInfo = default;
                return false;
            }
        }

        static bool ModuleAsteroidDrill_CheckForImpact_Override(ModuleAsteroidDrill module)
        {
            if (string.IsNullOrEmpty(module.ImpactTransform))
                return true;
            if (module.impactTransformCache.IsNullOrDestroyed())
                return true;

            var hitInfo = CheckForImpactCached(
                module.impactTransformCache,
                module.ImpactRange);

            if (hitInfo is RaycastHit hit)
            {
                module.impactHitInfo = hit;
                return hit.collider.gameObject.GetComponentUpwards<ModuleAsteroid>() != null;
            }
            else
            {
                module.impactHitInfo = default;
                return false;
            }
        }

        static bool ModuleCometDrill_CheckForImpact_Override(ModuleCometDrill module)
        {
            if (string.IsNullOrEmpty(module.ImpactTransform))
                return true;
            if (module.impactTransformCache.IsNullOrDestroyed())
                return true;

            var hitInfo = CheckForImpactCached(
                module.impactTransformCache,
                module.ImpactRange);

            if (hitInfo is RaycastHit hit)
            {
                module.impactHitInfo = hit;
                return hit.collider.gameObject.GetComponentUpwards<ModuleComet>() != null;
            }
            else
            {
                module.impactHitInfo = default;
                return false;
            }
        }
        #endregion
    }
}

