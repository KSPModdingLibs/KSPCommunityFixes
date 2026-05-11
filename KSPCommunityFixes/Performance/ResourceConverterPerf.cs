// The stock ModuleAsteroidDrill, ModuleCometDrill, and ModuleResourceHarvester
// each do a couple expensive operations twice per FixedUpdate. These don't change
// while FixedUpdate is running, so we can save quite a bit of time by caching
// them.
//
// The things we cache are:
// * Each and every ModuleResourceHarvester makes two calls to ResourceMap.GetAbundance
//   every FixedUpdate. The result of this only really depends on the vessel, resource
//   name, and harvester type, so we cache it so that it only gets done once per
//   frame (for a given cache key) instead of redoing it for every single module.
// * All 3 drill modules do two raycasts per FixedUpdate to check if they are touching
//   the target/ground. This doesn't change while they are running so we add a cache
//   that avoids the duplicate.
// * ResourceConverter.ProcessRecipe repeatedly ends up formatting a few different
//   strings using the resource displayName. This is expensive and there are a limited
//   number of resource display names so we cache the resulting localized strings.
//
// In addition, we also fix some other bugs that cause performance issues in stock:
// * ResourceConverter.GetDeltaTime has a check to tell whether it should be
//   operating in catchup mode. However, this is always off by ~5e-10 so it never
//   actually exits catchup mode. We fix that by allowing a 1% margin of error.

using HarmonyLib;
using KSP.Localization;
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
            AddPatch(PatchType.Transpiler, typeof(ResourceConverter), nameof(ResourceConverter.ProcessRecipe));
            AddPatch(PatchType.Override, typeof(BaseConverter), nameof(BaseConverter.GetDeltaTime));
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

        #region ResourceConverter Format Cache
        struct FormatCacheKey : IEquatable<FormatCacheKey>
        {
            public string template;
            public string name;

            public readonly bool Equals(FormatCacheKey other)
                => template == other.template
                && name == other.name;
            public readonly override bool Equals(object obj) => obj is FormatCacheKey key && Equals(key);
            public readonly override int GetHashCode() =>
                HashCode.Combine(template.GetHashCode(), name.GetHashCode());
        }

        static readonly Dictionary<FormatCacheKey, string> FormatCache = new Dictionary<FormatCacheKey, string>();

        static string LocalizerFormatCached(string template, string[] list)
        {
            switch (template)
            {
                case "#autoLOC_261332":
                    goto default;

                case "#autoLOC_261304":
                case "#autoLOC_261334":
                case "#autoLOC_261263":
                    var key = new FormatCacheKey
                    {
                        template = template,
                        name = list[0]
                    };

                    if (FormatCache.TryGetValue(key, out var value))
                        return value;

                    value = Localizer.Format(template, list);
                    FormatCache.Add(key, value);
                    return value;

                default:
                    return Localizer.Format(template, list);
            }
        }

        static IEnumerable<CodeInstruction> ResourceConverter_ProcessRecipe_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var formatMethod = SymbolExtensions.GetMethodInfo(() => Localizer.Format("", new string[] { }));
            var cachedMethod = SymbolExtensions.GetMethodInfo(() => LocalizerFormatCached("", new string[] { }));

            var matcher = new CodeMatcher(instructions);
            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Call, formatMethod))
                .Repeat(matcher => matcher.SetOperandAndAdvance(cachedMethod));

            return matcher.Instructions();
        }
        #endregion

        #region Fix GetDeltaTime Comparison Check
        static double BaseConverter_GetDeltaTime_Override(BaseConverter converter)
        {
            if (Time.timeSinceLevelLoad < 1f || !FlightGlobals.ready)
                return -1d;
            if (Math.Abs(converter.lastUpdateTime) < 1e-9)
            {
                converter.lastUpdateTime = Planetarium.GetUniversalTime();
                return -1.0;
            }

            try
            {
                double deltaT = Math.Min(
                    Planetarium.GetUniversalTime() - converter.lastUpdateTime,
                    ResourceUtilities.GetMaxDeltaTime());
                double fixedDeltaT = Math.Min(TimeWarp.fixedDeltaTime, 0.02);

                // We multiply fixedDeltaT by 1.01 because otherwise it tends to be
                // smaller than deltaT by ~5e-10, which makes it so that in stock
                // GetBestDeltaTime is almost always called.
                if (deltaT > fixedDeltaT * 1.01 && converter.catchupRetries < 10)
                {
                    double bestDeltaT = converter.GetBestDeltaTime(deltaT);
                    if (bestDeltaT < deltaT)
                    {
                        converter.catchupRetries++;
                        deltaT = Math.Max(fixedDeltaT, bestDeltaT);
                    }
                }

                converter.lastUpdateTime += deltaT;
                return deltaT;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RESOURCES] Error in BaseConverter.GetDeltaTime");
                Debug.LogException(ex);
                return 0.0;
            }
        }
        #endregion
    }
}

