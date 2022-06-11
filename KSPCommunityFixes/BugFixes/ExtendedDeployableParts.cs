using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    class ExtendedDeployableParts : BasePatch
    {
        // Not entirely clear what is happening, but for a retracted ModuleDeployablePart, as of 1.12 stock fails to set the animation
        // after instantiating the part, resulting in having the model in the extended state if the part model was exported in the extended
        // state. It seems something has changed between Unity 2019.2.2f1 (KSP 1.11) and 2019.4.18f1 (KSP 1.12), likely the Animation.Stop()
        // method not force-rewinding the animation anymore if it isn't actually playing. Setting the animation state with Animation.Stop()
        // is (suspiciously) only done in two places, in many more cases the stock code is instead keeping AnimationState.enabled = true and
        // setting AnimationState.speed = 0.
        // We patch those two Animation.Stop() call sites, and use that latter method instead.

        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ModuleDeployablePart), nameof(ModuleDeployablePart.startFSM)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ModuleDeployableSolarPanel), nameof(ModuleDeployablePart.OnStart)),
                this));
        }

        static IEnumerable<CodeInstruction> ModuleDeployablePart_startFSM_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // change :

            // anim.Stop(animationName);

            //IL_0091: ldarg.0
            //IL_0092: ldfld class [UnityEngine.AnimationModule] UnityEngine.Animation ModuleDeployablePart::anim
            //IL_0097: ldarg.0
            //IL_0098: ldfld string ModuleDeployablePart::animationName
            //IL_009d: callvirt instance void[UnityEngine.AnimationModule] UnityEngine.Animation::Stop(string)

            // to :

            // anim[animationName].speed = 0f;

            //IL_0091: ldarg.0
            //IL_0092: ldfld class [UnityEngine.AnimationModule]
            //IL_0097: ldarg.0
            //IL_0098: ldfld string ModuleDeployablePart::animationName
            //IL_0082: callvirt instance class [UnityEngine.AnimationModule] UnityEngine.AnimationState[UnityEngine.AnimationModule] UnityEngine.Animation::get_Item(string)
            //IL_0087: ldc.r4 0
            //IL_008c: callvirt instance void[UnityEngine.AnimationModule] UnityEngine.AnimationState::set_Speed(float32)

            MethodInfo Animation_Stop = AccessTools.Method(typeof(Animation), nameof(Animation.Stop), new Type[]{typeof(string)});
            MethodInfo Animation_get_Item = AccessTools.Method(typeof(Animation), "get_Item");
            MethodInfo AnimationState_set_Speed = AccessTools.PropertySetter(typeof(AnimationState), nameof(AnimationState.speed));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Callvirt && ReferenceEquals(code[i].operand, Animation_Stop))
                {
                    code.Insert(i, new CodeInstruction(OpCodes.Callvirt, Animation_get_Item));
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Ldc_R4, 0f));
                    code[i + 2].operand = AnimationState_set_Speed;
                    break;
                }
            }

            return code;
        }

        static IEnumerable<CodeInstruction> ModuleDeployableSolarPanel_OnStart_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // here no need to add anything, stock already does
            //float normalizedTime = ((deployState == DeployState.EXTENDED) ? 1f : 0f);
            //anim[animationName].normalizedTime = normalizedTime;
            //anim[animationName].enabled = true;
            //anim[animationName].weight = 1f;

            // but after that it does :

            //if (deployState == DeployState.RETRACTED)
            //{
            //    anim.Stop(animationName);
            //}

            // We remove that entire if statement by replacing the if (Brtrue_S) by an unconditional jump (Br_S)

            FieldInfo ModuleDeployablePart_deployState = AccessTools.Field(typeof(ModuleDeployablePart), nameof(ModuleDeployablePart.deployState));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ldarg_0
                    && code[i + 1].opcode == OpCodes.Ldfld && ReferenceEquals(code[i+1].operand, ModuleDeployablePart_deployState)
                    && code[i + 2].opcode == OpCodes.Brtrue_S)
                {
                    code[i].opcode = OpCodes.Br_S;
                    code[i].operand = code[i + 2].operand; // grab the target instruction from the original jump
                    break;
                }
            }

            return code;
        }
    }
}
