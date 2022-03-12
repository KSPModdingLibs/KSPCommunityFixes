using System;
using Expansions.Serenity;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Expansions;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    public class RoboticsDrift2 : BasePatch
    {
        private static Dictionary<Part, PartPosition> partPositions = new Dictionary<Part, PartPosition>();
        private static Dictionary<Part, ServoInfo> servoInfos = new Dictionary<Part, ServoInfo>();
        private static Dictionary<Vessel, VesselRootServos> vesselServos = new Dictionary<Vessel, VesselRootServos>();

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
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseServo), "RecurseCoordUpdate"),
                this));

            GameEvents.onVesselPartCountChanged.Add(OnVesselPartCountChanged);
            GameEvents.onVesselDestroy.Add(OnVesselDestroy);
            GameEvents.onPartDestroyed.Add(OnPartDestroyed);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneSwitch);
        }

        private void OnVesselPartCountChanged(Vessel vessel)
        {
            if (vesselServos.TryGetValue(vessel, out VesselRootServos rootServos))
                rootServos.FindRootServos(vessel);
        }

        private void OnVesselDestroy(Vessel vessel)
        {
            vesselServos.Remove(vessel);
        }

        private void OnPartDestroyed(Part part)
        {
            partPositions.Remove(part);
            servoInfos.Remove(part);
        }

        private void OnSceneSwitch(GameScenes data)
        {
            partPositions.Clear();
            servoInfos.Clear();
            vesselServos.Clear();
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

                servoInfo = new PositionServoInfo(__instance, ___movingPartObject, movingPartObjectPos);
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

        private static bool BaseServo_RecurseCoordUpdate_Prefix(BaseServo __instance, Part p, ConfigurableJoint ___servoJoint, GameObject ___movingPartObject)
        {
            if (ReferenceEquals(___servoJoint, null) || p == null || ___movingPartObject == null)
                return true;

            if (!vesselServos.TryGetValue(__instance.vessel, out VesselRootServos vesselRootServos))
            {
                vesselRootServos = new VesselRootServos(__instance.vessel);
                vesselServos.Add(__instance.vessel, vesselRootServos);
            }

            vesselRootServos.UpdateServos();

            return false;
        }

        private class VesselRootServos
        {
            public float lastUpdateTime;
            public List<ServoInfo> rootServos = new List<ServoInfo>();

            public VesselRootServos(Vessel vessel)
            {
                FindRootServos(vessel);
            }

            public void UpdateServos()
            {
                if (Time.fixedTime == lastUpdateTime)
                    return;

                lastUpdateTime = Time.fixedTime;

                for (int i = 0; i < rootServos.Count; i++)
                {
                    rootServos[i].RecurseCoordsUpdate();
                }
            }

            public void FindRootServos(Vessel vessel)
            {
                rootServos.Clear();
                FindServoRecursive(vessel.rootPart);
            }

            private void FindServoRecursive(Part part)
            {
                for (int i = 0; i < part.Modules.Count; i++)
                {
                    if (part.Modules[i] is BaseServo)
                    {
                        if (!servoInfos.TryGetValue(part, out ServoInfo servoInfo))
                            Debug.LogError($"[RoboticsDrift] ServoInfo for {part} not found !");
                        else
                            rootServos.Add(servoInfo);

                        return;
                    }
                }

                for (int i = 0; i < part.children.Count; i++)
                {
                    FindServoRecursive(part.children[i]);
                }
            }
        }

        private class PartPosition
        {
            public readonly Vector3 orgPos;
            public readonly Quaternion orgRot;
            public readonly Vector3 parentPosOffset;
            public readonly Quaternion parentRotOffset;
            public readonly ServoInfo servoInfo;

            public PartPosition(Part part, PartPosition parentPartPosition)
            {
                orgPos = part.orgPos;
                orgRot = part.orgRot;
                servoInfos.TryGetValue(part, out servoInfo);

                if (parentPartPosition != null)
                {
                    parentPosOffset = orgPos - parentPartPosition.orgPos;
                    parentRotOffset = parentPartPosition.orgRot.Inverse() * orgRot;
                }
            }
        }

        private abstract class ServoInfo
        {
            protected readonly BaseServo baseServo;
            public GameObject movingPartObject;

            private List<ServoRotation> servoRotations = new List<ServoRotation>();

            protected GameObject PristineMovingPartObject => baseServo.part.partInfo.partPrefab.gameObject.GetChild(baseServo.servoTransformName);

            public ServoInfo(BaseServo baseServo, GameObject movingPartObject)
            {
                this.baseServo = baseServo;
                this.movingPartObject = movingPartObject;
            }

            public abstract void OnSave(ConfigNode node);

            protected abstract void UpdateOffset();

            public void RecurseCoordsUpdate()
            {
                servoRotations.Clear();

                if (!partPositions.TryGetValue(baseServo.part, out PartPosition partPosition))
                {
                    partPosition = new PartPosition(baseServo.part, null);
                    partPositions.Add(baseServo.part, partPosition);
                }

                RecursePristineCoordsUpdate2(baseServo.part, partPosition, Quaternion.identity);
            }

            private struct ServoRotation
            {
                public Vector3 origin;
                public Quaternion rotation;

                public ServoRotation(Vector3 origin, Quaternion rotation)
                {
                    this.origin = origin;
                    this.rotation = rotation;
                }
            }

            private static void RecursePristineCoordsUpdate2(Part parent, PartPosition parentPosition, Quaternion servoRotOffsetTotal)
            {
                Vector3 servoPosOffset = Vector3.zero;
                Quaternion servoRotOffset = Quaternion.identity;

                if (parentPosition.servoInfo != null && parentPosition.servoInfo.baseServo.ServoInitComplete)
                {
                    parentPosition.servoInfo.UpdateOffset();

                    if (parentPosition.servoInfo is PositionServoInfo positionServo)
                    {
                        servoPosOffset = positionServo.currentOffset;
                    }
                    else
                    {
                        // Nope... this rotation is expressed in the parent part local space, and I *think* I need to
                        // convert it back to the vessel / root part local space
                        servoRotOffset = ((RotationServoInfo)parentPosition.servoInfo).currentOffset;
                        //servoRotOffsetTotal = servoRotOffset;
                        //servoRotOffsetTotal *= servoRotOffset;
                    }
                }

                foreach (Part child in parent.children)
                {
                    if (!partPositions.TryGetValue(child, out PartPosition childPosition))
                    {
                        childPosition = new PartPosition(child, parentPosition);
                        partPositions.Add(child, childPosition);
                    }

                    child.orgPos = parent.orgPos + childPosition.parentPosOffset + servoPosOffset; // parent.orgPos + (servoRotOffset * childPosition.parentPosOffset) + servoPosOffset;
                    child.orgRot = parent.orgRot * childPosition.parentRotOffset * servoRotOffset;

                    RecursePristineCoordsUpdate2(child, childPosition, servoRotOffsetTotal);
                }

            }


            //private static void RecursePristineCoordsUpdate(Part part, Vector3 posOffset, Quaternion rotOffset, List<ServoRotation> servoRotations)
            //{
            //    if (!partPositions.TryGetValue(part, out PartPosition partPosition))
            //    {
            //        partPosition = new PartPosition(part);
            //        partPositions.Add(part, partPosition);
            //    }

            //    part.orgPos = partPosition.orgPos + posOffset;
            //    part.orgRot = rotOffset * partPosition.orgRot;

            //    for (int i = 0; i < servoRotations.Count; i++)
            //    {
            //        Vector3 initialPartOffset = partPosition.orgPos + posOffset - servoRotations[i].origin;
            //        Vector3 rotationInducedPosOffset = (servoRotations[i].rotation * initialPartOffset) - initialPartOffset;
            //        part.orgPos += rotationInducedPosOffset;
            //    }

            //    // Ignore the first call when ServoInitComplete == false, as this mean initial MovingPartObject rot/pos
            //    // won't be set because PostStartInitJoint() hasn't yet been called for servos other than the first one.
            //    // That first call to RecurseCoordUpdate() seems useless anyway...
            //    if (partPosition.servoInfo != null && partPosition.servoInfo.baseServo.ServoInitComplete)
            //    {
            //        partPosition.servoInfo.UpdateOffset();

            //        if (partPosition.servoInfo is PositionServoInfo positionServo)
            //        {
            //            posOffset += positionServo.currentOffset;
            //        }
            //        else
            //        {
            //            RotationServoInfo rotationServo = (RotationServoInfo)partPosition.servoInfo;
            //            rotOffset = rotationServo.currentOffset * rotOffset;

            //            servoRotations.Add(new ServoRotation(part.orgPos, rotationServo.currentOffset));
            //        }
            //    }

            //    for (int i = 0; i < part.children.Count; i++)
            //    {
            //        RecursePristineCoordsUpdate(part.children[i], posOffset, rotOffset, servoRotations);
            //    }
            //}
        }

        private class PositionServoInfo : ServoInfo
        {
            public readonly Vector3 initialOffset;
            public Vector3 currentOffset;

            public PositionServoInfo(BaseServo baseServo, GameObject movingPartObject, Vector3 movingPartObjectPosition) : base(baseServo, movingPartObject)
            {
                initialOffset = movingPartObjectPosition;
                //initialOffset = PristineMovingPartObject.transform.localPosition;
                //initialOffset = movingPartObject.transform.localPosition;
            }

            //public PositionServoInfo(BaseServo baseServo, GameObject movingPartObject, ConfigNode node) : base(baseServo, movingPartObject)
            //{
            //    if (!node.TryGetValue("KSPCFOffset", ref initialOffset))
            //        initialOffset = PristineMovingPartObject.transform.localPosition;
            //}

            public override void OnSave(ConfigNode node)
            {
                // node.AddValue("KSPCFOffset", movingPartObject.transform.localPosition - initialOffset);
                node.AddValue("KSPCFOffset", movingPartObject.transform.localPosition);
            }

            protected override void UpdateOffset()
            {
                currentOffset = movingPartObject.transform.localPosition - initialOffset;
            }
        }

        private class RotationServoInfo : ServoInfo
        {
            public readonly Quaternion initialOffset;
            public Quaternion currentOffset;

            public RotationServoInfo(BaseServo baseServo, GameObject movingPartObject, Quaternion movingPartObjectRotation) : base(baseServo, movingPartObject)
            {
                initialOffset = movingPartObjectRotation;
                //initialOffset = PristineMovingPartObject.transform.localRotation;
                //initialOffset = movingPartObject.transform.localRotation;
            }

            //public RotationServoInfo(BaseServo baseServo, GameObject movingPartObject, ConfigNode node) : base(baseServo, movingPartObject)
            //{
            //    if (!node.TryGetValue("KSPCFOffset", ref initialOffset))
            //        initialOffset = PristineMovingPartObject.transform.localRotation;
            //}

            public override void OnSave(ConfigNode node)
            {
                //node.AddValue("KSPCFOffset", initialOffset.Inverse() * movingPartObject.transform.localRotation);
                node.AddValue("KSPCFOffset", movingPartObject.transform.localRotation);
            }

            protected override void UpdateOffset()
            {
                currentOffset = initialOffset.Inverse() * movingPartObject.transform.localRotation;
            }

            //protected override void GetOffset(PartPosition partPosition, ref Vector3 posOffset, ref Quaternion rotOffset)
            //{
            //    Vector3 initialPartOffset = partPosition.orgPos + posOffset - baseServo.part.orgPos;
            //    Vector3 rotationInducedPosOffset = (currentOffset * initialPartOffset) - initialPartOffset;
            //    posOffset += rotationInducedPosOffset;

            //    rotOffset = currentOffset * rotOffset;
            //}
        }


    }
}
