using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

// Remaining issues :
// - there is a visual glitch where the rotating part becomes misaligned when both ports are rotating at the same time
//   not sure why this happen and how to fix this properly
//   in any case this is visual and "fixes itself" once the rotation is complete, but this will cause a persistent desync
//   if the vessel is saved while this happen (quite unlikely, but still...)

namespace KSPCommunityFixes.BugFixes
{
    class DockingPortRotationDriftAndFixes : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
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
                ConfigurableJoint rotationJoint = __instance.RotationJoint;

                // this is the reverse of the VisualTargetAngle property
                float currentTargetAngle = __instance.visualTargetAngle;
                if (rotationJoint != null && rotationJoint == __instance.part.attachJoint.Joint)
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
            ConfigurableJoint joint = __instance.RotationJoint;
            if (joint != null)
                joint.angularXMotion = ConfigurableJointMotion.Free;

            return false;
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
        static bool ModuleDockingNode_OnStartFinished_Prefix(ModuleDockingNode __instance, PartModule.StartState state)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return false;

            if (!__instance.canRotate)
                return false;

            if (__instance.fsm == null || __instance.fsm.CurrentState == null)
            {
                __instance.StartCoroutine(OnStartFinishedDelayed(__instance, state));
                return false;
            }

            if (!dockingNodeInfos.TryGetValue(__instance.part, out DockingNodeInfo info))
            {
                info = new DockingNodeInfo(__instance);
                dockingNodeInfos.Add(__instance.part, info);
            }

            info.FlightSetup(__instance.JointTargetAngle);

