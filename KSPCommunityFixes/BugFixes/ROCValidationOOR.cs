using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

// Note : ideally this should be a transpiler, but I'm lazy...

namespace KSPCommunityFixes
{
    public class ROCValidationOOR : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        private static Func<ROCManager, string, CelestialBody> ValidCelestialBody;
        private static Func<ROCManager, CelestialBody, string, bool> ValidCBBiome;

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ROCManager), "ValidateCBBiomeCombos"),
                this));

            ValidCelestialBody = AccessTools.MethodDelegate<Func<ROCManager, string, CelestialBody>>(AccessTools.Method(typeof(ROCManager), "ValidCelestialBody"));
            ValidCBBiome = AccessTools.MethodDelegate<Func<ROCManager, CelestialBody, string, bool>>(AccessTools.Method(typeof(ROCManager), "ValidCBBiome"));
        }

        static bool ROCManager_ValidateCBBiomeCombos_Prefix(ROCManager __instance)
        {
            List<ROCDefinition> rocDefinitions = __instance.rocDefinitions;

            for (int num = rocDefinitions.Count - 1; num >= 0; num--)
            {
                for (int num2 = rocDefinitions[num].myCelestialBodies.Count - 1; num2 >= 0; num2--)
                {
                    CelestialBody celestialBody = ValidCelestialBody(__instance, rocDefinitions[num].myCelestialBodies[num2].name);
                    if (celestialBody.IsNullOrDestroyed())
                    {
                        Debug.LogWarningFormat("[ROCManager]: Invalid CelestialBody Name {0} on ROC Definition {1}. Removed entry.", rocDefinitions[num].myCelestialBodies[num2].name, rocDefinitions[num].type);
                        rocDefinitions[num].myCelestialBodies.RemoveAt(num2);
                        continue; // missing in stock code
                    }
                    else
                    {
                        for (int num3 = rocDefinitions[num].myCelestialBodies[num2].biomes.Count - 1; num3 >= 0; num3--)
                        {
                            if (!ValidCBBiome(__instance, celestialBody, rocDefinitions[num].myCelestialBodies[num2].biomes[num3]))
                            {
                                Debug.LogWarningFormat("[ROCManager]: Invalid Biome Name {0} for Celestial Body {1} on ROC Definition {2}. Removed entry.", rocDefinitions[num].myCelestialBodies[num2].biomes[num3], rocDefinitions[num].myCelestialBodies[num2].name, rocDefinitions[num].type);
                                rocDefinitions[num].myCelestialBodies[num2].biomes.RemoveAt(num3);
                            }
                        }
                    }
                    if (rocDefinitions[num].myCelestialBodies[num2].biomes.Count == 0) // ArgumentOutOfRangeException for myCelestialBodies[num2] when the previous if evaluate to true
                    {
                        Debug.LogWarningFormat("[ROCManager]: No Valid Biomes for Celestial Body {0} on ROC Definition {1}. Removed entry.", rocDefinitions[num].myCelestialBodies[num2].name, rocDefinitions[num].type);
                        rocDefinitions[num].myCelestialBodies.RemoveAt(num2);
                    }
                }
                if (rocDefinitions[num].myCelestialBodies.Count == 0)
                {
                    Debug.LogWarningFormat("[ROCManager]: No Valid Celestial Bodies on ROC Definition {0}. Removed entry.", rocDefinitions[num].type);
                    rocDefinitions.RemoveAt(num);
                }
            }

            return false;
        }
    }
}
