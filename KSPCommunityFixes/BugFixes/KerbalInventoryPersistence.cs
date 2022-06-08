using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace KSPCommunityFixes
{
    // bugfix : https://bugs.kerbalspaceprogram.com/issues/28569
    // 
    // KerbalInventoryScenario is supposed to handle the ModuleInventoryPart instances created for
    // in-vessel crew members through the ProtoCrewMember.kerbalModule getter.
    // However, in 1.12.2, the getter will bail out from adding those inventory instances
    // to the KerbalInventoryScenario instance, unless the current game mode is SCENARIO or SCENARIO_NON_RESUMABLE.
    // I guess the conditions where inverted by mistake, although I fail to understand why there should be any condition of that type.
    // 
    // This cause ModuleInventoryPart.Update() to not run for kerbal inventories, which lead to the inventory
    // not being updated when hovering them with a held part. The volume/mass limit sliders don't update,
    // and the player can effectively entirely bypass the limits.
    // 
    // This also cause the whole kerbal inventory persistence system to fail in various ways,
    // this is responsible for those two issues :
    // https://bugs.kerbalspaceprogram.com/issues/28559
    // https://bugs.kerbalspaceprogram.com/issues/28561

    public class KerbalInventoryPersistence : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 2);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.PropertyGetter(typeof(ProtoCrewMember), "kerbalModule"),
                this, nameof(ProtoCrewMember_kerbalModule_Transpiler)));
        }

        private static IEnumerable<CodeInstruction> ProtoCrewMember_kerbalModule_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo HighLogic_CurrentGame = AccessTools.PropertyGetter(typeof(HighLogic), nameof(HighLogic.CurrentGame));
            FieldInfo Game_Mode = AccessTools.Field(typeof(Game), nameof(Game.Mode));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            //// (no C# code)
            //IL_0075: call class Game HighLogic::get_CurrentGame()
            //IL_007a: ldfld valuetype Game/Modes Game::Mode
            //IL_007f: ldc.i4.2
            //IL_0080: beq.s IL_008f

            //// KerbalInventoryScenario.Instance.AddKerbalInventoryInstance(name, instanceKerbalModule);
            //IL_0082: call class Game HighLogic::get_CurrentGame()
            //IL_0087: ldfld valuetype Game/Modes Game::Mode
            //IL_008c: ldc.i4.3
            //IL_008d: bne.un.s IL_00a5

            for (int i = 0; i < code.Count - 5; i++)
            {
                if (code[i].opcode == OpCodes.Call
                    && ReferenceEquals(code[i].operand, HighLogic_CurrentGame)
                    && code[i + 1].opcode == OpCodes.Dup // thanks obfuscation
                    && code[i + 2].opcode == OpCodes.Pop // thanks obfuscation
                    && code[i + 3].opcode == OpCodes.Ldfld
                    && ReferenceEquals(code[i + 3].operand, Game_Mode)
                    && code[i + 4].opcode == OpCodes.Ldc_I4_2
                    && code[i + 5].opcode == OpCodes.Beq_S)
                {
                    code[i].opcode = OpCodes.Nop;
                    code[i].operand = null;
                    code[i + 1].opcode = OpCodes.Nop;
                    code[i + 2].opcode = OpCodes.Nop;
                    code[i + 3].opcode = OpCodes.Nop;
                    code[i + 3].operand = null;
                    code[i + 4].opcode = OpCodes.Nop;
                    code[i + 5].opcode = OpCodes.Nop;
                    code[i + 5].operand = null;
                }

                if (code[i].opcode == OpCodes.Call
                    && ReferenceEquals(code[i].operand, HighLogic_CurrentGame)
                    && code[i + 1].opcode == OpCodes.Dup // thanks obfuscation
                    && code[i + 2].opcode == OpCodes.Pop // thanks obfuscation
                    && code[i + 3].opcode == OpCodes.Ldfld
                    && ReferenceEquals(code[i + 3].operand, Game_Mode)
                    && code[i + 4].opcode == OpCodes.Ldc_I4_3
                    && code[i + 5].opcode == OpCodes.Bne_Un_S)
                {
                    code[i].opcode = OpCodes.Nop;
                    code[i].operand = null;
                    code[i + 1].opcode = OpCodes.Nop;
                    code[i + 2].opcode = OpCodes.Nop;
                    code[i + 3].opcode = OpCodes.Nop;
                    code[i + 3].operand = null;
                    code[i + 4].opcode = OpCodes.Nop;
                    code[i + 5].opcode = OpCodes.Nop;
                    code[i + 5].operand = null;
                }
            }
            return code;
        }
    }
}
