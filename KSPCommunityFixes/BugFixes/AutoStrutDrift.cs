// See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/21

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class AutoStrutDrift : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), "SecureAutoStrut", new Type[]{typeof(Part), typeof(AttachNode), typeof(AttachNode), typeof(bool)}),
                this));
        }

        static void Part_SecureAutoStrut_Postfix(Part __instance, Part anchor, ref PartJoint __result)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            if (__result.IsNullOrDestroyed() || __result.joints.Count == 0)
                return;

            ConfigurableJoint joint = __result.Joint;

            // We only correct autostruts directly connected to the main rigidbody of the part.
            // Specifically, we don't handle autostruts connected to a BG robotic part.
            // It might be possible to handle that case by getting the offset between the part and the servoRb, not entirely sure.
            if (joint.connectedBody != anchor.rb)
            {
#if DEBUG
                Debug.Log($"[AutoStrutDrift] Skipping autostrut for {__instance}. The joint rb ({joint.connectedBody}) isn't the anchor rb ({anchor.rb}), the anchor is likely a robotics part");
#endif
                return;
            }

            // Get the theoretical position of the strutted to part relative to the current part, in the strutted to part local space
            Vector3 pristinePos = Quaternion.Inverse(anchor.orgRot) * (__instance.orgPos - anchor.orgPos);

            // Set the strutted to part target position
            joint.anchor = new Vector3(0f, 0f, 0f);
            joint.connectedAnchor = pristinePos;

            // Set the joint main axis (X ?) parallel to the strut vector
            // I'm not entirely sure this is correct, as the axis seems to be expressed in the current part local space
            // In any case, this won't make a huge difference
            joint.axis = pristinePos.normalized;

            // Set the joint secondary axis (Y ?)
            joint.secondaryAxis = RandomOrthoVector(joint.axis);

            // Calculate the joint rotation expressed by the joint's axis and secondary axis
            // See https://gist.github.com/mstevenson/4958837
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, joint.axis).normalized;
            Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);

            // Transform into world space
            Quaternion resultRotation = Quaternion.Inverse(worldToJointSpace);

            // Get the in-physics rotation between the current part and the one it is strutted to
            Quaternion physicsRelRot = anchor.transform.rotation.Inverse() * __instance.transform.rotation;

            // Get the theoretical rotation between the current part and the one it is strutted to
            Quaternion pristineRelRot = anchor.orgRot.Inverse() * __instance.orgRot;

            // Counter-rotate to compensate for the difference between the in-physics rotation and the pristine rotation
            resultRotation *= Quaternion.Inverse(pristineRelRot) * physicsRelRot;

            // Transform back into joint space
            resultRotation *= worldToJointSpace;

            // Set the joint rotation offset
            joint.targetRotation = resultRotation;
        }

        private static Vector3 RandomOrthoVector(Vector3 vec)
        {
            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.forward;
            float upDot = Vector3.Dot(vec, up);
            float forwardDot = Vector3.Dot(vec, forward);

            if (Math.Abs(upDot) < Math.Abs(forwardDot))
                return Vector3.Cross(up, vec);
            else
                return Vector3.Cross(forward, vec);
        }
    }
}
