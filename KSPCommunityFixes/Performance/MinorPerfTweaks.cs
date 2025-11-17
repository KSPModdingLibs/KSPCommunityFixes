using Contracts.Predicates;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    internal class MinorPerfTweaks : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, AccessTools.PropertyGetter(typeof(FlightGlobals), nameof(FlightGlobals.fetch)), nameof(FlightGlobals_fetch_Override));

            AddPatch(PatchType.Override, typeof(Part), nameof(Part.isKerbalEVA));

            AddPatch(PatchType.Override, typeof(VolumeNormalizer), nameof(VolumeNormalizer.Update));

            AddPatch(PatchType.Transpiler, typeof(PQS), nameof(PQS.StartSphere));
        }

        // When FlightGlobals._fetch is null/destroyed, the stock "fetch" getter fallback to a FindObjectOfType()
        // call. This is extremly slow, and account for ~10% of the total loading time (7 seconds) of the total
        // launch > main menu on stock + BDB install, due to being called during part compilation.
        // The _fetch field is acquired from Awake() and set to null in OnDestroy(), so there is no real reason for this.
        // The only behavior change I can think of would be something calling fetch in between the OnDestroy()
        // call and the effective destruction of the native object. In any case, this can be qualified as a bug,
        // as the flightglobal instance left accessible will be in quite invalid state.
        private static FlightGlobals FlightGlobals_fetch_Override()
        {
            return FlightGlobals._fetch.IsNullOrDestroyed() ? null : FlightGlobals._fetch;
        }

        // Called (sometimes multiple times) in Part.FixedUpdate()
        public static bool Part_isKerbalEVA_Override(Part part)
        {
            part.cachedModules ??= new Dictionary<Type, PartModule>(10);

            if (!part.cachedModules.TryGetValue(typeof(KerbalEVA), out PartModule module))
            {
                if (part.modules == null)
                    return false;

                List<PartModule> modules = part.modules.modules;
                int moduleCount = modules.Count;
                for (int i = 0; i < moduleCount; i++)
                {
                    if (modules[i] is KerbalEVA)
                    {
                        module = modules[i];
                        break;
                    }
                }

                part.cachedModules[typeof(KerbalEVA)] = module;
            }

            return module.IsNotNullRef();
        }

        // setting AudioListener.volume is actually quite costly (0.7% of the frame time),
        // so avoid setting it when the value hasn't actually changed...
        private static void VolumeNormalizer_Update_Override(VolumeNormalizer vn)
        {
            float newVolume;
            if (GameSettings.SOUND_NORMALIZER_ENABLED)
            {
                vn.threshold = GameSettings.SOUND_NORMALIZER_THRESHOLD;
                vn.sharpness = GameSettings.SOUND_NORMALIZER_RESPONSIVENESS;
                AudioListener.GetOutputData(vn.samples, 0);
                vn.level = 0f;

                for (int i = 0; i < vn.sampleCount; i += 1 + GameSettings.SOUND_NORMALIZER_SKIPSAMPLES)
                    vn.level = Mathf.Max(vn.level, Mathf.Abs(vn.samples[i]));

                if (vn.level > vn.threshold)
                    newVolume = vn.threshold / vn.level;
                else
                    newVolume = 1f;

                newVolume = Mathf.Lerp(AudioListener.volume, newVolume * GameSettings.MASTER_VOLUME, vn.sharpness * Time.deltaTime);
            }
            else
            {
                newVolume = Mathf.Lerp(AudioListener.volume, GameSettings.MASTER_VOLUME, vn.sharpness * Time.deltaTime);
            }

            if (newVolume != vn.volume)
                AudioListener.volume = newVolume;

            vn.volume = newVolume;
        }

        private static PQSCache GetPQSCache(Type _)
        {
            var instance = PQSCache.Instance;
            if (!instance.IsNullOrDestroyed())
                return instance;

            // If we can't find a valid instance then fall back to FindObjectOfType
            return (PQSCache)UnityEngine.Object.FindObjectOfType(typeof(PQSCache));
        }

        // PQS.StartSphere calls out to FindObjectOfType. Since it is called a
        // number of times during scene switch, this can add up to >1s of overhead
        // during scene switches.
        //
        // PQSCache already tracks the active instance so this just replaces the
        // call to FindObjectOfType with one that reads from PQSCache.Instance.
        private static IEnumerable<CodeInstruction> PQS_StartSphere_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var findObjectOfTypeMethod = SymbolExtensions.GetMethodInfo(
                () => UnityEngine.Object.FindObjectOfType(typeof(PQSCache))
            );

            var matcher = new CodeMatcher(instructions);
            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Call, findObjectOfTypeMethod))
                .SetOperandAndAdvance(SymbolExtensions.GetMethodInfo(() => GetPQSCache(null)));

            return matcher.Instructions();
        }
    }
}
