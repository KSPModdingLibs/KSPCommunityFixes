using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    class DockingPortRotationDriftAndFixes : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.OnLoad)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.OnSave)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.OnStart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.OnStartFinished)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.OnDestroy)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.PropertyGetter(typeof(ModuleDockingNode), nameof(ModuleDockingNode.RotationJoint)),
                this, nameof(ModuleDockingNode_RotationJoint_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.PropertyGetter(typeof(ModuleDockingNode), nameof(ModuleDockingNode.VisualTargetAngle)),
                this, nameof(ModuleDockingNode_VisualTargetAngle_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.OnPartUnpack)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.UpdatePAWUI)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.UpdateAlignmentRotation)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.ApplyCoordsUpdate)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.SetJointHighLowLimits)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.ModifyLocked)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.IsJointUnlocked)),
                this));

            GameEvents.onGameSceneLoadRequested.Add(OnSceneSwitch);
        }

        private static readonly Dictionary<Part, DockingNodeInfo> dockingNodeInfos = new Dictionary<Part, DockingNodeInfo>();

        private void OnSceneSwitch(GameScenes data)
        {
            dockingNodeInfos.Clear();
        }

        // Remove the -86°/86° hardcoded limitation of `hardMinMaxLimits`, it is now -180°/180°.
        // I suspect this arbitrary limitation was set to mitigate the ConfigurableJointMotion.Limited issue, 
        // see the ModuleDockingNode_SetJointHighLowLimits_Prefix for details.
        static IEnumerable<CodeInstruction> ModuleDockingNode_OnLoad_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            foreach (CodeInstruction instruction in code)
            {
                if (instruction.opcode == OpCodes.Ldc_R4)
                {
                    if ((float)instruction.operand == -86f)
                        instruction.operand = -180f;
                    else if ((float)instruction.operand == 86f)
                        instruction.operand = 180f;
                }
            }

            return code;
        }

        // If a vessel is saved while rotating, the reloaded vessel will have its part in the mid-rotated state but the moving part will
        // snap to the target rotation, resulting in a permanent offset. To prevent this, we update the persisted targetAngle to the current
        // angle if a rotation is ongoing.
        static void ModuleDockingNode_OnSave_Postfix(ModuleDockingNode __instance, ConfigNode node)
        {
            if (__instance.canRotate && __instance.rotationInitComplete && __instance.IsRotating)
            {
                ConfigurableJoint rotationJoint = GetRotationJoint(__instance);

                // this is the reverse of the VisualTargetAngle property
                float currentTargetAngle = __instance.visualTargetAngle;
                if (rotationJoint.IsNotNullOrDestroyed() && rotationJoint == __instance.part.attachJoint.DestroyedAsNull()?.Joint)
                {
                    if (!__instance.inverted)
                        currentTargetAngle *= -1f;
                }
                else
                {
                    if (__instance.inverted)
                        currentTargetAngle *= -1f;
                }

                node.SetValue(nameof(ModuleDockingNode.targetAngle), currentTargetAngle);
            }
        }

        // stock tries to set ConfigurableJointMotion.Limited with angle limits corresponding to the
        // configured hardMinMaxLimits field. However, it seems that those limits are relative to the
        // joint local space and the stock code doesn't do that conversion. The end result is that the
        // joint occasionally isn't allowed to go to its target (usally when the two docking ports are
        // requesting opposite extreme angles), causing :
        // - desynchronizations between the expected position and actual position, which in the stock
        //   "drifting" implementation result in a persistent offset
        // - Phantom forces while the joint is unable to go to its target, followed by a brutal
        //   "unlocking" once the target rotation enter the allowed angle limit, usually resulting in
        //   a joint failure.
        // I'm not sure why the joint motion is set to limited in the first place. Setting it to "free"
        // solve the issue and doesn't seem to affect the joint behavior.
        static bool ModuleDockingNode_SetJointHighLowLimits_Prefix(ModuleDockingNode __instance)
        {
            UnlockRotationJoint(GetRotationJoint(__instance));
            return false;
        }

        static void UnlockRotationJoint(ConfigurableJoint rotationJoint)
        {
            if (rotationJoint.IsNotNullOrDestroyed())
                rotationJoint.angularXMotion = ConfigurableJointMotion.Free;
        }

        static void ModuleDockingNode_OnStart_Postfix(ModuleDockingNode __instance)
        {
            if (!__instance.canRotate)
            {
                __instance.Fields["nodeIsLocked"].guiActive = false;
                __instance.Fields["nodeIsLocked"].guiActiveEditor = false;
                return;
            }

            if (!dockingNodeInfos.TryGetValue(__instance.part, out DockingNodeInfo info))
            {
                info = new DockingNodeInfo(__instance);
                dockingNodeInfos.Add(__instance.part, info);
            }

            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                info.EditorSetup();
                __instance.Fields["targetAngle"].guiActiveEditor = !__instance.nodeIsLocked;
                __instance.Fields["inverted"].guiActiveEditor = !__instance.nodeIsLocked;
                __instance.Fields["nodeIsLocked"].guiActiveEditor = true;
            }
            else
            {
                __instance.Fields["targetAngle"].guiActive = !__instance.nodeIsLocked;
                __instance.Fields["inverted"].guiActive = !__instance.nodeIsLocked;
                __instance.Fields["nodeIsLocked"].guiActive = true;
            }

            __instance.Fields["targetAngle"].OnValueModified += info.OnTargetAngleModified;
        }

        // The ModuleDockingNode FSM setup is done in a coroutine skipping the first update cycle after OnStart().
        // OnStartFinished is also a frame delayed execution, that itself depend on the Part.Start() coroutine.
        // It is responsible for setting up the rotation feature, and require the FSM to be started.
        // The end result is that it's possible for OnStartFinished to be called while the FSM isn't started.
        // This will happen systematically if the PartStartStability patch is enabled, due to moving OnStartFinished
        // to the FixedUpdate() cycle. Arguably, that issue is a side effect of the PartStartStability patch, but the
        // stock code works "by chance", so we reimplement OnStartFinished as coroutine that actually check if the FSM
        // is started before running.
        static bool ModuleDockingNode_OnStartFinished_Prefix(ModuleDockingNode __instance)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return false;

            if (!__instance.canRotate)
                return false;

            if (__instance.fsm == null || __instance.fsm.CurrentState == null)
            {
                __instance.StartCoroutine(OnStartFinishedDelayed(__instance));
                return false;
            }

            if (dockingNodeInfos.TryGetValue(__instance.part, out DockingNodeInfo info))
                info.FlightSetup(__instance.JointTargetAngle);
            else
                Debug.LogWarning($"[DockingNodeDrift] Docking node info not found on {__instance.part}, drift correction won't be applied !");

            return true;
        }

        private static IEnumerator OnStartFinishedDelayed(ModuleDockingNode mdn)
        {
            while (mdn.vessel.packed || mdn.fsm == null || mdn.fsm.CurrentState == null)
                yield return null;

            if (mdn.otherNode.IsNotNullOrDestroyed() && (mdn.otherNode.fsm == null || mdn.fsm.CurrentState == null))
                yield return null;

            float jointTargetAngle = mdn.JointTargetAngle;

            if (dockingNodeInfos.TryGetValue(mdn.part, out DockingNodeInfo info))
                info.FlightSetup(jointTargetAngle);
            else
                Debug.LogWarning($"[DockingNodeDrift] Docking node info not found on {mdn.part}, drift correction won't be applied !");

            if (mdn.otherNode.IsNullOrDestroyed())
            {
                mdn.rotationInitComplete = true;
                yield break;
            }

            ConfigurableJoint rotationJoint = GetRotationJoint(mdn);

            if (rotationJoint.IsNullOrDestroyed() || mdn.rotationTransform.IsNullOrDestroyed())
                yield break;

            float visualTargetAngle = GetVisualTargetAngle(mdn, rotationJoint);

            rotationJoint.angularXMotion = ConfigurableJointMotion.Limited;

            mdn.driveTargetAngle = jointTargetAngle;
            mdn.cachedInitialAngle = jointTargetAngle;
            mdn.initialRotation = mdn.rotationTransform.localRotation.eulerAngles;

            if (rotationJoint == mdn.part.attachJoint.DestroyedAsNull()?.Joint)
            {
                Quaternion targetLocalRotation = mdn.SetTargetRotation(Quaternion.identity, jointTargetAngle - mdn.cachedInitialAngle, true, Vector3.up);
                rotationJoint.SetTargetRotationLocal(targetLocalRotation, Quaternion.identity);
                mdn.targetRotation = mdn.SetTargetRotation(Quaternion.Euler(mdn.initialRotation), visualTargetAngle, false);
            }
            else
            {
                mdn.targetRotation = mdn.SetTargetRotation(Quaternion.Euler(mdn.initialRotation), 0f - visualTargetAngle, false);
            }

            mdn.rotationTransform.localRotation = mdn.targetRotation;
            mdn.visualTargetAngle = visualTargetAngle;

            mdn.rotationInitComplete = true;
        }

        private static void ModuleDockingNode_OnDestroy_Postfix(ModuleDockingNode __instance)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            if (dockingNodeInfos.TryGetValue(__instance.part, out DockingNodeInfo info))
            {
                __instance.Fields["targetAngle"].OnValueModified -= info.OnTargetAngleModified;
                dockingNodeInfos.Remove(__instance.part);
            }
        }

        static bool ModuleDockingNode_RotationJoint_Prefix(ModuleDockingNode __instance, out ConfigurableJoint __result)
        {
            __result = GetRotationJoint(__instance);
            return false;
        }

        static ConfigurableJoint GetRotationJoint(ModuleDockingNode mdn)
        {
            if (IsDocked(mdn))
            {
                if (mdn.sameVesselDockJoint.IsNotNullOrDestroyed())
                    return null;

                if (mdn.part.parent == mdn.otherNode.part)
                    return mdn.part.attachJoint.DestroyedAsNull()?.Joint;
                else
                    return mdn.otherNode.part.attachJoint.DestroyedAsNull()?.Joint;
            }

            if (mdn.referenceNode != null && mdn.referenceNode.attachedPart.IsNotNullOrDestroyed() && mdn.referenceNode.attachedPart.parent == mdn.part)
                return mdn.referenceNode.attachedPart.attachJoint.DestroyedAsNull()?.Joint;

            return null;
        }

        static bool IsDocked(ModuleDockingNode mdn)
        {
            if (!mdn.fsm.fsmStarted)
                return false;

            return mdn.otherNode.IsNotNullOrDestroyed()
                   && (mdn.fsm.currentState == mdn.st_docked_docker 
                       || mdn.fsm.currentState == mdn.st_docked_dockee 
                       || mdn.fsm.currentState == mdn.st_preattached);
        }

        static bool ModuleDockingNode_VisualTargetAngle_Prefix(ModuleDockingNode __instance, out float __result)
        {
            __result = GetVisualTargetAngle(__instance, GetRotationJoint(__instance));
            return false;
        }

        static float GetVisualTargetAngle(ModuleDockingNode mdn, ConfigurableJoint rotationJoint)
        {
            if (rotationJoint.IsNotNullOrDestroyed() && rotationJoint == mdn.part.attachJoint.DestroyedAsNull()?.Joint)
            {
                if (mdn.inverted)
                    return mdn.targetAngle;
                else
                    return mdn.targetAngle * -1f;
            }
            else
            {
                if (mdn.inverted)
                    return mdn.targetAngle * -1f;
                else
                    return mdn.targetAngle;
            }
        }

        static bool ModuleDockingNode_OnPartUnpack_Prefix(ModuleDockingNode __instance)
        {
            if (!__instance.canRotate || !__instance.rotationInitComplete)
                return false;

            ConfigurableJoint rotationJoint = GetRotationJoint(__instance);
            UnlockRotationJoint(rotationJoint);

            if (!__instance.IsRotating)
            {
                __instance.driveTargetAngle = __instance.JointTargetAngle;
                __instance.visualTargetAngle = GetVisualTargetAngle(__instance, rotationJoint);
                
                if (rotationJoint.IsNotNullOrDestroyed() && rotationJoint == __instance.part.attachJoint.DestroyedAsNull()?.Joint)
                {
                    Quaternion targetLocalRotation = __instance.SetTargetRotation(Quaternion.identity, __instance.driveTargetAngle - __instance.cachedInitialAngle, true, Vector3.up);
                    rotationJoint.SetTargetRotationLocal(targetLocalRotation, Quaternion.identity);
                }
            }

            return false;
        }

        

        static bool ModuleDockingNode_UpdatePAWUI_Prefix(ModuleDockingNode __instance)
        {
            if (!__instance.canRotate)
                return false;

            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                __instance.Fields["targetAngle"].guiActiveEditor = !__instance.nodeIsLocked;
                __instance.Fields["inverted"].guiActiveEditor = !__instance.nodeIsLocked;
            }
            else
            {
                bool canUnlock = __instance.sameVesselDockJoint.IsNullOrDestroyed();
                bool rotationEnabled = !__instance.nodeIsLocked && canUnlock;
                __instance.Fields["targetAngle"].guiActive = rotationEnabled;
                __instance.Fields["inverted"].guiActive = rotationEnabled;
                __instance.Fields["nodeIsLocked"].guiActive = canUnlock;
            }

            return false;
        }

        // We reimplement the method because :
        // - We want to insert extra handling to allow rotating when not docked and doing it with
        //   a non-overriding prefix would be messy and inefficient
        // - The stock implementation doesn't work when both docking port are moving at the same time
        static bool ModuleDockingNode_UpdateAlignmentRotation_Prefix(ModuleDockingNode __instance)
        {
            if (!__instance.hasEnoughResources || !__instance.rotationInitComplete || __instance.sameVesselDockJoint.IsNotNullOrDestroyed())
                return false;

            __instance.targetAngle = Mathf.Clamp(__instance.targetAngle, __instance.hardMinMaxLimits.x, __instance.hardMinMaxLimits.y);

            // handle rotation while not docked
            // we only need to update the moving transform rotation
            if (!IsDocked(__instance))
            {
                float finalTargetAngle = GetVisualTargetAngle(__instance, null);
                __instance.IsRotating = __instance.visualTargetAngle != finalTargetAngle;

                if (__instance.IsRotating)
                {
                    __instance.maxAnglePerFrame = __instance.traverseVelocity * Time.fixedDeltaTime;
                    __instance.visualTargetAngle = Mathf.MoveTowards(__instance.visualTargetAngle, finalTargetAngle, __instance.maxAnglePerFrame);
                    __instance.targetRotation = __instance.SetTargetRotation(Quaternion.Euler(__instance.initialRotation), 0f - __instance.visualTargetAngle, false);
                    __instance.rotationTransform.localRotation = __instance.targetRotation;
                }

                return false;
            }

            // handle rotation while docked

            ConfigurableJoint rotationJoint = GetRotationJoint(__instance);
            bool isJointOwner = rotationJoint == __instance.part.attachJoint.DestroyedAsNull()?.Joint;

            // If both ports can rotate, both ports are handled from the "child" docking port side (the joint owner)
            // This is needed because when both ports are rotating at the same time, we need to know each port rotation direction
            // to adjust the rotation speed of the joint.
            // Note that if one port can rotate and not the other, the rotation is handled by the port that rotate, no matter if
            // it's the joint owner or not.
            // So if we are executing from the "parent" port (not the joint owner), and both ports can rotate, abort.
            if (!isJointOwner && __instance.otherNode.canRotate)
                return false;

            // get the joint desired angle (combination of both docking port targetAngle fields)
            float jointFinalTargetAngle = __instance.JointTargetAngle;

            // if the joint desired angle isn't reached, we need to rotate
            bool isJointRotating = __instance.driveTargetAngle != jointFinalTargetAngle;

            // handle state changes, and abort if not rotating
            if (isJointRotating)
            {
                if (!__instance.IsRotating)
                {
                    __instance.IsRotating = true;
                    rotationJoint.angularXMotion = ConfigurableJointMotion.Free;
                    rotationJoint.breakTorque *= 10f;
                    rotationJoint.breakForce *= 10f;
                    __instance.partJointUnbreakable = true; // useless but lets be consistent with stock state
                }
            }
            else
            {
                if (__instance.IsRotating)
                {
                    __instance.IsRotating = false;
                    rotationJoint.breakTorque /= 10f;
                    rotationJoint.breakForce /= 10f;
                    __instance.partJointUnbreakable = false; // useless but lets be consistent with stock state
                }

                return false;
            }

            // isJointRotating is true if any of the two docking ports are rotating.
            // We also need to know if the current docking port (__instance) is really rotating.
            // FIXME: this will (wrongly) double the EC consumption, but well... 
            float visualFinalTargetAngle = GetVisualTargetAngle(__instance, rotationJoint);
            bool isRotatingSelf = __instance.visualTargetAngle != visualFinalTargetAngle;

            __instance.maxAnglePerFrame = __instance.traverseVelocity * Time.fixedDeltaTime;
            float jointAnglePerFrame = __instance.maxAnglePerFrame;
            ModuleDockingNode otherNode = __instance.otherNode;

            // we only need to handle the other node if __instance is the joint owner and if both ports can rotate
            if (isJointOwner && otherNode.canRotate)
            {
                // Do the same for the other node
                // The other node IsRotating property will be true only if it is actually rotating
                float otherNodeVisualFinalTargetAngle = GetVisualTargetAngle(otherNode, rotationJoint);
                otherNode.IsRotating = otherNode.visualTargetAngle != otherNodeVisualFinalTargetAngle;

                // before we move the joint, we need to handle the case where the two docking ports are rotating at the same time
                // If they are rotating in :
                // - the same direction => the joint rotation speed is doubled
                // - opposite direcions => the joint doesn't rotate

                if (isRotatingSelf && otherNode.IsRotating)
                {
                    bool angleIsIncreasing = __instance.visualTargetAngle < visualFinalTargetAngle;
                    bool otherAngleIsIncreasing = otherNode.visualTargetAngle > otherNodeVisualFinalTargetAngle;
                    if (angleIsIncreasing == otherAngleIsIncreasing)
                        jointAnglePerFrame *= 2f;
                    else
                        jointAnglePerFrame = 0f;
                }

                // if the other node is rotating, apply the rotation to the moving transform
                if (otherNode.IsRotating)
                {
                    otherNode.visualTargetAngle = Mathf.MoveTowards(otherNode.visualTargetAngle, otherNodeVisualFinalTargetAngle, __instance.maxAnglePerFrame);
                    otherNode.targetRotation = otherNode.SetTargetRotation(Quaternion.Euler(otherNode.initialRotation), 0f - otherNode.visualTargetAngle, false);
                    otherNode.rotationTransform.localRotation = otherNode.targetRotation;
                }
            }

            // FIXME : there is still some (tiny) desynchronization happening when both ports are moving and when reaching the "inversion point"
            // I guess the angle deltas should be clamped to the lowest result, but I'm unsure how to do that and the error is unlikely to ever matter

            // if we are rotating, apply the rotation to the moving transform
            if (isRotatingSelf)
            {
                __instance.visualTargetAngle = Mathf.MoveTowards(__instance.visualTargetAngle, visualFinalTargetAngle, __instance.maxAnglePerFrame);

                float visualTargetAngle = __instance.visualTargetAngle;
                if (!isJointOwner && !otherNode.canRotate)
                    visualTargetAngle *= -1f;

                __instance.targetRotation = __instance.SetTargetRotation(Quaternion.Euler(__instance.initialRotation), visualTargetAngle, false);
                __instance.rotationTransform.localRotation = __instance.targetRotation;
            }

            // apply the joint rotation
            if (jointAnglePerFrame > 0f)
            {
                __instance.driveTargetAngle = Mathf.MoveTowards(__instance.driveTargetAngle, jointFinalTargetAngle, jointAnglePerFrame);
                otherNode.driveTargetAngle = __instance.driveTargetAngle; // not necessary but this replicate stock behavior
                Quaternion targetLocalRotation = __instance.SetTargetRotation(Quaternion.identity, __instance.driveTargetAngle - __instance.cachedInitialAngle, true, __instance.GetRotationAxis());
                rotationJoint.SetTargetRotationLocal(targetLocalRotation, Quaternion.identity);
                __instance.ApplyCoordsUpdate();
            }

            return false;
        }

        static bool ModuleDockingNode_ApplyCoordsUpdate_Prefix(ModuleDockingNode __instance)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return true;

            if (!dockingNodeInfos.TryGetValue(__instance.part, out DockingNodeInfo info))
            {
                Debug.LogWarning($"[DockingNodeDrift] Docking node info not found on {__instance.part}, drift correction won't be applied !");
                return true;
            }

            return !info.TryPristineCoordsUpdate();
        }

        static bool ModuleDockingNode_ModifyLocked_Prefix(ModuleDockingNode __instance)
        {
            if (!__instance.canRotate)
                return false;

            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                // We replicate the stock behavior here, but I fail to see why this is needed

                if (EditorLogic.fetch.ship == null)
                    return false;

                for (int i = 0; i < EditorLogic.fetch.ship.parts.Count; i++)
                    EditorLogic.fetch.ship.parts[i].CycleAutoStrut();
            }
            else
            {
                if (__instance.nodeIsLocked)
                {
                    if (__instance.IsRotating)
                    {
                        __instance.nodeIsLocked = false;
                        return false;
                    }

                    __instance.RecurseCoordUpdate(__instance.part, __instance.vessel.rootPart);
                }

                __instance.vessel.CycleAllAutoStrut();
            }

            return false;
        }

        static bool ModuleDockingNode_IsJointUnlocked_Prefix(ModuleDockingNode __instance, out bool __result)
        {
            if (IsDocked(__instance) && (__instance.canRotate || __instance.otherNode.canRotate))
                __result = !__instance.nodeIsLocked || !__instance.otherNode.nodeIsLocked;
            else
                __result = false;

            return false;
        }

        private class DockingNodeInfo
        {
            private static readonly Queue<Part> partQueue = new Queue<Part>();

            private readonly ModuleDockingNode dockingNode;
            private readonly float initialTargetAngle;
            private readonly Vector3d mainAxis;
            private double lastRotAngle;
            private QuaternionD rotOffset;

            public DockingNodeInfo(ModuleDockingNode dockingNode)
            {
                this.dockingNode = dockingNode;
                mainAxis = (Vector3d)dockingNode.GetRotationAxis() * -1.0; // why do I need that inversion is a mystery
                initialTargetAngle = dockingNode.targetAngle;
            }

            public void FlightSetup(double initialRotAngle)
            {
                lastRotAngle = initialRotAngle;
            }

            public void EditorSetup()
            {
                double rotAngle = dockingNode.targetAngle;
                if (dockingNode.inverted)
                    rotAngle *= -1.0;

                lastRotAngle = rotAngle;

                if (rotAngle == 0.0)
                    return;

                QuaternionD rotationOffset = QuaternionD.AngleAxis(rotAngle, mainAxis);
                dockingNode.rotationTransform.localRotation = (QuaternionD)dockingNode.rotationTransform.localRotation * rotationOffset;
            }

            public bool TryPristineCoordsUpdate()
            {
                double angleOffset = dockingNode.driveTargetAngle - lastRotAngle;
                if (angleOffset == 0.0)
                    return true;

                if (!dockingNodeInfos.TryGetValue(dockingNode.otherNode.part, out DockingNodeInfo otherNodeInfo))
                {
                    Debug.LogWarning($"[DockingNodeDrift] Docking node info not found on {dockingNode.otherNode.part}, drift correction won't be applied !");
                    return false;
                }

                lastRotAngle = dockingNode.driveTargetAngle;
                otherNodeInfo.lastRotAngle = lastRotAngle;

                rotOffset = QuaternionD.AngleAxis(angleOffset, mainAxis);

                QuaternionD localToVesselSpace = GetLocalToVesselSpace();

                // transform offset from the servo local space to the vessel space
                rotOffset = QuaternionD.Inverse(localToVesselSpace) * rotOffset * localToVesselSpace;

                // if docking node is the joint owner
                // - invert the offset
                // - rotate the docking node part itself
                // - recursively rotate all childs
                // else
                // - recursively rotate the child docking port, and all its childs
                if (GetRotationJoint(dockingNode) == dockingNode.part.attachJoint.DestroyedAsNull()?.Joint)
                {
                    rotOffset = QuaternionD.Inverse(rotOffset);
                    dockingNode.part.orgRot = rotOffset * (QuaternionD)dockingNode.part.orgRot;

                    for (int i = 0; i < dockingNode.part.children.Count; i++)
                        RecurseChildCoordsUpdate(dockingNode.part.children[i], Vector3d.zero);
                }
                else
                {
                    RecurseChildCoordsUpdate(dockingNode.otherNode.part, Vector3d.zero);
                }

                return true;
            }

            private void RecurseChildCoordsUpdate(Part part, Vector3d posOffset)
            {
                // get position offset between this part and its parent
                Vector3d orgPosOffset = part.orgPos - part.parent.orgPos;
                // add the upstream position offset (from all parents), then rotate the result
                orgPosOffset = rotOffset * (orgPosOffset + posOffset);
                // get the new position for this part
                Vector3d newOrgPos = part.parent.orgPos + orgPosOffset;
                // update the total offset that will be applied downstream to childrens
                posOffset = newOrgPos - part.orgPos;
                // apply the new position
                part.orgPos = newOrgPos;

                // apply the rotation
                part.orgRot = rotOffset * (QuaternionD)part.orgRot;

                // propagate to childrens
                for (int i = 0; i < part.children.Count; i++)
                    RecurseChildCoordsUpdate(part.children[i], posOffset);
            }

            private QuaternionD GetLocalToVesselSpace()
            {
                // for some reason not normalizing can end up with a quaternion with near infinity components 
                // when it should be identity, leading to infinity and NaN down the line...
                return (QuaternionD.Inverse(dockingNode.part.orgRot) * (QuaternionD)dockingNode.part.vessel.rootPart.orgRot).Normalize();
            }

            internal void OnTargetAngleModified(object newValue)
            {
                // prevent changing targetAngle when rotation init isn't done (which would result in a borked initial rotation)
                // can happen on vessel load when targetAngle is driven by a robotic controller left playing
                if (HighLogic.LoadedScene == GameScenes.FLIGHT && !dockingNode.rotationInitComplete)
                {
                    dockingNode.targetAngle = initialTargetAngle;
                    return;
                }

                // auto-unlock the port if targetAngle is modified
                // moving the port while locked would result in the in-physics rotation being applied
                // without being propagated to orgPos/orgRot. Stock doesn't check the locked state, and
                // there are too many places that would need to be patched, so we implement this as a workaround.
                // In practice, this is mainly necessary when using the axis field in a robotic controller.
                // We also check this in the editor, as this somehow affect autostruting rules there too.
                if (dockingNode.nodeIsLocked)
                {
                    dockingNode.nodeIsLocked = false;
                    dockingNode.ModifyLocked(null);
                }

                // handle rotation in the editor
                // this is entirely missing in the stock implementation, despite the rotation feature being available
                // through robotic controllers. Using a robotic controller while in the editor in stock will have no visible
                // effect, but will result in a broken initial docking port state when launching the vessel
                if (HighLogic.LoadedScene == GameScenes.EDITOR)
                {
                    double rotAngle = (float)newValue;
                    if (dockingNode.inverted)
                        rotAngle *= -1.0;

                    double angleOffset = rotAngle - lastRotAngle;

                    if (angleOffset == 0.0)
                        return;

                    lastRotAngle = rotAngle;

                    QuaternionD rotationOffset = QuaternionD.AngleAxis(angleOffset, mainAxis);

                    dockingNode.rotationTransform.localRotation = (QuaternionD)dockingNode.rotationTransform.localRotation * rotationOffset;

                    // ModuleDockingNode.referenceNode reference acquisition is in OnLoad(), which won't be called for newly instantiated parts
                    // but since AttachNode is serializable, the field will still be populated with an useless instance
                    // So we always acquire the AttachNode instance from the part.
                    AttachNode attachNode = dockingNode.part.FindAttachNode(dockingNode.referenceAttachNode);
                    if (attachNode != null && attachNode.attachedPart.IsNotNullOrDestroyed() && attachNode.attachedPart.HasModuleImplementing<ModuleDockingNode>())
                    {
                        Part rotatingPart;
                        // if the docking port is the child of the docking port pair, rotate itself, else rotate the child docking port
                        if (attachNode.attachedPart.NotDestroyedRefEquals(dockingNode.part.parent))
                            rotatingPart = dockingNode.part;
                        else
                            rotatingPart = attachNode.attachedPart;

                        rotatingPart.transform.rotation = (QuaternionD)rotatingPart.transform.rotation * QuaternionD.Inverse(rotationOffset);

                        // update orgPos/orgRot of the part and all its childs
                        Part rootPart = EditorLogic.fetch.ship.parts[0];
                        Quaternion inverseRootRotation = Quaternion.Inverse(rootPart.partTransform.rotation);

                        partQueue.Clear();
                        partQueue.Enqueue(rotatingPart);

                        while (partQueue.TryDequeue(out Part nextPart))
                        {
                            int childCount = nextPart.children.Count;
                            for (int i = 0; i < childCount; i++)
                                partQueue.Enqueue(nextPart.children[i]);

                            nextPart.orgPos = rootPart.partTransform.InverseTransformPoint(nextPart.partTransform.position);
                            nextPart.orgRot = inverseRootRotation * nextPart.partTransform.rotation;
                        }
                    }
                }
            }
        }
    }


}
