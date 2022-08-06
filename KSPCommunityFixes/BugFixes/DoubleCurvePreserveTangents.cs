using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Reflection.Emit;

namespace KSPCommunityFixes.BugFixes
{
    class DoubleCurvePreserveTangents : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(DoubleCurve), nameof(DoubleCurve.RecomputeTangents)),
                this));
        }

        static IEnumerable<CodeInstruction> DoubleCurve_RecomputeTangents_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // The existing function has a test if ( count == 1 ) and, if true, it
            // will flatten the tangents of the key regardless of if it is
            // set to autotangent or not. Since the tangents of a single-key
            // curve don't matter, let's just make the test always false,
            // by making it if ( count == -1 ).
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            for (int i = 1; i < code.Count; ++i)
            {
                if (code[i].opcode == OpCodes.Ldc_I4_1 && code[i - 1].opcode == OpCodes.Ldloc_1)
                {
                    code[i + 1] = new CodeInstruction(OpCodes.Ret);
                    break;
                }
            }

            return code;
        }
    }
}
