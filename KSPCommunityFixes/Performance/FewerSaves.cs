using System;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;
using KSP.UI;
using System.Reflection.Emit;

namespace KSPCommunityFixes
{
    public class FewerSaves : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ACSceneSpawner), nameof(ACSceneSpawner.onACDespawn)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(AdministrationSceneSpawner), nameof(AdministrationSceneSpawner.onAdminDespawn)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(MCSceneSpawner), nameof(MCSceneSpawner.OnMCDespawn)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(RDSceneSpawner), nameof(RDSceneSpawner.onRDDespawn)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(SpaceTracking), "OnVesselDeleteConfirm"),
                this));
        }

        static bool ACSceneSpawner_onACDespawn_Prefix(ACSceneSpawner __instance)
        {
            UIMasterController.Instance.RemoveCanvas(__instance.ACScreenPrefab);
            MusicLogic.fetch.UnpauseWithCrossfade();
            return false;
        }

        static bool AdministrationSceneSpawner_onAdminDespawn_Prefix(AdministrationSceneSpawner __instance)
        {
            UIMasterController.Instance.RemoveCanvas(__instance.AdministrationScreenPrefab);
            MusicLogic.fetch.UnpauseWithCrossfade();
            return false;
        }

        static bool MCSceneSpawner_OnMCDespawn_Prefix(MCSceneSpawner __instance)
        {
            UIMasterController.Instance.RemoveCanvas(__instance.missionControlPrefab);
            MusicLogic.fetch.UnpauseWithCrossfade();
            return false;
        }

        static bool RDSceneSpawner_onRDDespawn_Prefix(RDSceneSpawner __instance)
        {
            UIMasterController.Instance.RemoveCanvas(__instance.RDScreenPrefab);
            RenderSettings.defaultReflectionMode = __instance.oldReflectionMode;
            RenderSettings.customReflection = __instance.oldReflection;
            MusicLogic.fetch.UnpauseWithCrossfade();
            return false;
        }

        static IEnumerable<CodeInstruction> SpaceTracking_OnVesselDeleteConfirm_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int startIndex = -1;
            int endIndex = -1;

            var codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldstr &&
                    codes[i].operand as string == "persistent")
                {
                    startIndex = i;

                    for (int j = startIndex; j < codes.Count; j++)
                    {
                        if (codes[j].opcode == OpCodes.Ldarg_0)
                        {
                            endIndex = j;
                            break;
                        }
                    }
                    break;
                }
            }

            if (startIndex > -1 && endIndex > -1)
            {
                // Cuts out the section about GamePersistence.SaveGame()
                codes.RemoveRange(startIndex, endIndex - startIndex);
            }

            return codes;
        }
    }
}
