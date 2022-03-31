using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// TODO :
// - allow changing rotation while not docked
// - allow changing rotation in the editor (necessary if used in a servo controller)

namespace KSPCommunityFixes.BugFixes
{
    class DockingPortRotationDriftAndFixes : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
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
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.UpdatePAWUI)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.ApplyCoordsUpdate)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.SetJointHighLowLimits)),
                this));

            GameEvents.onGameSceneLoadRequested.Add(OnSceneSwitch);
        }

        private static readonly Dictionary<Part, DockingNodeInfo> dockingNodeInfos = new Dictionary<Part, DockingNodeInfo>();

        private void OnSceneSwitch(GameScenes data)
        {
            dockingNodeInfos.Clear();
        }

        static void ModuleDockingNode_OnStart_Postfix(ModuleDockingNode __instance)
        {
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
                return;

            if (__instance.nodeIsLocked)
            {
                __instance.Fields["targetAngle"].guiActive = false;
                __instance.Fields["inverted"].guiActive = false;
            }

            if (!dockingNodeInfos.TryGetValue(__instance.part, out DockingNodeInfo info))
            {
                info = new DockingNodeInfo(__instance);
                dockingNodeInfos.Add(__instance.part, info);
            }

            __instance.Fields["targetAngle"].OnValueModified += info.OnTargetAngleModified;
        }

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

            info.Setup(__instance.JointTargetAngle);

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

            info.Setup(jointTargetAngle);

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

        static void ModuleDockingNode_UpdatePAWUI_Postfix(ModuleDockingNode __instance)
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && __instance.canRotate)
            {
                bool isEnabled = !__instance.nodeIsLocked;
                __instance.Fields["targetAngle"].guiActive = isEnabled;
                __instance.Fields["inverted"].guiActive = isEnabled;

            }
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

        static bool ModuleDockingNode_SetJointHighLowLimits_Prefix(ModuleDockingNode __instance)
        {
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
            // solve the issue and doesn't seem to affect the joint strength.

            ConfigurableJoint joint = __instance.RotationJoint;
            if (joint != null)
                joint.angularXMotion = ConfigurableJointMotion.Free;

            return false;
        }

        private class DockingNodeInfo
        {
            private readonly ModuleDockingNode dockingNode;
            private readonly Vector3d mainAxis;
            private bool isJointOwner;
            private double lastRotAngle;
            private QuaternionD rotOffset;

            public DockingNodeInfo(ModuleDockingNode dockingNode)
            {
                this.dockingNode = dockingNode;
                mainAxis = (Vector3d)dockingNode.GetRotationAxis() * -1.0;
            }

            public void Setup(double initialRotAngle)
            {
                isJointOwner = dockingNode.RotationJoint == dockingNode.part.attachJoint?.Joint;
                lastRotAngle = initialRotAngle;
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
                if (dockingNode.nodeIsLocked)
                {
                    dockingNode.nodeIsLocked = false;
                    dockingNode.ModifyLocked(null);
                }

                if (HighLogic.LoadedScene == GameScenes.EDITOR)
                {
                    bool dockedAsChild;
                    if (dockingNode.referenceNode != null && dockingNode.referenceNode.attachedPart != null)
                    {
                        dockedAsChild = dockingNode.referenceNode.attachedPart == dockingNode.part.parent;
                    }
                    else
                    {
                        return;
                    }

                    double rotAngle = (double) newValue;
                    double angleOffset = lastRotAngle - rotAngle;
                    lastRotAngle = rotAngle;

                    QuaternionD rotationOffset = QuaternionD.AngleAxis(angleOffset, mainAxis);

                    dockingNode.rotationTransform.localRotation = (QuaternionD)dockingNode.rotationTransform.localRotation * rotationOffset;

                    // rotate itself
                    if (dockedAsChild)
                    {
                        dockingNode.transform.localRotation = (QuaternionD)dockingNode.transform.localRotation * rotationOffset;
                    }
                    else
                    {
                        // transform in world space
                    }
                }
            }
        }
    }
}
