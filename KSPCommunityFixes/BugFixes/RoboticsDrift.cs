using Expansions.Serenity;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    public class RoboticsDrift : BasePatch
    {
        private class PartPosition
        {
            public Vector3 orgPos;
            public Quaternion orgRot;

            public PartPosition(Part part)
            {
                orgPos = part.orgPos;
                orgRot = part.orgRot;
            }
        }

        private class RobotJointOffset
        {
            public bool ready = false;
            public Quaternion worldToJointSpace;
            public Vector3 initialOffset;
            public Quaternion rotOffset;

            public RobotJointOffset() {}

            public RobotJointOffset(ConfigurableJoint joint)
            {
                worldToJointSpace = GetWorldToJointSpace(joint);
                ready = true;
            }

            public static Quaternion GetWorldToJointSpace(ConfigurableJoint joint)
            {
                // Calculate the joint rotation expressed by the joint's axis and secondary axis
                // See https://gist.github.com/mstevenson/4958837
                Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
                Vector3 up = Vector3.Cross(forward, joint.axis).normalized;
                return Quaternion.LookRotation(forward, up);
            }
        }

        private static Dictionary<int, PartPosition> partPositions = new Dictionary<int, PartPosition>();
        private static Dictionary<int, RobotJointOffset> robotJointOffsets = new Dictionary<int, RobotJointOffset>();

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseServo), "RecurseCoordUpdate"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.OnSave)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.OnLoad)),
                this));
        }

        private static bool BaseServo_RecurseCoordUpdate_Prefix(BaseServo __instance, Part p, Part rootPart, ConfigurableJoint ___servoJoint, GameObject ___movingPartObject)
        {
            if (ReferenceEquals(___servoJoint, null) || p == null || rootPart == null || ___movingPartObject == null)
                return true;

            if (!robotJointOffsets.TryGetValue(p.GetInstanceID(), out RobotJointOffset jointOffset))
            {
                jointOffset = new RobotJointOffset(___servoJoint);
                robotJointOffsets.Add(p.GetInstanceID(), jointOffset);
            }
            else if (!jointOffset.ready)
            {
                jointOffset.worldToJointSpace = RobotJointOffset.GetWorldToJointSpace(___servoJoint);
                jointOffset.ready = true;
            }

            // rotation from A to B : A.Inverse() * B

            Vector3 posOffset = (Quaternion.Inverse(___movingPartObject.transform.localRotation) * jointOffset.worldToJointSpace) * ___servoJoint.targetPosition;

            // ModuleRoboticServoPiston use a manually configured anchor, other modules don't
            if (!___servoJoint.autoConfigureConnectedAnchor)
                posOffset += ___servoJoint.connectedAnchor;

            posOffset -= jointOffset.initialOffset;

            Quaternion rotOffset = Quaternion.Inverse(___movingPartObject.transform.localRotation) * jointOffset.worldToJointSpace * ___servoJoint.targetRotation;

            for (int i = 0; i < p.children.Count; i++)
            {
                RecursePristineCoordsUpdate(p.children[i], posOffset);
            }

            return false;
        }

        private static void RecursePristineCoordsUpdate(Part part, Vector3 posOffset)
        {
            if (!partPositions.TryGetValue(part.GetInstanceID(), out PartPosition partPosition))
            {
                partPosition = new PartPosition(part);
                partPositions.Add(part.GetInstanceID(), partPosition);
            }

            part.orgPos = partPosition.orgPos + posOffset;

            for (int i = 0; i < part.children.Count; i++)
            {
                RecursePristineCoordsUpdate(part.children[i], posOffset);
            }
        }

        // TODO: we don't correct the coords drift of the movingPartObject.
        // Stock save it in servoTransformPosition/servoTransformRotation in OnSave, and restore it on load.
        // Computing pristine coords for it based on the joint targets should be doable too.
        private static void BaseServo_OnSave_Prefix(BaseServo __instance, ConfigNode node, ConfigurableJoint ___servoJoint, GameObject ___movingPartObject)
        {
            if (ReferenceEquals(___servoJoint, null) || ___movingPartObject == null)
                return;

            Quaternion worldToJointSpace;

            if (robotJointOffsets.TryGetValue(__instance.part.GetInstanceID(), out RobotJointOffset jointOffset))
                worldToJointSpace = jointOffset.worldToJointSpace;
            else
                worldToJointSpace = RobotJointOffset.GetWorldToJointSpace(___servoJoint);

            GameObject pristineMovingPartObject = __instance.part.partInfo.partPrefab.gameObject.GetChild(__instance.servoTransformName);
            Quaternion pristineMovingPartObjectRotation = pristineMovingPartObject.transform.localRotation;

            Vector3 posOffset = (Quaternion.Inverse(pristineMovingPartObjectRotation) * worldToJointSpace) * ___servoJoint.targetPosition;
            if (!___servoJoint.autoConfigureConnectedAnchor)
                posOffset += ___servoJoint.connectedAnchor;

            node.AddValue("posOffset", posOffset);
        }

        private static void BaseServo_OnLoad_Prefix(BaseServo __instance, ConfigNode node)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            Vector3 posOffset = default;

            if (!node.TryGetValue("posOffset", ref posOffset))
                return;

            if (!robotJointOffsets.TryGetValue(__instance.part.GetInstanceID(), out RobotJointOffset jointOffset))
            {
                jointOffset = new RobotJointOffset();
                robotJointOffsets.Add(__instance.part.GetInstanceID(), jointOffset);
            }

            jointOffset.initialOffset = posOffset;
        }
    }
}
