using System;
using Expansions;
using Expansions.Serenity;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    public class RoboticsDrift : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override bool CanApplyPatch(out string reason)
        {
            if (!Directory.Exists(Path.Combine(KSPExpansionsUtils.ExpansionsGameDataPath, "Serenity")))
            {
                reason = "Breaking Grounds DLC not installed";
                return false;
            }

            return base.CanApplyPatch(out reason);
        }

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.OnStart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(BaseServo), "OnDestroy"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseServo), "RecurseCoordUpdate"),
                this));

            GameEvents.onGameSceneLoadRequested.Add(OnSceneSwitch);
        }

        private static readonly Dictionary<Part, ServoInfo> servoInfos = new Dictionary<Part, ServoInfo>();

        private void OnSceneSwitch(GameScenes data)
        {
            servoInfos.Clear();
        }

        private static void BaseServo_OnStart_Postfix(BaseServo __instance, GameObject ___movingPartObject, bool ___servoTransformPosLoaded, bool ___servoTransformRotLoaded)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            ServoInfo servoInfo;
            if (__instance is ModuleRoboticServoPiston)
            {
                Vector3d movingPartObjectPos;
                if (___servoTransformPosLoaded)
                    movingPartObjectPos = __instance.servoTransformPosition;
                else
                    movingPartObjectPos = ___movingPartObject.transform.localPosition;

                servoInfo = new TranslationServoInfo(__instance, ___movingPartObject, movingPartObjectPos);
            }
            else
            {
                QuaternionD movingPartObjectRot;
                if (___servoTransformRotLoaded)
                    movingPartObjectRot = __instance.servoTransformRotation;
                else
                    movingPartObjectRot = ___movingPartObject.transform.localRotation;

                servoInfo = new RotationServoInfo(__instance, ___movingPartObject, movingPartObjectRot);
            }

            servoInfos.Add(__instance.part, servoInfo);
        }

        private static void BaseServo_OnDestroy_Postfix(BaseServo __instance)
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                servoInfos.Remove(__instance.part);
        }

        private static bool BaseServo_RecurseCoordUpdate_Prefix(BaseServo __instance, Part p, ConfigurableJoint ___servoJoint, GameObject ___movingPartObject)
        {
            // ignore our custome logic when :
            // - in the editor => ReferenceEquals(___servoJoint, null)
            // - called from OnStart() => ___movingPartObject == null
            // - called from OnStartBeforePartAttachJoint() => p.attachJoint == null
            if (ReferenceEquals(___servoJoint, null) || p == null || ___movingPartObject == null || p.attachJoint == null)
                return true;

            if (!servoInfos.TryGetValue(p, out ServoInfo servoInfo))
            {
                Debug.LogWarning($"[RoboticsDrift] Servo info not found for {__instance.GetType()} on {p}, drift correction won't be applied !");
                return true;
            }

            servoInfo.PristineCoordsUpdate();

            return false;
        }

        private abstract class ServoInfo
        {
            protected readonly BaseServo baseServo;
            protected readonly GameObject movingPartObject;
            protected readonly Vector3d mainAxis;
            protected bool isInverted;
            private bool isInitialized;

            public ServoInfo(BaseServo baseServo, GameObject movingPartObject)
            {
                this.baseServo = baseServo;
                this.movingPartObject = movingPartObject;
                mainAxis = baseServo.GetMainAxis();
                isInitialized = false;
            }

            public void PristineCoordsUpdate()
            {
                if (!isInitialized)
                {
                    isInverted = baseServo.part.attachJoint.Joint.gameObject == movingPartObject;
                    isInitialized = true;
                }

                UpdateOffset();

                Part p = baseServo.part;
                for (int i = 0; i < p.children.Count; i++)
                {
                    // Dont move the child if :
                    // - child is attached the servo moving part, and servo is attached to its parent by its moving part
                    // - child is attached to the servo non-moving part, and servo is attached to its parent by its non-moving part
                    if (p.children[i].attachJoint.Joint.connectedBody.gameObject == movingPartObject)
                    {
                        if (isInverted)
                            continue;
                    }
                    else if (!isInverted)
                    {
                        continue;
                    }

                    RecurseChildCoordsUpdate(p.children[i]);
                }
            }

            protected abstract void UpdateOffset();

            protected abstract void RecurseChildCoordsUpdate(Part part);

            protected QuaternionD GetLocalToVesselSpace()
            {
                // for some reason not normalizing can end up with a quaternion with near infinity components 
                // when it should be identity, leading to infinity and NaN down the line...
                return (QuaternionD.Inverse(baseServo.part.orgRot) * (QuaternionD)baseServo.part.vessel.rootPart.orgRot).Normalize();
            }
        }

        private class TranslationServoInfo : ServoInfo
        {
            private Vector3d lastLocalOffset;
            private Vector3d posOffset;

            public TranslationServoInfo(BaseServo baseServo, GameObject movingPartObject, Vector3d movingPartObjectPosition) : base(baseServo, movingPartObject)
            {
                lastLocalOffset = mainAxis * movingPartObjectPosition.magnitude;
            }

            protected override void UpdateOffset()
            {
                Quaternion localToVesselSpace = GetLocalToVesselSpace();

                Vector3d localOffset = mainAxis * movingPartObject.transform.localPosition.magnitude;

                // get translation offset of the moving part since last update, and transform from the servo local space to the vessel space
                posOffset = localToVesselSpace.Inverse() * (localOffset - lastLocalOffset);

                // save the moving part position
                lastLocalOffset = localOffset;

                // if servo is attached to its parent by its moving part, we need to :
                // - invert the offset
                // - translate the servo part itself
                if (isInverted)
                {
                    posOffset = -posOffset;
                    baseServo.part.orgPos += posOffset;
                }
            }

            protected override void RecurseChildCoordsUpdate(Part part)
            {
                // apply the offset to the orignial position
                part.orgPos += posOffset;

                // propagate to childrens
                for (int i = 0; i < part.children.Count; i++)
                    RecurseChildCoordsUpdate(part.children[i]);
            }
        }

        private class RotationServoInfo : ServoInfo
        {
            private QuaternionD lastLocalRotation;
            private QuaternionD rotOffset;
            private readonly int mainAxisAxisIndex;
            private readonly int mainAxisAxisSign;

            public RotationServoInfo(BaseServo baseServo, GameObject movingPartObject, QuaternionD movingPartObjectRotation) : base(baseServo, movingPartObject)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (mainAxis[i] != 0.0)
                    {
                        mainAxisAxisIndex = i;
                        mainAxisAxisSign = Math.Sign(mainAxis[i]);
                        break;
                    }
                }

                lastLocalRotation = GetLocalOffset(movingPartObjectRotation);
            }

            protected override void UpdateOffset()
            {
                QuaternionD localToVesselSpace = GetLocalToVesselSpace();

                QuaternionD localOffset = GetLocalOffset(movingPartObject.transform.localRotation);

                // get rotation offset of the moving part since last update
                rotOffset = QuaternionD.Inverse(lastLocalRotation) * localOffset;

                // transform offset from the servo local space to the vessel space
                rotOffset = QuaternionD.Inverse(localToVesselSpace) * rotOffset * localToVesselSpace;

                // save the moving part rotation
                lastLocalRotation = localOffset;

                // if servo is attached to its parent by its moving part, we need to :
                // - invert the offset
                // - rotate the servo part itself
                if (isInverted)
                {
                    rotOffset = QuaternionD.Inverse(rotOffset);
                    baseServo.part.orgRot = rotOffset * (QuaternionD)baseServo.part.orgRot;
                }
            }

            private QuaternionD GetLocalOffset(QuaternionD movingPartObjectRotation)
            {
                QuaternionExtensions.ToAngleAxis(movingPartObjectRotation, out Vector3d axis, out double angle);
#if DEBUG
                Quaternion qf = movingPartObjectRotation;
                qf.ToAngleAxis(out float angleF, out Vector3 axisF);

                if (angleF != (float)angle || axisF != (Vector3)axis)
                {
                    Debug.LogWarning($"[RoboticsDrift] Angle={angle:R}(D)/{angleF:R}(F) - Axis={axis:R}(D)/{axisF:R}(F)");
                }
#endif

                if (Math.Sign(axis[mainAxisAxisIndex]) != mainAxisAxisSign)
                    angle *= -1.0;

                return QuaternionD.AngleAxis(angle, mainAxis);
            }

            protected override void RecurseChildCoordsUpdate(Part part)
            {
                RecurseChildCoordsUpdate(part, Vector3d.zero);
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
        }
    }

    public static class QuaternionExtensions
    {
        private const double Rad2Deg = 180.0 / Math.PI;

        public static QuaternionD Normalize(this QuaternionD q)
        {
            double ls = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            double invNorm = 1.0 / Math.Sqrt(ls);
            return new QuaternionD(q.x * invNorm, q.y * invNorm, q.z * invNorm, q.w * invNorm);
        }

        public static void ToAngleAxis(this QuaternionD q, out Vector3d axis, out double angle)
        {
            if (Math.Abs(q.w) > 1.0)
                q = q.Normalize();

            if (Math.Abs(q.w) < 0.99)
            {
                angle = 2.0 * Math.Acos(q.w) * Rad2Deg;
                if (angle == 0.0)
                {
                    axis = Vector3d.up;
                }
                else
                {
                    double invDen = 1.0 / Math.Sqrt(1.0 - q.w * q.w);
                    axis = new Vector3d(q.x * invDen, q.y * invDen, q.z * invDen);
                }
            }
            else
            {
                axis = new Vector3d(q.x, q.y, q.z);
                double len = axis.magnitude;
                if (len == 0.0)
                {
                    angle = 0.0;
                    axis = Vector3d.up;
                }
                else
                {
                    angle = 2.0 * Math.Asin(len) * Math.Sign(q.w) * Rad2Deg;
                    axis *= 1.0 / len;
                }
            }
        }
    }
}
