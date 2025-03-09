using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace KSPCommunityFixes.QoL
{
    class TargetParentBody : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(OrbitTargeter), nameof(OrbitTargeter.DropInvalidTargets));

            AddPatch(PatchType.Transpiler, typeof(FlightGlobals), nameof(FlightGlobals.UpdateInformation));
        }

        // OrbitTargeter.DropInvalidTargets is only ever used to clear the target if the target is in the Parent body heriarchy
        // Always return false and do not run the base method
        private static bool OrbitTargeter_DropInvalidTargets_Prefix(bool __result)
        {
            __result = false;
            return false;
        }

        // FlightGlobals.UpdateInformation contains two sequential if statements that each make a call to FlightGlobals.SetVesselTarget
        // to clear the target if the target is the direct parent of the vessel or in the parent body heriarchy
        // Starting from the end of the if statement making the second call to FlightGlobals.SetVesselTarget, iterate backwards and
        // replace opcodes with Nop until reaching the beginning of the first if statement
        static IEnumerable<CodeInstruction> FlightGlobals_UpdateInformation_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo FlightGlobals_currentMainBody = AccessTools.Field(typeof(FlightGlobals), nameof(FlightGlobals.currentMainBody));
            MethodInfo FlightGlobals_SetVesselTarget = AccessTools.Method(typeof(FlightGlobals), nameof(FlightGlobals.SetVesselTarget));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int numFlightGlobals_currentMainBodysSeen = 0;
            int numFlightGlobals_SetVesselTargetsSeen = 0;
            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Br
                    && code[i - 1].opcode == OpCodes.Call
                    && ReferenceEquals(code[i - 1].operand, FlightGlobals_SetVesselTarget))
                {
                    numFlightGlobals_SetVesselTargetsSeen++;
                    if (numFlightGlobals_SetVesselTargetsSeen == 2)
                    {
                        for (int j = i; j >= 0; j--)
                        {
                            if (code[j].opcode == OpCodes.Ldsfld && ReferenceEquals(code[j].operand, FlightGlobals_currentMainBody))
                            {
                                numFlightGlobals_currentMainBodysSeen++;
                            }
                            OpCode opcode = code[j].opcode;
                            code[j].opcode = OpCodes.Nop;
                            code[j].operand = null;
                            if (numFlightGlobals_currentMainBodysSeen == 2 && opcode == OpCodes.Ldarg_0)
                            {
                                break;
                            }
                        }
                        break;
                    }
                }
            }
            return code;
        }
    }
}
