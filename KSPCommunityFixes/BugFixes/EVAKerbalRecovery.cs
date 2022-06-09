// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/43

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace KSPCommunityFixes.BugFixes
{
    public class EVAKerbalRecovery : BasePatch
    {
        protected override Version VersionMin => new Version(1, 11, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ProtoVessel), nameof(ProtoVessel.GetAllProtoPartsIncludingCargo)),
                this));
        }

        static IEnumerable<CodeInstruction> ProtoVessel_GetAllProtoPartsIncludingCargo_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGen)
        {
            MethodInfo mInfo_IsEVAKerbal = AccessTools.Method(typeof(EVAKerbalRecovery), nameof(EVAKerbalRecovery.IsEVAKerbal));
            MethodInfo mInfo_GetAllProtoPartsFromCrew = AccessTools.Method(typeof(ProtoVessel), nameof(ProtoVessel.GetAllProtoPartsFromCrew));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                // find "if (partInfoByName == null || string.Equals(partInfoByName.name, "kerbalEVA"))"
                // and nop the whole "string.Equals(partInfoByName.name, "kerbalEVA")" condition
                // Note that this condition was added in KSP 1.12.0 (probably to attempt to fix cargo parts being recovered twice)
                // so it won't be found in KSP 1.11.x. Since this "fix" doesn't work and create even more problems, we remove it.
                if (code[i].opcode == OpCodes.Ldstr && code[i].operand is string str && str == "kerbalEVA")
                {
                    int j = i + 1;
                    // advance to the jump
                    while (code[j].opcode != OpCodes.Brtrue)
                        j++;

                    // rewind and nop all instructions until the "partInfoByName == null" jump is reached
                    while (code[j].opcode != OpCodes.Brfalse)
                    {
                        code[j].opcode = OpCodes.Nop;
                        code[j].operand = null;
                        j--;
                    }
                }

                // find the "list.AddRange(GetAllProtoPartsFromCrew());" line at the end of the method and don't execute it for EVA kerbals
                // this is the proper fix to the EVA kerbal inventories being recovered twice.
                if (code[i].opcode == OpCodes.Call && ReferenceEquals(code[i].operand, mInfo_GetAllProtoPartsFromCrew))
                {
                    // search the begining of line
                    int insertPos = i;
                    while (code[insertPos].opcode != OpCodes.Ldloc_0)
                        insertPos--;

                    // search the final "return list;" line
                    int jumpPos = i;
                    while (code[jumpPos].opcode != OpCodes.Ret)
                        jumpPos++;

                    while (code[jumpPos].opcode != OpCodes.Ldloc_0)
                        jumpPos--;

                    // and add a label so we can jump to it
                    Label jump = ilGen.DefineLabel();
                    code[jumpPos].labels.Add(jump);

                    // then add a jump to bypass the "list.AddRange(GetAllProtoPartsFromCrew());" line if our
                    // IsEVAKerbal() method returns true
                    CodeInstruction[] insert = new CodeInstruction[3];
                    insert[0] = new CodeInstruction(OpCodes.Ldarg_0);
                    insert[1] = new CodeInstruction(OpCodes.Call, mInfo_IsEVAKerbal);
                    insert[2] = new CodeInstruction(OpCodes.Brtrue, jump);

                    code.InsertRange(insertPos, insert);
                    break;
                }
            }

            return code;
        }

        private static bool IsEVAKerbal(ProtoVessel pv)
        {
            // EVA kerbals are always 1 part vessels
            if (pv.protoPartSnapshots.Count != 1)
                return false;

            return pv.protoPartSnapshots[0].partInfo.name.StartsWith("kerbalEVA");
        }
    }


}
