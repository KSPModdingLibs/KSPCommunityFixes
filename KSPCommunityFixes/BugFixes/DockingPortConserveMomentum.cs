using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace KSPCommunityFixes.BugFixes
{
    class DockingPortConserveMomentum : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            // We need to patch a closure, which doesn't have a stable method name.
            // So we patch everything that *might* be the closure we are looking for,
            // but in the transpiler body we inspect the code contents to make sure we found the right one
            // before making any modifications.

            Traverse dockingNodeTraverse = Traverse.Create<ModuleDockingNode>();
            foreach (string methodName in dockingNodeTraverse.Methods())
            {
                if (methodName.StartsWith("<SetupFSM>") && !methodName.Contains("_Patch"))
                {
                    AddPatch(PatchType.Transpiler, typeof(ModuleDockingNode), methodName, "ModuleDockingNode_SetupFSMClosure_Transpiler");
                }
            }
        }

        // Reimplementation of https://harmony.pardeike.net/api/HarmonyLib.CodeInstruction.html#HarmonyLib_CodeInstruction_StoreLocal_System_Int32_,
        // which seems not to be available for some reason?
        static CodeInstruction CodeInstructionStoreLocal(int index)
        {
            switch (index)
            {
                case 0:
                    return new CodeInstruction(OpCodes.Stloc_0);

                case 1:
                    return new CodeInstruction(OpCodes.Stloc_1);

                case 2:
                    return new CodeInstruction(OpCodes.Stloc_2);

                case 3:
                    return new CodeInstruction(OpCodes.Stloc_3);

                default:
                    if (index < 256)
                    {
                        return new CodeInstruction(OpCodes.Stloc_S, Convert.ToByte(index));
                    }
                    else
                    {
                        return new CodeInstruction(OpCodes.Stloc, index);
                    }

            }
        }

        static CodeInstruction CodeInstructionLoadLocal(int index)
        {
            switch (index)
            {
                case 0:
                    return new CodeInstruction(OpCodes.Ldloc_0);

                case 1:
                    return new CodeInstruction(OpCodes.Ldloc_1);

                case 2:
                    return new CodeInstruction(OpCodes.Ldloc_2);

                case 3:
                    return new CodeInstruction(OpCodes.Ldloc_3);

                default:
                    if (index < 256)
                    {
                        return new CodeInstruction(OpCodes.Ldloc_S, Convert.ToByte(index));
                    }
                    else
                    {
                        return new CodeInstruction(OpCodes.Ldloc, index);
                    }
            }
        }

        // Checks whether a sequence of instructions looks like the closure we want to patch, , by inspecting which fields it loads.
        static bool IsTargetClosure(List<CodeInstruction> instructions)
        {
            bool acquireForce = false;
            bool acquireTorque = false;
            bool acquireTorqueRoll = false;
            bool acquireForceTweak = false;
            bool otherNode = false;

            foreach (CodeInstruction instr in instructions)
            {
                if (instr.opcode == OpCodes.Ldfld)
                {
                    if (!acquireForce && Equals(instr.operand, typeof(ModuleDockingNode).GetField("acquireForce")))
                    {
                        acquireForce = true;
                    }
                    else if (!acquireTorque && Equals(instr.operand, typeof(ModuleDockingNode).GetField("acquireTorque")))
                    {
                        acquireTorque = true;
                    }
                    else if (!acquireTorqueRoll && Equals(instr.operand, typeof(ModuleDockingNode).GetField("acquireTorqueRoll")))
                    {
                        acquireTorqueRoll = true;
                    }
                    else if (!acquireForceTweak && Equals(instr.operand, typeof(ModuleDockingNode).GetField("acquireForceTweak")))
                    {
                        acquireForceTweak = true;
                    }
                    else if (!otherNode && Equals(instr.operand, typeof(ModuleDockingNode).GetField("otherNode")))
                    {
                        otherNode = true;
                    }
                }
            }

            return acquireForce & acquireTorque & acquireTorqueRoll & acquireForceTweak & otherNode;
        }

        static IEnumerable<CodeInstruction> ModuleDockingNode_SetupFSMClosure_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGen)
        {
            List<CodeInstruction> instrList = instructions.ToList();
            // Check if this closure is the one we want to patch.
            if (IsTargetClosure(instrList))
            {
                // This looks like the closure we want to patch, patch it.

                // First, calculate the averages of the force values between the two modules.

                LocalBuilder avgAcquireForce = ilGen.DeclareLocal(typeof(float));
                LocalBuilder avgAcquireTorque = ilGen.DeclareLocal(typeof(float));
                LocalBuilder avgAcquireTorqueRoll = ilGen.DeclareLocal(typeof(float));

                // calculate avgAcquireForce
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireForceTweak");
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireForce");
                yield return new CodeInstruction(OpCodes.Mul);

                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "otherNode");
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireForceTweak");
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "otherNode");
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireForce");
                yield return new CodeInstruction(OpCodes.Mul);

                yield return new CodeInstruction(OpCodes.Add);
                yield return CodeInstructionStoreLocal(avgAcquireForce.LocalIndex);

                // calculate avgAcquireTorque
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireForceTweak");
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireTorque");
                yield return new CodeInstruction(OpCodes.Mul);

                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "otherNode");
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireForceTweak");
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "otherNode");
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireTorque");
                yield return new CodeInstruction(OpCodes.Mul);

                yield return new CodeInstruction(OpCodes.Add);
                yield return CodeInstructionStoreLocal(avgAcquireTorque.LocalIndex);

                // calculate avgAcquireTorqueRoll
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireForceTweak");
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireTorqueRoll");
                yield return new CodeInstruction(OpCodes.Mul);

                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "otherNode");
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireForceTweak");
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "otherNode");
                yield return CodeInstruction.LoadField(typeof(ModuleDockingNode), "acquireTorqueRoll");
                yield return new CodeInstruction(OpCodes.Mul);

                yield return new CodeInstruction(OpCodes.Add);
                yield return CodeInstructionStoreLocal(avgAcquireTorqueRoll.LocalIndex);

                foreach (CodeInstruction instr in instrList)
                {
                    // Replace any uses of the individual module force values with the average between both modules.

                    if (instr.LoadsField(typeof(ModuleDockingNode).GetField("acquireForceTweak")))
                    {
                        yield return new CodeInstruction(OpCodes.Pop);
                        // Why 0.5 and not 1? We didn't divide by 2 when calculating the average earlier, so we do it now.
                        yield return new CodeInstruction(OpCodes.Ldc_R4, 0.5f);
                    }
                    else if (instr.LoadsField(typeof(ModuleDockingNode).GetField("acquireForce")))
                    {
                        yield return new CodeInstruction(OpCodes.Pop);
                        yield return CodeInstructionLoadLocal(avgAcquireForce.LocalIndex);
                    }
                    else if (instr.LoadsField(typeof(ModuleDockingNode).GetField("acquireTorque")))
                    {
                        yield return new CodeInstruction(OpCodes.Pop);
                        yield return CodeInstructionLoadLocal(avgAcquireTorque.LocalIndex);
                    }
                    else if (instr.LoadsField(typeof(ModuleDockingNode).GetField("acquireTorqueRoll")))
                    {
                        yield return new CodeInstruction(OpCodes.Pop);
                        yield return CodeInstructionLoadLocal(avgAcquireTorqueRoll.LocalIndex);
                    }
                    else
                    {
                        yield return instr;
                    }
                }
            }
            else
            {
                // This doesn't look like our patch target, pass it on unmodified.
                foreach (CodeInstruction instr in instrList)
                {
                    yield return instr;
                }
            }
        }
    }
}
