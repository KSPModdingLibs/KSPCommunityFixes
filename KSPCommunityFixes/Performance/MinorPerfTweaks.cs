using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    internal class MinorPerfTweaks : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Override,
                AccessTools.Method(typeof(Part), nameof(Part.isKerbalEVA))));

            patches.Add(new PatchInfo(
                PatchMethodType.Override,
                AccessTools.Method(typeof(VolumeNormalizer), nameof(VolumeNormalizer.Update))));
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
        [TranspileInDebug]
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
    }
}
