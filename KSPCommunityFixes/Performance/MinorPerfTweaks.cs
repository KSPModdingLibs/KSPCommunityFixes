using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace KSPCommunityFixes.Performance
{
    internal class MinorPerfTweaks : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(Part), nameof(Part.isKerbalEVA)),
                this));
        }

        private static IEnumerable<CodeInstruction> Part_isKerbalEVA_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo isKerbalEVAFast = AccessTools.Method(typeof(KSPObjectsExtensions), nameof(KSPObjectsExtensions.IsKerbalEVA));

            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, isKerbalEVAFast);
            yield return new CodeInstruction(OpCodes.Ret);
        }
    }
}
