using System;
using System.Collections.Generic;
using HarmonyLib;
using Strategies;
using System.Reflection.Emit;

namespace KSPCommunityFixes.BugFixes
{
    class StrategyDuration : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.PropertyGetter(typeof(Strategy), nameof(Strategy.LongestDuration)),
                this, nameof(Strategy_LongestDuration)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.PropertyGetter(typeof(Strategy), nameof(Strategy.LeastDuration)),
                this, nameof(Strategy_LeastDuration)));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(Strategy), nameof(Strategy.CanBeDeactivated)),
                this));
        }

        static bool Strategy_LongestDuration(Strategy __instance, ref double __result)
        {
            __result = __instance.FactorLerp(__instance.MinLongestDuration, __instance.MaxLongestDuration);
            return false;
        }

        static bool Strategy_LeastDuration(Strategy __instance, ref double __result)
        {
            __result = __instance.FactorLerp(__instance.MinLeastDuration, __instance.MaxLeastDuration);
            return false;
        }

        internal static IEnumerable<CodeInstruction> Strategy_CanBeActivated_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            for (int i = 9; i < code.Count; ++i)
            {
                // We need to fix the inequality check for if ( dateActivated + LeastDuration < Planetarium.fetch.time )
                // because that should be a > check.
                if (code[i].opcode == OpCodes.Bge_Un_S)
                {
                    code[i].opcode = OpCodes.Ble_Un_S;
                    break;
                }
            }

            return code;
        }
    }
}
