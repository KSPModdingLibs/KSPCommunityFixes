﻿using System;
using HarmonyLib;
using System.Collections.Generic;

namespace KSPCommunityFixes.QoL
{
    class FairingMouseOverPersistence : BasePatch
    {
        protected override Version VersionMin => new Version(1, 10, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleProceduralFairing), nameof(ModuleProceduralFairing.OnSave))));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleProceduralFairing), nameof(ModuleProceduralFairing.OnLoad))));
        }

        private static void ModuleProceduralFairing_OnSave_Prefix(ModuleProceduralFairing __instance, ConfigNode node)
        {
            node.SetValue(nameof(ModuleProceduralFairing.isFadeLocked), __instance.isFadeLocked, true);
        }

        private static void ModuleProceduralFairing_OnLoad_Prefix(ModuleProceduralFairing __instance, ConfigNode node)
        {
            node.TryGetValue(nameof(ModuleProceduralFairing.isFadeLocked), ref __instance.isFadeLocked);
        }
    }
}
