using CommNet.Network;
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

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Transpiler, typeof(ModuleDeployablePart), nameof(ModuleDeployablePart.startFSM));
            AddPatch(PatchType.Transpiler, typeof(ModuleDeployableSolarPanel), nameof(ModuleDeployablePart.OnStart));
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

            // We remove that entire if statement by making the if (Brtrue_S) unconditional

            FieldInfo ModuleDeployablePart_deployState = AccessTools.Field(typeof(ModuleDeployablePart), nameof(ModuleDeployablePart.deployState));

            var matcher = new CodeMatcher(instructions);
            matcher
                .MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, ModuleDeployablePart_deployState),
                    new CodeMatch(OpCodes.Brtrue_S)
                );

            if (!matcher.IsValid)
                return matcher.Instructions();

            matcher
                .RemoveInstructions(2)
                .Insert(new CodeInstruction(OpCodes.Ldc_I4_1));

            return matcher.Instructions();
        }

        static bool ModuleDeployablePart_startFSM_Prefix(ModuleDeployableSolarPanel __instance)
        {
            if (__instance.useAnimation)
            {
                __instance.anim[__instance.animationName].wrapMode = WrapMode.ClampForever;
                switch (__instance.deployState)
                {
                    case ModuleDeployablePart.DeployState.RETRACTED:
                        __instance.anim[__instance.animationName].normalizedTime = 0f;
                        __instance.anim[__instance.animationName].enabled = true;
                        __instance.anim[__instance.animationName].weight = 1f;
                        __instance.anim[__instance.animationName].speed = 0f;
                        __instance.bypassSetupAnimation = true;
                        __instance.Events["Retract"].active = false;
                        __instance.Events["Extend"].active = true;
                        break;
                    case ModuleDeployablePart.DeployState.EXTENDED:
                        __instance.anim[__instance.animationName].normalizedTime = 1f;
                        __instance.anim[__instance.animationName].enabled = true;
                        __instance.anim[__instance.animationName].speed = 0f;
                        __instance.anim[__instance.animationName].weight = 1f;
                        __instance.Events["Extend"].active = false;
                        __instance.Events["Retract"].active = __instance.retractable || HighLogic.LoadedSceneIsEditor;
                        if (__instance.hasPivot)
                        {
                            __instance.panelRotationTransform.localRotation = __instance.currentRotation;
                        }
                        break;
                    case ModuleDeployablePart.DeployState.RETRACTING:
                        __instance.Events["Retract"].active = false;
                        __instance.Events["Extend"].active = false;
                        break;
                    case ModuleDeployablePart.DeployState.EXTENDING:
                        __instance.Events["Retract"].active = false;
                        __instance.Events["Extend"].active = false;
                        break;
                }
                if (__instance.deployState == ModuleDeployablePart.DeployState.RETRACTING || __instance.deployState == ModuleDeployablePart.DeployState.EXTENDING || __instance.deployState == ModuleDeployablePart.DeployState.BROKEN)
                {
                    __instance.anim[__instance.animationName].normalizedTime = __instance.storedAnimationTime;
                    __instance.anim[__instance.animationName].speed = __instance.storedAnimationSpeed;
                }
                if (!__instance.bypassSetupAnimation)
                {
                    __instance.anim.Play(__instance.animationName);
                }
                if (!__instance.playAnimationOnStart && __instance.deployState != ModuleDeployablePart.DeployState.EXTENDING && __instance.deployState != ModuleDeployablePart.DeployState.RETRACTING)
                {
                    __instance.stopAnimation = true;
                }
            }
            else
            {
                if (__instance.hasPivot)
                {
                    __instance.panelRotationTransform.localRotation = __instance.originalRotation;
                }
                if (__instance.deployState != ModuleDeployablePart.DeployState.BROKEN)
                {
                    __instance.deployState = ModuleDeployablePart.DeployState.EXTENDED;
                }
                __instance.Events["Retract"].active = false;
                __instance.Events["Extend"].active = false;
                __instance.Actions["ExtendPanelsAction"].active = false;
                __instance.Actions["ExtendAction"].active = false;
                __instance.Actions["RetractAction"].active = false;
                __instance.Fields["status"].guiActiveEditor = false;
            }
            if (__instance.deployState == ModuleDeployablePart.DeployState.BROKEN)
            {
                __instance.Events["Retract"].active = false;
                __instance.Events["Extend"].active = false;
                if (__instance.panelBreakTransform)
                {
                    __instance.panelBreakTransform.gameObject.SetActive(false);
                }
            }
            return false;
        }
    }
}
