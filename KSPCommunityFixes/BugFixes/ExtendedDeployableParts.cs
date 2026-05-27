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
        //
        // The FindAnimations() postfix additionally fixes issue #380 : a NullReferenceException thrown when a static
        // (non-animated) solar panel is placed on a part model that contains other, unrelated animations. See the
        // postfix for details.

        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Transpiler, typeof(ModuleDeployablePart), nameof(ModuleDeployablePart.startFSM));

            AddPatch(PatchType.Transpiler, typeof(ModuleDeployableSolarPanel), nameof(ModuleDeployablePart.OnStart));

            AddPatch(PatchType.Postfix, typeof(ModuleDeployablePart), nameof(ModuleDeployablePart.FindAnimations));
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
            // stock already does (nothing to change here) :
            // if (HighLogic.LoadedSceneIsFlight && anim != null)
            // {
            //     float normalizedTime = ((deployState == DeployState.EXTENDED) ? 1f : 0f);
            //     anim[animationName].normalizedTime = normalizedTime;
            //     anim[animationName].enabled = true;
            //     anim[animationName].weight = 1f;

            // but after that it does :

            //     if (deployState == DeployState.RETRACTED)
            //     {
            //         anim.Stop(animationName);
            //     }
            // }

            // We remove that trailing if statement by turning the leading `ldarg.0` of the `deployState == RETRACTED` test
            // into an unconditional jump to the same place the `brtrue.s` was jumping to (the end of the block), making the
            // anim.Stop() call unreachable.

            FieldInfo ModuleDeployablePart_deployState = AccessTools.Field(typeof(ModuleDeployablePart), nameof(ModuleDeployablePart.deployState));

            CodeMatcher matcher = new(instructions);

            matcher
                .MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, ModuleDeployablePart_deployState),
                    new CodeMatch(OpCodes.Brtrue_S))
                .ThrowIfInvalid("[ExtendedDeployableParts] could not find the `deployState == RETRACTED` test")
                .Set(OpCodes.Br_S, matcher.InstructionAt(2).operand); // reuse the brtrue.s target

            return matcher.Instructions();
        }

        static void ModuleDeployablePart_FindAnimations_Postfix(ModuleDeployablePart __instance, ref Animation ___anim)
        {
            // Issue #380 : FindAnimations() falls back to assigning `anim` to the first Animation component on the part
            // when none of them actually contains a clip named `animationName` (e.g. a static solar panel placed on a part
            // that has other, unrelated animations). That `anim` is never usable - whenever this fallback fires,
            // anim[animationName] is null so useAnimation is always forced false - yet it is left non-null. Consumers that
            // only guard on `anim != null` (such as ModuleDeployableSolarPanel.OnStart) then access the missing clip and
            // throw a NullReferenceException. We null it out in exactly that case so those `anim != null` guards work, while
            // leaving a legitimately-assigned `anim` (one that does have the clip) untouched.
            if (___anim != null && ___anim[__instance.animationName] == null)
                ___anim = null;
        }
    }
}
