using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Reflection.Emit;

namespace KSPCommunityFixes.BugFixes
{
    class DoubleCurvePreserveTangents : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            if (!KSPCommunityFixes.cleanedDll)
            {
                AddPatch(PatchType.Transpiler, typeof(DoubleCurve), nameof(DoubleCurve.RecomputeTangents));
            }
            else
            {
                AddPatch(PatchType.Prefix, typeof(DoubleCurve), nameof(DoubleCurve.RecomputeTangents));
            }
        }

        static IEnumerable<CodeInstruction> DoubleCurve_RecomputeTangents_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // The existing function has a test if ( count == 1 ) and, if true, it
            // will flatten the tangents of the key regardless of if it is
            // set to autotangent or not. Since the tangents of a single-key
            // curve don't matter, let's just return.
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            for (int i = 1; i < code.Count; ++i)
            {
                if (code[i].opcode == OpCodes.Ldc_I4_1 && code[i - 1].opcode != OpCodes.Ldloc_1)
                {
                    code[i] = new CodeInstruction(OpCodes.Ret);
                    code[i + 1] = new CodeInstruction(OpCodes.Nop);
                    code[i + 2] = new CodeInstruction(OpCodes.Nop);
                    code[i + 3] = new CodeInstruction(OpCodes.Nop);
                    code[i + 4] = new CodeInstruction(OpCodes.Nop);
                    code[i + 5] = new CodeInstruction(OpCodes.Nop);
                    break;
                }
            }

            return code;
        }

        static bool DoubleCurve_RecomputeTangents_Prefix(DoubleCurve __instance)
        {
            int count = __instance.keys.Count;
            DoubleKeyframe doubleKeyframe;
            if (count == 1)
            {
                return false;
            }
            doubleKeyframe = __instance.keys[0];
            if (doubleKeyframe.autoTangent)
            {
                doubleKeyframe.inTangent = 0.0;
                doubleKeyframe.outTangent = (__instance.keys[1].value - doubleKeyframe.value) / (__instance.keys[1].time - doubleKeyframe.time);
                __instance.keys[0] = doubleKeyframe;
            }
            int num3 = count - 1;
            doubleKeyframe = __instance.keys[num3];
            if (doubleKeyframe.autoTangent)
            {
                doubleKeyframe.inTangent = (doubleKeyframe.value - __instance.keys[num3 - 1].value) / (doubleKeyframe.time - __instance.keys[num3 - 1].value);
                doubleKeyframe.outTangent = 0.0;
                __instance.keys[num3] = doubleKeyframe;
            }
            if (count > 2)
            {
                for (int i = 1; i < num3; i++)
                {
                    doubleKeyframe = __instance.keys[i];
                    if (doubleKeyframe.autoTangent)
                    {
                        double num4 = (doubleKeyframe.value - __instance.keys[i - 1].value) / (doubleKeyframe.time - __instance.keys[i - 1].value);
                        double num5 = (__instance.keys[i + 1].value - doubleKeyframe.value) / (__instance.keys[i + 1].time - doubleKeyframe.time);
                        doubleKeyframe.inTangent = (doubleKeyframe.outTangent = (num4 + num5) * 0.5);
                    }
                }
            }
            return false;
        }
    }
}
