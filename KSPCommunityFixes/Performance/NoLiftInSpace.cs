using System;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    class NoLiftInSpace : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleLiftingSurface), nameof(ModuleLiftingSurface.FixedUpdate)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleControlSurface), nameof(ModuleControlSurface.FixedUpdate)),
                this));
        }

        static bool ModuleLiftingSurface_FixedUpdate_Prefix(ModuleLiftingSurface __instance)
        {
            if (HighLogic.LoadedSceneIsFlight && __instance.part.dynamicPressurekPa == 0.0 && __instance.part.submergedDynamicPressurekPa == 0.0)
            {
                if (__instance.pointVelocity != Vector3.zero)
                {
                    __instance.pointVelocity = Vector3.zero;
                    __instance.nVel = Vector3.zero;
                    __instance.liftVector = Vector3.zero;
                    __instance.liftForce = Vector3.zero;
                    __instance.dragForce = Vector3.zero;
                    __instance.Qdrag = 0.0;
                    __instance.Qlift = 0.0;
                    __instance.liftDot = 0f;
                    __instance.liftField.guiActive = false;
                    __instance.dragField.guiActive = false;
                }
                return false;
            }

            return true;
        }

        static bool ModuleControlSurface_FixedUpdate_Prefix(ModuleControlSurface __instance)
        {
            if (HighLogic.LoadedSceneIsFlight && __instance.part.dynamicPressurekPa == 0.0 && __instance.part.submergedDynamicPressurekPa == 0.0)
            {
                if (__instance.pointVelocity != Vector3.zero)
                {
                    __instance.pointVelocity = Vector3.zero;
                    __instance.nVel = Vector3.zero;
                    __instance.liftVector = Vector3.zero;
                    __instance.liftForce = Vector3.zero;
                    __instance.dragForce = Vector3.zero;
                    __instance.Qdrag = 0.0;
                    __instance.Qlift = 0.0;
                    __instance.liftDot = 0f;
                    __instance.liftField.guiActive = false;
                    __instance.dragField.guiActive = false;
                    __instance.baseLiftForce = Vector3.zero;
                }

                if (__instance.deploy && __instance.currentDeployAngle != __instance.deployAngle)
                {
                    float sign = __instance.usesMirrorDeploy 
                        ? ((__instance.deployInvert ? (-1f) : 1f) * (__instance.partDeployInvert ? (-1f) : 1f) * (__instance.mirrorDeploy ? (-1f) : 1f)) 
                        : ((__instance.deployInvert ? (-1f) : 1f) * Mathf.Sign((Quaternion.Inverse(__instance.vessel.ReferenceTransform.rotation) * (__instance.baseTransform.position - __instance.vessel.CurrentCoM)).x));
                    __instance.currentDeployAngle = -1f * sign * __instance.deployAngle;
                }

                if (__instance.ctrlSurface != null && __instance.deploy ? __instance.deflection != __instance.currentDeployAngle : __instance.deflection != 0f)
                {
                    float targetAngle = __instance.deploy ? __instance.currentDeployAngle : 0f;
                    __instance.deflection = Mathf.MoveTowards(__instance.deflection, targetAngle, __instance.actuatorSpeed * TimeWarp.fixedDeltaTime);
                    __instance.ctrlSurface.localRotation = Quaternion.AngleAxis(__instance.deflection, Vector3.right) * __instance.neutral;

                    if (__instance.deflection == targetAngle)
                    {
                        __instance.inputVector = Vector3.zero;
                        __instance.action = 0f;
                        __instance.roll = 0f;

                        if (__instance.displaceVelocity)
                        {
                            __instance.PitchCtrlState = "n/a";
                            __instance.RollCtrlState = "n/a";
                            __instance.YawCtrlState = "n/a";
                            __instance.potentialBladeControlTorque = Vector3.zero;
                            __instance.rotatingControlInput = Vector3.zero;
                        }
                    }
                }

                return false;
            }

            return true;
        }
    }
}
