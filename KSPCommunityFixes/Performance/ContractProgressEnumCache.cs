// fix for https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/100

using FinePrint.Utilities;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

#if PROFILE_GETANYBODYPROGRESS
using System.Diagnostics;
using UnityEngine;
#endif

namespace KSPCommunityFixes.Performance
{
    class ContractProgressEnumCache : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ProgressUtilities), nameof(ProgressUtilities.GetAnyBodyProgress)),
                this));

#if PROFILE_GETANYBODYPROGRESS
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ProgressUtilities), nameof(ProgressUtilities.GetAnyBodyProgress)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ProgressUtilities), nameof(ProgressUtilities.GetAnyBodyProgress)),
                this));
#endif
        }

        /*
	    // ProgressType[] array = (ProgressType[])Enum.GetValues(typeof(ProgressType));
	    IL_001d: ldtoken FinePrint.Utilities.ProgressType
	    IL_0022: call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
	    IL_0027: dup
	    IL_0028: pop
	    IL_0029: call class [mscorlib]System.Array [mscorlib]System.Enum::GetValues(class [mscorlib]System.Type)
	    IL_002e: dup
	    IL_002f: pop
	    IL_0030: castclass valuetype FinePrint.Utilities.ProgressType[]
	    IL_0035: stloc.0
        */

        static readonly ProgressType[] progressTypes = (ProgressType[])Enum.GetValues(typeof(ProgressType));

        static IEnumerable<CodeInstruction> ProgressUtilities_GetAnyBodyProgress_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            Type enumType = typeof(ProgressType);
            Type enumArrayType = typeof(ProgressType[]);
            FieldInfo progressTypes_Info = AccessTools.Field(typeof(ContractProgressEnumCache), nameof(progressTypes));

            bool nop = false;
            foreach (CodeInstruction op in instructions)
            {
                if (op.opcode == OpCodes.Ldtoken && ReferenceEquals(op.operand, enumType))
                    nop = true;

                if (nop)
                {
                    if (op.opcode == OpCodes.Castclass && ReferenceEquals(op.operand, enumArrayType))
                    {
                        op.opcode = OpCodes.Ldsfld;
                        op.operand = progressTypes_Info;
                        nop = false;
                    }
                    else
                    {
                        op.opcode = OpCodes.Nop;
                        op.operand = null;
                    }
                }

                yield return op;
            }
        }

#if PROFILE_GETANYBODYPROGRESS
        static Stopwatch stopwatch = new Stopwatch();
        static int callCount;
        static int? firstFrame = null;

        static Unity.Profiling.ProfilerMarker profiler = new Unity.Profiling.ProfilerMarker("ProgressUtilities.GetAnyBodyProgress");

        static void ProgressUtilities_GetAnyBodyProgress_Prefix()
        {
            profiler.Begin();
            stopwatch.Start();
        }

        static void ProgressUtilities_GetAnyBodyProgress_Postfix(CelestialBody body, bool __result)
        {
            // 0.036 ms stock
            // 0.001 ms with transpiler
            stopwatch.Stop();
            profiler.End();
            callCount++;

            if (firstFrame == null)
                firstFrame = Time.frameCount - 1;

            double elapsed = stopwatch.Elapsed.TotalMilliseconds;
            double elapsedFrames = Time.frameCount - ((int)firstFrame);

            UnityEngine.Debug.Log($"ProgressUtilities.GetAnyBodyProgress({body.displayName}) -> {__result} // avg/call={elapsed / callCount:F4}ms, avg/frame={elapsed/elapsedFrames:F4}ms");
        }
#endif
    }
}