            return true;
        }

        private static IEnumerator OnStartFinishedDelayed(ModuleDockingNode mdn, PartModule.StartState state)
        {
            while (mdn.fsm == null || mdn.fsm.CurrentState == null)
                yield return null;

            if (mdn.otherNode != null && (mdn.otherNode.fsm == null || mdn.fsm.CurrentState == null))
                yield return null;

            float jointTargetAngle = mdn.JointTargetAngle;
            if (!dockingNodeInfos.TryGetValue(mdn.part, out DockingNodeInfo info))
            {
                info = new DockingNodeInfo(mdn);
                dockingNodeInfos.Add(mdn.part, info);
            }

            info.FlightSetup(jointTargetAngle);

            if (mdn.otherNode == null)
            {
                mdn.rotationInitComplete = true;
                yield break;
            }

            if (mdn.RotationJoint == null || mdn.part.attachJoint == null || mdn.rotationTransform == null)
                yield break;

            ConfigurableJoint joint = mdn.RotationJoint;
            float visualTargetAngle = mdn.VisualTargetAngle;

            joint.angularXMotion = ConfigurableJointMotion.Limited;

            mdn.driveTargetAngle = jointTargetAngle;
            mdn.cachedInitialAngle = jointTargetAngle;
            mdn.initialRotation = mdn.rotationTransform.localRotation.eulerAngles;

            if (joint == mdn.part.attachJoint.Joint)
            {
                Quaternion targetLocalRotation = mdn.SetTargetRotation(Quaternion.identity, jointTargetAngle - mdn.cachedInitialAngle, true, Vector3.up);
                joint.SetTargetRotationLocal(targetLocalRotation, Quaternion.identity);
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
                bool canUnlock = __instance.sameVesselDockJoint == null;
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
        // - The stock code is extremly inefficient, calling some code heavy properties several times
        static bool ModuleDockingNode_UpdateAlignmentRotation_Prefix(ModuleDockingNode __instance)
        {
            if (!(__instance.canRotate && __instance.hasEnoughResources && __instance.rotationInitComplete 
                 && __instance.targetAngle >= __instance.hardMinMaxLimits.x && __instance.targetAngle <= __instance.hardMinMaxLimits.y)
                 && __instance.sameVesselDockJoint == null)
                return false;

            ConfigurableJoint joint = __instance.RotationJoint;
            __instance.maxAnglePerFrame = __instance.traverseVelocity * Time.fixedDeltaTime;

            float finalVisualTargetAngle = __instance.VisualTargetAngle;

            // custom implementation (allow rotation while not docked) :
            if (joint == null)
            {
                __instance.IsRotating = __instance.visualTargetAngle != finalVisualTargetAngle;

                if (__instance.IsRotating)
                {
                    __instance.IsRotating = true;
                    __instance.visualTargetAngle = Mathf.MoveTowards(__instance.visualTargetAngle, finalVisualTargetAngle, __instance.maxAnglePerFrame);
                    __instance.targetRotation = __instance.SetTargetRotation(Quaternion.Euler(__instance.initialRotation), 0f - __instance.visualTargetAngle, false);
                    __instance.rotationTransform.localRotation = __instance.targetRotation;
                }
            }
            // stock implementation :
            else
            {
                float finalJointTargetAngle = __instance.JointTargetAngle;
                if (__instance.driveTargetAngle != finalJointTargetAngle)
                {
                    if (!__instance.IsRotating)
                    {
                        __instance.IsRotating = true;
                        joint.angularXMotion = ConfigurableJointMotion.Free;
                    }

                    __instance.driveTargetAngle = Mathf.MoveTowards(__instance.driveTargetAngle, finalJointTargetAngle, __instance.maxAnglePerFrame);
                    __instance.visualTargetAngle = Mathf.MoveTowards(__instance.visualTargetAngle, finalVisualTargetAngle, __instance.maxAnglePerFrame);

                    if (joint == __instance.part.attachJoint.Joint)
                    {
                        if (!__instance.partJointUnbreakable)
                        {
                            joint.breakTorque *= 10f;
                            joint.breakForce *= 10f;
                            __instance.partJointUnbreakable = true;
                        }

                        Quaternion targetLocalRotation = __instance.SetTargetRotation(Quaternion.identity, __instance.driveTargetAngle - __instance.cachedInitialAngle, true, Vector3.up);
                        joint.SetTargetRotationLocal(targetLocalRotation, Quaternion.identity);
                        __instance.ApplyCoordsUpdate();
                        __instance.targetRotation = __instance.SetTargetRotation(Quaternion.Euler(__instance.initialRotation), __instance.visualTargetAngle, false);
                        __instance.rotationTransform.localRotation = __instance.targetRotation;
                    }
                    else
                    {
                        __instance.targetRotation = __instance.SetTargetRotation(Quaternion.Euler(__instance.initialRotation), 0f - __instance.visualTargetAngle, false);
                        __instance.rotationTransform.localRotation = __instance.targetRotation;
                    }
                }
                else
                {
                    __instance.IsRotating = false;
                    if (__instance.partJointUnbreakable)
                    {
                        joint.breakTorque /= 10f;
                        joint.breakForce /= 10f;
                        __instance.partJointUnbreakable = false;
                    }
                }
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

        private class DockingNodeInfo
        {
            private static Queue<Part> partQueue = new Queue<Part>();

            private readonly ModuleDockingNode dockingNode;
            private readonly Vector3d mainAxis;
            private bool isJointOwner;
            private double lastRotAngle;
            private QuaternionD rotOffset;

            public DockingNodeInfo(ModuleDockingNode dockingNode)
            {
                this.dockingNode = dockingNode;
                mainAxis = (Vector3d)dockingNode.GetRotationAxis() * -1.0; // why do I need that inversion is a mystery
            }

            public void FlightSetup(double initialRotAngle)
            {
                isJointOwner = dockingNode.RotationJoint == dockingNode.part.attachJoint?.Joint;
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
                if (isJointOwner)
                {
                    rotOffset = QuaternionD.Inverse(rotOffset);
                    dockingNode.part.orgRot = rotOffset * (QuaternionD)dockingNode.part.orgRot;
                }

                for (int i = 0; i < dockingNode.part.children.Count; i++)
                {
                    RecurseChildCoordsUpdate(dockingNode.part.children[i], Vector3d.zero);
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

                    if (dockingNode.referenceNode != null && dockingNode.referenceNode.attachedPart != null)
                    {
                        Part rotatingPart;
                        // if the docking port is the child of the docking part pair, rotate itself, else rotate the child docking port
                        if (dockingNode.referenceNode.attachedPart == dockingNode.part.parent)
                            rotatingPart = dockingNode.part;
                        else
                            rotatingPart = dockingNode.referenceNode.attachedPart;

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
