using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    // https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/179
    // ModulePartVariants can move attachnodes - most notably for the structural tube parts in MakingHistory.
    // When a vessel is started from a craft file, the actual positions of the attachnodes are stored in the
    // craft file and get created correctly.  However when resuming a flight in progress, the attachnode
    // positions are NOT stored in the persistence file and will use whatever location was set in the prefab.
    // This affects where the part joints are created, and can have a significant impact on part flexibility.

    // This patch alters ModulePartVariants.OnStart so that it processes attachnode positions in all scenes
    // (the stock code only does this in EDITOR and LOADING scenes).  However UpdateNode will then call 
    // UpdatePartPosition which is something that should only be done in the editor.

    internal class ModulePartVariantsNodePersistence : BasePatch
    {
        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            Type[] ApplyVariant_parameterTypes = new Type[]
            {
                typeof(Part),
                typeof(Transform),
                typeof(PartVariant),
                typeof(Material[]),
                typeof(bool),
                typeof(int)
            };

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ModulePartVariants), nameof(ModulePartVariants.ApplyVariant), ApplyVariant_parameterTypes)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModulePartVariants), nameof(ModulePartVariants.UpdatePartPosition))));
        }

        static IEnumerable<CodeInstruction> ModulePartVariants_ApplyVariant_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            CodeInstruction[] instructionsArr = instructions.ToArray();

            FieldInfo HighLogic_LoadedScene_FieldInfo = AccessTools.Field(typeof(HighLogic), nameof(HighLogic.LoadedScene));

            for (int i = 0; i < instructionsArr.Length; i++)
            {
                // find the if statement that checks for EDITOR scene, and make it always compare equal
                if (instructionsArr[i+0].LoadsField(HighLogic_LoadedScene_FieldInfo) &&
                    instructionsArr[i+1].LoadsConstant(GameScenes.EDITOR) &&
                    instructionsArr[i+2].opcode == OpCodes.Beq_S)
                {
                    instructionsArr[i+1] = new CodeInstruction(OpCodes.Ldsfld, HighLogic_LoadedScene_FieldInfo);
                    return instructionsArr;
                }
            }

            throw new Exception("Failed to find code patch location");
        }

        // Prevent UpdatePartPosition from running outside of the editor scene
        static bool ModulePartVariants_UpdatePartPosition_Prefix()
        {
            return HighLogic.LoadedSceneIsEditor;
        }
    }
}
