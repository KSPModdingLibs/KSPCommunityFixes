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
            // ignore our custome logic when :
            // - in the editor => ReferenceEquals(___servoJoint, null)
            // - called from OnStart() => ___movingPartObject == null
            // - called from OnStartBeforePartAttachJoint() => p.attachJoint == null
            if (ReferenceEquals(___servoJoint, null) || p == null || ___movingPartObject == null || p.attachJoint == null)
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
            protected readonly GameObject movingPartObject;
            public readonly Quaternion localToVesselSpace;

            protected GameObject PristineMovingPartObject => baseServo.part.partInfo.partPrefab.gameObject.GetChild(baseServo.servoTransformName);

            public ServoInfo(BaseServo baseServo, GameObject movingPartObject)
            {
                this.baseServo = baseServo;
                this.movingPartObject = movingPartObject;
                localToVesselSpace = baseServo.part.orgRot.Inverse() * baseServo.part.vessel.rootPart.orgRot;
            }

            protected abstract void UpdateOffset();

            public void RecurseCoordsUpdate()
            {
                if (!partPositions.TryGetValue(baseServo.part, out PartPosition partPosition))
                {
                    // we need the parent part position as well
                    PartPosition parentPartPosition = new PartPosition(baseServo.part.parent, null);
                    partPositions.Add(baseServo.part.parent, parentPartPosition);

                    partPosition = new PartPosition(baseServo.part, parentPartPosition);
                    partPositions.Add(baseServo.part, partPosition);
                }

                RecursePristineCoordsUpdate2(baseServo.part, partPosition, Vector3.zero, Quaternion.identity, Quaternion.identity);
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

            private enum ServoType { None, Translation, Rotation}

            // TODO: Currently things only work if you put the "child" parts on the right side of the servo part
            // (ie, if the child parts are attached to the MovingPartObject). When the child(s) is attached to the
            // base part :
            // - For linear servos : the servo itself must be translated (instead of translating the childs)
            // - For rotation servos : servoRotOffset must be inverted
            // + some childs might actually not attached to the moving part
            // Test the configurablejoint.connectedbody ?
            private static void RecursePristineCoordsUpdate2(Part parent, PartPosition parentPosition, Vector3 parentPosOffset, Quaternion parentRotOffset, Quaternion servoRotOffsetTotal)
            {
                Vector3 servoPosOffset = Vector3.zero;
                Quaternion servoRotOffset = Quaternion.identity;
                ServoType servoType = ServoType.None;
                bool servoIsInverted = false;

                if (parentPosition.servoInfo != null) // && parentPosition.servoInfo.baseServo.ServoInitComplete
                {
                    parentPosition.servoInfo.UpdateOffset();

                    if (parentPosition.servoInfo is PositionServoInfo positionServo)
                    {
                        servoType = ServoType.Translation;
                        servoPosOffset = positionServo.currentOffset;
                    }
                    else
                    {
                        servoType = ServoType.Rotation;
                        servoRotOffset = ((RotationServoInfo)parentPosition.servoInfo).currentOffset;
                        // Transform the rotation in vessel space
                        servoRotOffsetTotal *= parentPosition.servoInfo.localToVesselSpace.Inverse() * servoRotOffset * parentPosition.servoInfo.localToVesselSpace;
                    }

                    // is servo attached to its parent by its moving part ?
                    if (parent.attachJoint.Joint.gameObject == parentPosition.servoInfo.movingPartObject)
                    {
                        servoIsInverted = true;
                        servoPosOffset = -servoPosOffset + parentPosOffset;
                        servoRotOffset = servoRotOffset.Inverse() * parentRotOffset;

                        // if translation, we need to move the servo part instead of the child parts
                        Part grandParent = parent.parent;

                        if (!partPositions.TryGetValue(grandParent, out PartPosition grandParentPosition))
                        {
                            Debug.LogWarning($"[RoboticsDrift] Querying grandparent part position for servo {parent} during RecursePristineCoordsUpdate, this shouldn't be happening...");
                            grandParentPosition = new PartPosition(grandParent, null);
                            partPositions.Add(grandParent, grandParentPosition);
                        }

                        parent.orgPos = grandParent.orgPos + (servoRotOffsetTotal * (parentPosition.parentPosOffset + servoPosOffset));
                        parent.orgRot = grandParent.orgRot * parentPosition.parentRotOffset * servoRotOffset;

                        servoPosOffset = Vector3.zero;
                        servoRotOffset = Quaternion.identity;

                        //foreach (Part child in parent.children)
                        //{
                        //    // ignore childs connected to the moving part of the parent servo
                        //    if (child.attachJoint.Joint.connectedBody.gameObject == parentPosition.servoInfo.movingPartObject)
                        //        continue;

                        //    if (!partPositions.TryGetValue(child, out PartPosition childPosition))
                        //    {
                        //        childPosition = new PartPosition(child, parentPosition);
                        //        partPositions.Add(child, childPosition);
                        //    }

                        //    RecursePristineCoordsUpdate2(child, childPosition, servoRotOffsetTotal);
                        //}

                        //return;
                    }
                }

                foreach (Part child in parent.children)
                {
                    // ignore childs connected to the non-moving part of the parent servo
                    if (servoType != ServoType.None)
                    {
                        if (child.attachJoint.Joint.connectedBody.gameObject == parentPosition.servoInfo.movingPartObject)
                        {
                            if (servoIsInverted)
                            {
                                continue;
                            }
                        }
                        else if (!servoIsInverted)
                        {
                            continue;
                        }
                    }
                    
                    if (!partPositions.TryGetValue(child, out PartPosition childPosition))
                    {
                        childPosition = new PartPosition(child, parentPosition);
                        partPositions.Add(child, childPosition);
                    }

                    child.orgPos = parent.orgPos + (servoRotOffsetTotal * (childPosition.parentPosOffset + servoPosOffset));
                    child.orgRot = parent.orgRot * childPosition.parentRotOffset * servoRotOffset;

                    RecursePristineCoordsUpdate2(child, childPosition, servoPosOffset, servoRotOffset, servoRotOffsetTotal);
                }

            }
        }

        private class PositionServoInfo : ServoInfo
        {
            public readonly Vector3 initialOffset;
            public Vector3 currentOffset;

            public PositionServoInfo(BaseServo baseServo, GameObject movingPartObject, Vector3 movingPartObjectPosition) : base(baseServo, movingPartObject)
            {
                initialOffset = movingPartObjectPosition;
            }

            protected override void UpdateOffset()
            {
                currentOffset = localToVesselSpace.Inverse() * (movingPartObject.transform.localPosition - initialOffset);
            }
        }

        private class RotationServoInfo : ServoInfo
        {
            public readonly Quaternion initialOffset;
            public Quaternion currentOffset;

            public RotationServoInfo(BaseServo baseServo, GameObject movingPartObject, Quaternion movingPartObjectRotation) : base(baseServo, movingPartObject)
            {
                initialOffset = movingPartObjectRotation;
            }

            protected override void UpdateOffset()
            {
                currentOffset = initialOffset.Inverse() * movingPartObject.transform.localRotation;
            }
        }


    }
}
