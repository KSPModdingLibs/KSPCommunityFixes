/*
See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/166
This fixes a set of bugs and oversights resulting in the the lack of proper vessel rotation tracking :
- Vessels changing SOIs while unloaded have their rotation altered randomly
- The rotation of all vessels is "drifting" when inverse rotation is engaged
  That behavior can be verifed for example by creating a maneuver node and aligning the vessel with 
  the maneuver, then timewarping for a bit while being under the inverse rotation altitude threshold.
  The vessel will slowly drift away from the maneuver marker, at the same speed as the main body rotation.

This is an exploratory fix, which works and is probably safe from a modding ecosystem perspective,
but introduce a non-negligible performance impact, between 0.5% and 2.5% of the frame time, the bulk of
which is directly proportional to the loaded part count. The overhead on loaded and unpacked vessels
feels the most problematic and can be avoided by simply not correcting in that case, which in practice
is unlikely to be noticeable, as the player is usually actively correcting the attitude, and the rotation
drift is too slow to be noticeable.
*/

// uncomment the following to activate inverse rotation drift correction for unpacked vessels :
// #define UNPACKED_VESSELS_CORRECTION

// uncomment the following to activate inverse rotation drift correction for physical objects
// (jettisoned fairings and interstages...) :
// #define PHYSICAL_OBJECTS_CORRECTION

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Profiling;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    public class ApplyInverseRotationToVessels : BasePatch
    {
        private const string ZUPROT_VALUE_NAME = "zupRot";
        private static QuaternionD planetariumRotation;

        private static ProfilerMarker profiler = new ProfilerMarker("KSPCF:FixVesselsRotation");

        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            // save planetarium rotation 
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Vessel), nameof(Vessel.OnSaveFlightState)),
                this));

            // restore planetarium rotation 
            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(ProtoVessel), nameof(ProtoVessel.Load), new[] { typeof(FlightState), typeof(Vessel) }),
                this));

            // get current planetarium rotation
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Planetarium), nameof(Planetarium.FixedUpdate)),
                this));

            // apply inverse rotation to all vessels
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Planetarium), nameof(Planetarium.FixedUpdate)),
                this));
        }

        static void Vessel_OnSaveFlightState_Postfix(Vessel __instance, Dictionary<string, KSPParseable> dataPool)
        {
            // convert rotation from Unity world space to planetarium ZUP space
            Quaternion vesselRot = (Quaternion)Planetarium.Zup.Rotation * __instance.transform.rotation.Swizzle();
            dataPool[ZUPROT_VALUE_NAME] = new KSPParseable(vesselRot.normalized, KSPParseable.Type.QUATERNION);
        }

        static Quaternion GetVesselRotation(ProtoVessel protoVessel)
        {
            // if the value doesn't exists, fallback to the stock main-body surface relative rotation
            if (!protoVessel.vesselStateValues.TryGetValue(ZUPROT_VALUE_NAME, out KSPParseable value))
                return protoVessel.vesselRef.orbit.referenceBody.bodyTransform.rotation * protoVessel.rotation;

            Quaternion zupRot = value.value_quat;

            // convert rotation from planetarium ZUP space to Unity world space
            return (Quaternion.Inverse(Planetarium.Zup.Rotation) * zupRot).Swizzle().normalized;
        }

        // replace the line :
        // vesselRef.transform.rotation = vesselRef.orbit.referenceBody.bodyTransform.rotation * rotation;
        // by :
        // vesselRef.transform.rotation = ApplyInverseRotationToVessels.GetVesselRotation(this);
        static IEnumerable<CodeInstruction> ProtoVessel_Load_Transpiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo m_GetVesselRotation = AccessTools.Method(typeof(ApplyInverseRotationToVessels), nameof(GetVesselRotation));
            MethodInfo m_SetRotation = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.rotation));
            FieldInfo f_vesselRef = AccessTools.Field(typeof(ProtoVessel), nameof(ProtoVessel.vesselRef));

            List<CodeInstruction> ilList = new List<CodeInstruction>(code);

            int end;
            for (end = 0; end < ilList.Count; end++)
                if (ilList[end].opcode == OpCodes.Callvirt && ReferenceEquals(ilList[end].operand, m_SetRotation))
                    break;

            int start;
            for (start = end; start >= 0; start--)
                if (ilList[start].opcode == OpCodes.Ldfld && ReferenceEquals(ilList[start].operand, f_vesselRef))
                    break;

            ilList[start] = new CodeInstruction(OpCodes.Call, m_GetVesselRotation);

            for (int i = start + 1; i < end; i++)
            {
                ilList[i].opcode = OpCodes.Nop;
                ilList[i].operand = null;
            }

            return ilList;
        }

        // save the planetarium rotation before inverse rotation is applied
        static void Planetarium_FixedUpdate_Prefix(Planetarium __instance)
        {
            planetariumRotation = __instance.rotation;
        }

        // if there is an offset between the last planetarium rotation and now,
        // apply it to all vessels and physical objects
        static void Planetarium_FixedUpdate_Postfix(Planetarium __instance)
        {
            if (!FlightGlobals.ready || !FlightGlobals.RefFrameIsRotating || __instance.rotation == planetariumRotation)
                return;

            profiler.Begin();

            Quaternion offset = QuaternionD.Inverse(planetariumRotation) * __instance.rotation;

#if PHYSICAL_OBJECTS_CORRECTION
            for (int i = FlightGlobals.physicalObjects.Count; i-- > 0;)
            {
                physicalObject physicalObject = FlightGlobals.physicalObjects[i];
                if (physicalObject.IsNullOrDestroyed()) 
                    continue;

                Transform physicalObjectTransform = physicalObject.transform;
                physicalObjectTransform.rotation = offset * physicalObjectTransform.rotation;
            }
#endif
            for (int i = FlightGlobals.VesselsUnloaded.Count; i-- > 0;)
            {
                Vessel vessel = FlightGlobals.VesselsUnloaded[i];

                if (vessel.IsNullOrDestroyed() || vessel.Landed || vessel.Splashed)
                    continue;

                Transform vesselTransform = vessel.vesselTransform;
                vesselTransform.rotation = offset * vesselTransform.rotation;
            }

            for (int i = FlightGlobals.VesselsLoaded.Count; i-- > 0;)
            {
                Vessel vessel = FlightGlobals.VesselsLoaded[i];

                if (vessel.IsNullOrDestroyed() || vessel.Landed || vessel.Splashed)
                    continue;

                List<Part> parts = vessel.parts;
                Vector3 vesselPos = vessel.vesselTransform.position;

                if (vessel.packed)
                {
                    Quaternion vesselRot = (offset * vessel.vesselTransform.rotation).normalized;

                    for (int j = parts.Count; j-- > 0;)
                    {
                        Part part = parts[j];

                        // don't move physicless parts (they are childs of other parts and will be moved by their local transform)
                        if (part.physicalSignificance == Part.PhysicalSignificance.NONE)
                            continue;

                        part.partTransform.SetPositionAndRotation(
                            vesselPos + vesselRot * part.orgPos,
                            vesselRot * part.orgRot);
                    }
                }
#if UNPACKED_VESSELS_CORRECTION
                else
                {
                    for (int j = parts.Count; j-- > 0;)
                    {
                        Part part = parts[j];

                        // don't move physicless parts (they are childs of other parts and will be moved by their local transform)
                        if (part.physicalSignificance == Part.PhysicalSignificance.NONE)
                            continue;

                        Transform partTransform = part.partTransform;
                        partTransform.SetPositionAndRotation(
                            vesselPos + offset * (partTransform.position - vesselPos),
                            offset * partTransform.rotation);
                    }
                }
#endif
            }

            profiler.End();
        }
    }
}
