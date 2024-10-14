using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace KSPCommunityFixes.BugFixes
{
    // RDTech.Load will force the tech's state to State.Available (i.e. researched) if the science cost is 0
    // this doesn't work properly in game modes where you need to purchase parts after researching the tech
    // because it doesn't set up the correct data structures.
    // To fix this, we remove the modification to the state field in the Load function and then research the
    // tech after Start has run to set up all the data properly.

    internal class ZeroCostTechNodes : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(RDTech), nameof(RDTech.Start));

            AddPatch(PatchType.Transpiler, typeof(RDTech), nameof(RDTech.Load));
        }

        static void RDTech_Start_Postfix(RDTech __instance)
        {
            if (__instance.scienceCost == 0 && __instance.state != RDTech.State.Available)
            {
                __instance.UnlockTech(true);
            }
        }

        static IEnumerable<CodeInstruction> RDTech_Load_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var stateField = AccessTools.Field(typeof(RDTech), nameof(RDTech.state));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.StoresField(stateField))
                {
                    // need to pop 2 values off the stack - the value to store and the field where to store it
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Pop);
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }
}
