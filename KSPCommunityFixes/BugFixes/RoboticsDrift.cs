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
                Vector3 movingPartObjectPos;
                if (___servoTransformPosLoaded)
                    movingPartObjectPos = __instance.servoTransformPosition;
                else
                    movingPartObjectPos = ___movingPartObject.transform.localPosition;

                servoInfo = new TranslationServoInfo(__instance, ___movingPartObject, movingPartObjectPos);
            }
            else
            {
                Quaternion movingPartObjectRot;
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
            protected bool isInverted;
            private bool isInitialized;

            public ServoInfo(BaseServo baseServo, GameObject movingPartObject)
            {
                this.baseServo = baseServo;
                this.movingPartObject = movingPartObject;
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

            protected Quaternion GetLocalToVesselSpace()
            {
                // for some reason not normalizing can end up with a quaternion with near infinity components 
                // when it should be identity, leading to infinity and NaN down the line...
                return (baseServo.part.orgRot.Inverse() * baseServo.part.vessel.rootPart.orgRot).normalized;
            }
        }

        private class TranslationServoInfo : ServoInfo
        {
            private Vector3 lastLocalPosition;
            private Vector3 posOffset;

            public TranslationServoInfo(BaseServo baseServo, GameObject movingPartObject, Vector3 movingPartObjectPosition) : base(baseServo, movingPartObject)
            {
                lastLocalPosition = movingPartObjectPosition;
            }

            protected override void UpdateOffset()
            {
                Quaternion localToVesselSpace = GetLocalToVesselSpace();

                // get translation offset of the moving part since last update, and transform from the servo local space to the vessel space
                posOffset = localToVesselSpace.Inverse() * (movingPartObject.transform.localPosition - lastLocalPosition);

                // save the moving part position
                lastLocalPosition = movingPartObject.transform.localPosition;

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
            private Quaternion lastLocalRotation;
            private Quaternion rotOffset;

            public RotationServoInfo(BaseServo baseServo, GameObject movingPartObject, Quaternion movingPartObjectRotation) : base(baseServo, movingPartObject)
            {
                lastLocalRotation = movingPartObjectRotation;
            }

            protected override void UpdateOffset()
            {
                Quaternion localToVesselSpace = GetLocalToVesselSpace();

                // get rotation offset of the moving part since last update
                rotOffset = lastLocalRotation.Inverse() * movingPartObject.transform.localRotation;

                // transform offset from the servo local space to the vessel space
                rotOffset = localToVesselSpace.Inverse() * rotOffset * localToVesselSpace;

                // save the moving part rotation
                lastLocalRotation = movingPartObject.transform.localRotation;

                // if servo is attached to its parent by its moving part, we need to :
                // - invert the offset
                // - rotate the servo part itself
                if (isInverted)
                {
                    rotOffset = rotOffset.Inverse();
                    baseServo.part.orgRot = rotOffset * baseServo.part.orgRot;
                }
            }

            protected override void RecurseChildCoordsUpdate(Part part)
            {
                RecurseChildCoordsUpdate(part, Vector3.zero);
            }

            private void RecurseChildCoordsUpdate(Part part, Vector3 posOffset)
            {
                // get position offset between this part and its parent
                Vector3 orgPosOffset = part.orgPos - part.parent.orgPos;
                // add the upstream position offset (from all parents), then rotate the result
                orgPosOffset = rotOffset * (orgPosOffset + posOffset);
                // get the new position for this part
                Vector3 newOrgPos = part.parent.orgPos + orgPosOffset;
                // update the total offset that will be applied downstream to childrens
                posOffset = newOrgPos - part.orgPos;
                // apply the new position
                part.orgPos = newOrgPos;

                // apply the rotation
                part.orgRot = rotOffset * part.orgRot;

                // propagate to childrens
                for (int i = 0; i < part.children.Count; i++)
                    RecurseChildCoordsUpdate(part.children[i], posOffset);
            }
        }
    }
}
