// Enable cross checking our implementations results with the stock implementations results
// Warning : very log-spammy and performance destroying, don't leave this enabled if you don't need to.
// #define DEBUG_FLIGHTINTEGRATOR

using HarmonyLib;
using KSPCommunityFixes.Library;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.Performance
{
    internal class FlightPerf : BasePatch
    {
        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusionSolar)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusionBody)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateMassStats)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(VesselPrecalculate), nameof(VesselPrecalculate.CalculatePhysicsStats)),
                this));

            // other offenders, in aero situations :

            // AddSurfaceDragDirection : 7%
            // - could turn the curves (1.6%) into lookup tables
            // - general float to double pass, not sure how practical due to working a lot with float-backed drag cubes, relevant at least for the Mathf.Pow() call
            // - multiple InverseTransform calls, use the matrix instead

            // general optimization pass on UpdateOcclusionConvection() : 4.4%

            // In general, Integrate() should benefit from being made a non recursive loop, and that might open a few optimizations
            // by moving some expensive matrix / quaternion stuff outside that loop.

            // In a similar vein, there are a few thing to be done about FloatingOrigin and more specifically Vessel.SetPosition (at least, pre-computing the quaternion rotation)
        }

        #region VesselPrecalculate.CalculatePhysicsStats optimizations

        /// <summary>
        /// 40-60% faster than the stock method depending on the situation.
        /// Hard to optimize further, a large chunk of the time is spent getting transform / rb properties (~40%)
        /// and performing unavoidable double/float conversions (~10%).
        /// </summary>
        private static bool VesselPrecalculate_CalculatePhysicsStats_Prefix(VesselPrecalculate __instance)
        {
            Vessel vessel = __instance.vessel;
            bool isMasslessOrNotLoaded = true;

            if (vessel.loaded)
            {
                int partCount = vessel.Parts.Count;
                Transform vesselTransform = vessel.ReferenceTransform;
                Matrix4x4 vesselInverseMatrixF = vesselTransform.worldToLocalMatrix;
                Matrix4x4D vesselInverseMatrix = vesselInverseMatrixF.ToMatrix4x4D();
                QuaternionD vesselInverseRotation = QuaternionD.Inverse(vesselTransform.rotation);
                Vector3d com = Vector3d.zero;
                Vector3d velocity = Vector3d.zero;
                Vector3d angularVelocity = Vector3d.zero;
                double vesselMass = 0.0;

                if (vessel.packed && partCount > 0)
                    Physics.SyncTransforms();

                VesselPrePartBufferEnsureCapacity(partCount);
                int index = partCount;
                int rbPartCount = 0;
                while (index-- > 0)
                {
                    Part part = vessel.parts[index];
                    if (part.rb.IsNotNullOrDestroyed())
                    {
                        Vector3d partPosition = part.partTransform.position;
                        QuaternionD partRotation = part.partTransform.rotation;
                        vesselPrePartBuffer[rbPartCount] = new PartVesselPreData(partPosition, partRotation, index);
                        rbPartCount++;
#if DEBUG_FLIGHTINTEGRATOR
                        double deviation = ((partPosition + partRotation * part.CoMOffset) - part.rb.worldCenterOfMass).magnitude;
                        if (deviation > 0.001)
                            Debug.LogWarning($"[KSPCF:FIPerf] KSPCF calculated WorldCenterOfMass is deviating from stock by {deviation:F3}m for part {part.partInfo.title} on vessel {vessel.GetDisplayName()}");
#endif
                        double physicsMass = part.physicsMass;
                        // note : on flight scene load, the parts RBs center of mass won't be set until the vessel gets out of the packed
                        // state (see FI.UpdateMassStats()), it will initially be set to whatever PhysX has computed from the RB colliders.
                        // This result in an inconsistent vessel CoM (and all derived stats) being computed for several frames. 
                        // For performance reasons we don't use rb.worldCenterOfMass, but instead re-compute it from Part.CoMOffset, but
                        // this also has the side effect of fixing those inconsistencies.
                        com.Add((partPosition + partRotation * part.CoMOffset) * physicsMass);
                        velocity.Add((Vector3d)part.rb.velocity * physicsMass);
                        angularVelocity.Add(vesselInverseRotation * part.rb.angularVelocity * physicsMass);
                        vesselMass += physicsMass;
                    }
                }

                if (vesselMass > 0.0)
                {
                    isMasslessOrNotLoaded = false;
                    vessel.totalMass = vesselMass;
                    double vesselMassRecip = 1.0 / vesselMass;
                    vessel.CoMD = com * vesselMassRecip;
                    vessel.rb_velocityD = velocity * vesselMassRecip;
                    vessel.velocityD = vessel.rb_velocityD + Krakensbane.GetFrameVelocity();
                    vessel.CoM = vessel.CoMD;
                    vessel.localCoM = vessel.vesselTransform.InverseTransformPoint(vessel.CoM);
                    vessel.rb_velocity = vessel.rb_velocityD;
                    vessel.angularVelocityD = angularVelocity * vesselMassRecip;
                    vessel.angularVelocity = vessel.angularVelocityD;

                    if (vessel.angularVelocityD == Vector3d.zero && vessel.packed)
                    {
                        vessel.MOI.Zero();
                        vessel.angularMomentum.Zero();
                    }
                    else
                    {
                        InertiaTensor inertiaTensor = new InertiaTensor();
                        for (int i = 0; i < rbPartCount; i++)
                        {
                            PartVesselPreData partPreData = vesselPrePartBuffer[i];
                            Part part = vessel.parts[partPreData.partIndex];

                            // add part inertia tensor to vessel inertia tensor
                            Vector3d principalMoments = part.rb.inertiaTensor;
                            QuaternionD princAxesRot = vesselInverseRotation * partPreData.rotation * (QuaternionD)part.rb.inertiaTensorRotation;
                            inertiaTensor.AddPartInertiaTensor(principalMoments, princAxesRot);

                            // add part mass and position contribution to vessel inertia tensor
                            double rbMass = Math.Max(part.partInfo.minimumRBMass, part.physicsMass);
                            // Note : the stock MoI code fails to account for the additional RB of servo parts.
                            // On servo parts, the part physicsMass is redistributed equally between the part RB and the servo RB, and since when
                            // computing the MoI, the stock code uses only the rb.mass, some mass will be unacounted for. Ideally we should do
                            // the full additional MoI calcs with the servo RB, but as a shorthand fix we just include the whole physicsMass instead
                            // of half of it like what stock would do. If we want to replicate exactely stock, uncomment those :
                            // if (part.servoRb.IsNotNullRef())
                            //     rbMass *= 0.5;
                            // Note 2 : another side effect of using Part.physicsMass instead of rb.mass is that mass will be correct on scene
                            // loads, before FI.UpdateMassStats() has run (when it hasn't run yet, rb.mass is set to 1 for all parts)
                            Vector3d partPosition = vesselInverseMatrix.MultiplyVector(partPreData.position - vessel.CoMD);
                            inertiaTensor.AddPartMass(rbMass, partPosition);
                        }

                        vessel.MOI = inertiaTensor.MoI;
                        vessel.angularMomentum.x = (float)(inertiaTensor.m00 * vessel.angularVelocityD.x);
                        vessel.angularMomentum.y = (float)(inertiaTensor.m11 * vessel.angularVelocityD.y);
                        vessel.angularMomentum.z = (float)(inertiaTensor.m22 * vessel.angularVelocityD.z);
                    }
                }

#if DEBUG_FLIGHTINTEGRATOR
                VerifyPhysicsStats(__instance);
#endif
            }

            if (isMasslessOrNotLoaded)
            {
                if (vessel.packed)
                {
                    if (vessel.LandedOrSplashed)
                    {
                        vessel.CoMD = __instance.worldSurfacePos + __instance.worldSurfaceRot * vessel.localCoM;
                    }
                    else
                    {
                        if (!vessel.orbitDriver.Ready)
                        {
                            vessel.orbitDriver.orbit.Init();
                            vessel.orbitDriver.updateFromParameters(setPosition: false);
                        }
                        vessel.CoMD = vessel.mainBody.position + vessel.orbitDriver.pos;
                    }
                }
                else
                {
                    vessel.CoMD = vessel.vesselTransform.TransformPoint(vessel.localCoM);
                }

                vessel.CoM = vessel.CoMD;

                if (vessel.rootPart.IsNotNullOrDestroyed() && vessel.rootPart.rb.IsNotNullOrDestroyed())
                {
                    vessel.rb_velocity = vessel.rootPart.rb.GetPointVelocity(vessel.CoM);
                    vessel.rb_velocityD = vessel.rb_velocity;
                    vessel.velocityD = (Vector3d)vessel.rb_velocity + Krakensbane.GetFrameVelocity();
                    vessel.angularVelocityD = (vessel.angularVelocity = Quaternion.Inverse(vessel.ReferenceTransform.rotation) * vessel.rootPart.rb.angularVelocity);
                }
                else
                {
                    vessel.rb_velocity.Zero();
                    vessel.rb_velocityD.Zero();
                    vessel.velocityD.Zero();
                    vessel.angularVelocity.Zero();
                    vessel.angularVelocityD.Zero();
                }
                vessel.MOI.Zero();
                vessel.angularMomentum.Zero();
            }
            __instance.firstStatsRunComplete = true;
            return false;
        }

        private readonly struct PartVesselPreData
        {
            public readonly int partIndex;
            public readonly Vector3d position;
            public readonly QuaternionD rotation;

            public PartVesselPreData(Vector3d position, QuaternionD rotation, int partIndex)
            {
                this.position = position;
                this.rotation = rotation;
                this.partIndex = partIndex;
            }
        }

        private static void VesselPrePartBufferEnsureCapacity(int partCount)
        {
            if (vesselPrePartBuffer.Length < partCount)
                vesselPrePartBuffer = new PartVesselPreData[(int)(partCount * 1.25)];
        }

        private static PartVesselPreData[] vesselPrePartBuffer = new PartVesselPreData[300];

        private struct InertiaTensor
        {
            public double m00;
            public double m11;
            public double m22;

            public void AddPartInertiaTensor(Vector3d principalMoments, QuaternionD princAxesRot)
            {
                // inverse the princAxesRot quaternion
                double invpx = -princAxesRot.x;
                double invpy = -princAxesRot.y;
                double invpz = -princAxesRot.z;

                // prepare inverse rotation
                double ipx2 = invpx * 2.0;
                double ipy2 = invpy * 2.0;
                double ipz2 = invpz * 2.0;
                double ipx2x = invpx * ipx2;
                double ipy2y = invpy * ipy2;
                double ipz2z = invpz * ipz2;
                double ipy2x = invpx * ipy2;
                double ipz2x = invpx * ipz2;
                double ipz2y = invpy * ipz2;
                double ipx2w = princAxesRot.w * ipx2;
                double ipy2w = princAxesRot.w * ipy2;
                double ipz2w = princAxesRot.w * ipz2;

                // inverse rotate column 0
                double ir0x = principalMoments.x * (1.0 - (ipy2y + ipz2z));
                double ir0y = principalMoments.y * (ipy2x + ipz2w);
                double ir0z = principalMoments.z * (ipz2x - ipy2w);

                // inverse rotate column 1
                double ir1x = principalMoments.x * (ipy2x - ipz2w);
                double ir1y = principalMoments.y * (1.0 - (ipx2x + ipz2z));
                double ir1z = principalMoments.z * (ipz2y + ipx2w);

                // inverse rotate column 2
                double ir2x = principalMoments.x * (ipz2x + ipy2w);
                double ir2y = principalMoments.y * (ipz2y - ipx2w);
                double ir2z = principalMoments.z * (1.0 - (ipx2x + ipy2y));

                // prepare rotation
                double qx2 = princAxesRot.x * 2.0;
                double qy2 = princAxesRot.y * 2.0;
                double qz2 = princAxesRot.z * 2.0;
                double qx2x = princAxesRot.x * qx2;
                double qy2y = princAxesRot.y * qy2;
                double qz2z = princAxesRot.z * qz2;
                double qy2x = princAxesRot.x * qy2;
                double qz2x = princAxesRot.x * qz2;
                double qz2y = princAxesRot.y * qz2;
                double qx2w = princAxesRot.w * qx2;
                double qy2w = princAxesRot.w * qy2;
                double qz2w = princAxesRot.w * qz2;

                // rotate column 0
                m00 += (1.0 - (qy2y + qz2z)) * ir0x + (qy2x - qz2w) * ir0y + (qz2x + qy2w) * ir0z;

                // rotate column 1
                m11 += (qy2x + qz2w) * ir1x + (1.0 - (qx2x + qz2z)) * ir1y + (qz2y - qx2w) * ir1z;

                // rotate column 2
                m22 += (qz2x - qy2w) * ir2x + (qz2y + qx2w) * ir2y + (1.0 - (qx2x + qy2y)) * ir2z;
            }

            public void AddPartMass(double partMass, Vector3d partPosition)
            {
                double massLever = partMass * partPosition.sqrMagnitude;
                double invMass = -partMass;

                m00 += invMass * partPosition.x * partPosition.x + massLever;
                m11 += invMass * partPosition.y * partPosition.y + massLever;
                m22 += invMass * partPosition.z * partPosition.z + massLever;
            }

            public Vector3 MoI => new Vector3((float)m00, (float)m11, (float)m22);
        }

        private static void VerifyPhysicsStats(VesselPrecalculate vesselPre)
        {
            Vessel vessel = vesselPre.Vessel;
            if (!vessel.loaded)
                return;

            Transform referenceTransform = vessel.ReferenceTransform;
            int partCount = vessel.Parts.Count;
            QuaternionD vesselInverseRotation = QuaternionD.Inverse(referenceTransform.rotation);
            Vector3d pCoM = Vector3d.zero;
            Vector3d pVel = Vector3d.zero;
            Vector3d pAngularVel = Vector3d.zero;
            double vesselMass = 0.0; // vessel.totalMass
            int index = partCount;
            while (index-- > 0)
            {
                Part part = vessel.parts[index];
                if (part.rb != null)
                {
                    double physicsMass = part.physicsMass;
                    pCoM += (Vector3d)part.rb.worldCenterOfMass * physicsMass;
                    Vector3d vector3d = (Vector3d)part.rb.velocity * physicsMass;
                    pVel += vector3d;
                    pAngularVel += vesselInverseRotation * part.rb.angularVelocity * physicsMass;
                    vesselMass += physicsMass;
                }
            }
            if (vesselMass > 0.0)
            {
                double vesselMassRecip = 1.0 / vesselMass;
                Vector3d vCoMD = pCoM * vesselMassRecip; // vessel.CoMD
                Vector3d vRbVelD = pVel * vesselMassRecip; // vessel.rb_velocityD
                Vector3d vAngVelD = pAngularVel * vesselMassRecip; // vessel.angularVelocityD
                Vector3 vMoI = vessel.MOI;
                if (vAngVelD == Vector3d.zero && vessel.packed)
                {
                    vMoI.Zero();
                }
                else
                {
                    Matrix4x4 inertiaTensor = Matrix4x4.zero;
                    Matrix4x4 mIdentity = Matrix4x4.identity;
                    Matrix4x4 m2 = Matrix4x4.identity;
                    Matrix4x4 m3 = Matrix4x4.identity;
                    Quaternion vesselInverseRotationF = vesselInverseRotation;
                    for (int i = 0; i < partCount; i++)
                    {
                        Part part2 = vessel.parts[i];
                        if (part2.rb != null)
                        {
                            KSPUtil.ToDiagonalMatrix2(part2.rb.inertiaTensor, ref mIdentity);
                            Quaternion partRot = vesselInverseRotationF * part2.transform.rotation * part2.rb.inertiaTensorRotation;
                            Quaternion invPartRot = Quaternion.Inverse(partRot);
                            Matrix4x4 mPart = Matrix4x4.TRS(Vector3.zero, partRot, Vector3.one);
                            Matrix4x4 invMPart = Matrix4x4.TRS(Vector3.zero, invPartRot, Vector3.one);
                            Matrix4x4 right = mPart * mIdentity * invMPart;
                            KSPUtil.Add(ref inertiaTensor, ref right);
                            Vector3 lever = referenceTransform.InverseTransformDirection(part2.rb.position - vCoMD);
                            KSPUtil.ToDiagonalMatrix2(part2.rb.mass * lever.sqrMagnitude, ref m2);
                            KSPUtil.Add(ref inertiaTensor, ref m2);
                            KSPUtil.OuterProduct2(lever, (0f - part2.rb.mass) * lever, ref m3);
                            KSPUtil.Add(ref inertiaTensor, ref m3);
                        }
                    }
                    vMoI = KSPUtil.Diag(inertiaTensor);
                }

                string warnings = string.Empty;

                double vMassDiff = Math.Abs(vesselMass - vessel.totalMass);
                if (vMassDiff > vesselMass / 1e6)
                    warnings += $"Mass diverging by {vMassDiff:F6} ({vMassDiff / (vesselMass > 0.0 ? vesselMass : 1.0):P5}) ";

                double vCoMDDiff = (vessel.CoMD - vCoMD).magnitude;
                if (vCoMDDiff > vCoMD.magnitude / 1e6)
                    warnings += $"CoM diverging by {vCoMDDiff:F6} ({vCoMDDiff / (vCoMD.magnitude > 0.0 ? vCoMD.magnitude : 1.0):P5}) ";

                double vVelDiff = (vessel.rb_velocityD - vRbVelD).magnitude;
                if (vVelDiff > vRbVelD.magnitude / 1e6)
                    warnings += $"Velocity diverging by {vVelDiff:F6} ({vVelDiff / (vRbVelD.magnitude > 0.0 ? vRbVelD.magnitude : 1.0):P5}) ";

                double vAngVelDDiff = (vessel.angularVelocityD - vAngVelD).magnitude;
                if (vAngVelDDiff > vAngVelD.magnitude / 1e6)
                    warnings += $"Angular velocity diverging by {vAngVelDDiff:F6} ({vAngVelDDiff / (vAngVelD.magnitude > 0.0 ? vAngVelD.magnitude : 1.0):P5}) ";

                double vMoIDiff = (vessel.MOI - vMoI).magnitude;
                if (vMoIDiff > vMoI.magnitude / 1e5)
                    warnings += $"MoI diverging by {vMoIDiff:F6} ({vMoIDiff / (vMoI.magnitude > 0.0 ? vMoI.magnitude : 1.0):P5}) ";

                if (warnings.Length > 0)
                {
                    Debug.LogWarning($"[KSPCF:FIPerf] CalculatePhysicsStats : diverging stats for vessel {vessel.GetDisplayName()}\n{warnings}");
                }
            }
        }

        #endregion

        #region FlightIntegrator.UpdateMassStats optimizations

        // Avoid setting RigidBody.mass and RigidBody.centerOfMass for all parts on every update if they didn't change
        // Setting these properties is quite costly on the PhysX side, especially centerOfMass (1% to 2% of the frame time
        // depending on the situation), and centerOfMass should almost never change unless something is changing CoMOffset.
        // Setting mass is less costly and will change relatively often but avoiding setting when unecessary is still a decent improvement.
        // We also take the opportunity to make a few optimizations (faster null checks, inlined inner loop, using the PartResourceList
        // backing list instead of going through the custom indexer...)
        static bool FlightIntegrator_UpdateMassStats_Prefix(FlightIntegrator __instance)
        {
            List<Part> parts = __instance.vessel.parts;
            for (int i = __instance.partCount; i-- > 0;)
            {
                Part part = parts[i];

                List<PartResource> partResources = part._resources.dict.list;
                float resourceMass = 0f;
                double resourceThermalMass = 0.0;
                for (int j = partResources.Count; j-- > 0;)
                {
                    PartResource partResource = partResources[j];
                    float resMass = (float)partResource.amount * partResource.info.density;
                    resourceMass += resMass;
                    resourceThermalMass += resMass * partResource.info._specificHeatCapacity;
                }

                part.resourceMass = resourceMass;
                part.resourceThermalMass = resourceThermalMass;
                part.thermalMass = part.mass * __instance.cacheStandardSpecificHeatCapacity * part.thermalMassModifier + part.resourceThermalMass;
                __instance.SetSkinThermalMass(part);
                part.thermalMass = Math.Max(part.thermalMass - part.skinThermalMass, 0.1);
                part.thermalMassReciprocal = 1.0 / part.thermalMass;
            }

            for (int i = __instance.partCount; i-- > 0;)
            {
                Part part = parts[i];
                if (part.rb.IsNotNullOrDestroyed())
                {
                    float physicsMass = part.mass + part.resourceMass + __instance.GetPhysicslessChildMass(part); // further optimization : don't use recursion, avoid the null check
                    physicsMass = Mathf.Clamp(physicsMass, part.partInfo.MinimumMass, Mathf.Abs(physicsMass));
                    part.physicsMass = physicsMass;

                    if (!part.packed)
                    {
                        float rbMass = Mathf.Max(part.partInfo.MinimumRBMass, physicsMass);
                        bool hasServoRB = part.servoRb.IsNotNullOrDestroyed();

                        if (hasServoRB)
                            rbMass *= 0.5f;

                        // unfortunately, there is some internal fp manipulation when setting rb.mass
                        // resulting in tiny deltas between what we set and the value we read back.
                        if (Math.Abs(part.rb.mass - rbMass) > rbMass / 1e6f)
                        {
                            part.rb.mass = rbMass;
                            if (hasServoRB)
                                part.servoRb.mass = rbMass;
                        }

                        // Either this doesn't happen with rb.centerOfMass, or the built-in Vector3 equality
                        // epsilon takes cares of it. I guess this might happen still if a large offset is defined...
                        if (part.rb.centerOfMass != part.CoMOffset)
                            part.rb.centerOfMass = part.CoMOffset;
                    }
                }
                else
                {
                    part.physicsMass = 0.0;
                }
            }

            return false;
        }

        #endregion

        #region FlightIntegrator.UpdateOcclusion optimizations

        static bool FlightIntegrator_UpdateOcclusionSolar_Prefix(FlightIntegrator __instance)
        {
            FlightIntegrator fi = __instance;
            List<OcclusionData> occlusionDataList = fi.occlusionSun;
            OcclusionCylinder[] occluders = fi.occludersSun;
            Vector3d velocity = fi.sunVector;

            bool requiresSort = false;

            if (fi.partThermalDataCount != fi.partThermalDataList.Count)
            {
                fi.recreateThermalGraph = true;
                fi.CheckThermalGraph();
            }

            int lastPartIndex = fi.partThermalDataCount - 1;
            int partIndex = fi.partThermalDataCount;
            QuaternionDPointRotation velToUp = new QuaternionDPointRotation(Numerics.FromToRotation(velocity, Vector3d.up));
            while (partIndex-- > 0)
            {
                OcclusionData occlusionDataToUpdate = occlusionDataList[partIndex];
                UpdateOcclusionData(occlusionDataToUpdate, velocity, velToUp);
                if (!requiresSort && partIndex < lastPartIndex && occlusionDataList[partIndex + 1].maximumDot < occlusionDataToUpdate.maximumDot)
                    requiresSort = true;
            }

            if (requiresSort)
                occlusionDataList.Sort();

            OcclusionData occlusionData = occlusionDataList[lastPartIndex];
            occlusionData.ptd.sunAreaMultiplier = 1.0;
            occlusionData.sunCyl.Setup(occlusionData);
            occluders[0] = occlusionData.sunCyl;

            // O(n²) [n = part count] situation here, so micro-optimizing the inner loop is critical.
            int occluderCount = 1;
            int index = lastPartIndex;
            while (index-- > 0)
            {
                occlusionData = occlusionDataList[index];
                double minExtentsX = occlusionData.minExtents.x;
                double minExtentsY = occlusionData.minExtents.y;
                double maxExtentsX = occlusionData.maxExtents.x;
                double maxExtentsY = occlusionData.maxExtents.y;

                double areaMultiplier = 1.0;

                for (int i = 0; i < occluderCount; i++)
                {
                    // GetCylinderOcclusion
                    OcclusionCylinder occluder = occluders[i];
                    double offsetX = occluder.offset.x;
                    double offsetY = occluder.offset.y;
                    double minX = offsetX + minExtentsX;
                    double minY = offsetY + minExtentsY;
                    double maxX = offsetX + maxExtentsX;
                    double maxY = offsetY + maxExtentsY;
                    double centralExtentX = occluder.extents.x;
                    double centralExtentY = occluder.extents.y;
                    double centralExtentXInv = -centralExtentX;
                    double centralExtentYInv = -centralExtentY;

                    double mid = (maxX - minX) * (maxY - minY);
                    if (maxX >= centralExtentXInv && minX <= centralExtentX && maxY >= centralExtentYInv && minY <= centralExtentY && mid != 0.0)
                    {
                        double midX = Math.Min(centralExtentX, maxX) - Math.Max(centralExtentXInv, minX);
                        if (midX < 0.0) midX = 0.0;

                        double midY = Math.Min(centralExtentY, maxY) - Math.Max(centralExtentYInv, minY);
                        if (midY < 0.0) midY = 0.0;

                        double rectRect = midX * midY / mid;
                        if (double.IsNaN(rectRect)) // it could be nice to put that outside the inner loop
                        {
                            if (GameSettings.FI_LOG_TEMP_ERROR)
                                Debug.LogError("[FlightIntegrator]: For part " + occlusionData.ptd.part.name + ", rectRect is NaN");
                        }
                        else
                        {
                            areaMultiplier -= rectRect;
                        }
                    }

                    if (areaMultiplier < 0.001)
                    {
                        areaMultiplier = 0.0;
                        break;
                    }
                }

                occlusionData.ptd.sunAreaMultiplier = areaMultiplier;
                if (areaMultiplier > 0)
                {
                    occlusionData.sunCyl.Setup(occlusionData);
                    occluders[occluderCount] = occlusionData.sunCyl;
                    occluderCount++;
                }
            }

            return false;
        }

        static bool FlightIntegrator_UpdateOcclusionBody_Prefix(FlightIntegrator __instance)
        {
            FlightIntegrator fi = __instance;
            List<OcclusionData> occlusionDataList = fi.occlusionBody;
            OcclusionCylinder[] occluders = fi.occludersBody;
            Vector3d velocity = -fi.vessel.upAxis;

            bool requiresSort = false;

            if (fi.partThermalDataCount != fi.partThermalDataList.Count)
            {
                fi.recreateThermalGraph = true;
                fi.CheckThermalGraph();
            }

            int lastPartIndex = fi.partThermalDataCount - 1;
            int partIndex = fi.partThermalDataCount;
            QuaternionDPointRotation velToUp = new QuaternionDPointRotation(Numerics.FromToRotation(velocity, Vector3d.up));
            while (partIndex-- > 0)
            {
                OcclusionData occlusionDataToUpdate = occlusionDataList[partIndex];
                UpdateOcclusionData(occlusionDataToUpdate, velocity, velToUp);
                if (!requiresSort && partIndex < lastPartIndex && occlusionDataList[partIndex + 1].maximumDot < occlusionDataToUpdate.maximumDot)
                    requiresSort = true;
            }

            if (requiresSort)
                occlusionDataList.Sort();

            OcclusionData occlusionData = occlusionDataList[lastPartIndex];
            occlusionData.ptd.bodyAreaMultiplier = 1.0;
            occlusionData.bodyCyl.Setup(occlusionData);
            occluders[0] = occlusionData.bodyCyl;

            int occluderCount = 1;
            int index = lastPartIndex;
            while (index-- > 0)
            {
                occlusionData = occlusionDataList[index];
                double minExtentsX = occlusionData.minExtents.x;
                double minExtentsY = occlusionData.minExtents.y;
                double maxExtentsX = occlusionData.maxExtents.x;
                double maxExtentsY = occlusionData.maxExtents.y;

                double areaMultiplier = 1.0;

                for (int i = 0; i < occluderCount; i++)
                {
                    // GetCylinderOcclusion
                    OcclusionCylinder occluder = occluders[i];
                    double offsetX = occluder.offset.x;
                    double offsetY = occluder.offset.y;
                    double minX = offsetX + minExtentsX;
                    double minY = offsetY + minExtentsY;
                    double maxX = offsetX + maxExtentsX;
                    double maxY = offsetY + maxExtentsY;
                    double centralExtentX = occluder.extents.x;
                    double centralExtentY = occluder.extents.y;

                    double mid = (maxX - minX) * (maxY - minY);
                    if (!(maxX < 0.0 - centralExtentX) && !(minX > centralExtentX) && !(maxY < 0.0 - centralExtentY) && !(minY > centralExtentY) && mid != 0.0)
                    {
                        double rectRect = Math.Max(0.0, Math.Min(centralExtentX, maxX) - Math.Max(0.0 - centralExtentX, minX)) * Math.Max(0.0, Math.Min(centralExtentY, maxY) - Math.Max(0.0 - centralExtentY, minY)) / mid;
                        if (double.IsNaN(rectRect))
                        {
                            if (GameSettings.FI_LOG_TEMP_ERROR)
                                Debug.LogError("[FlightIntegrator]: For part " + occlusionData.ptd.part.name + ", rectRect is NaN");
                        }
                        else
                        {
                            areaMultiplier -= rectRect;
                        }
                    }

                    if (areaMultiplier < 0.001)
                    {
                        areaMultiplier = 0.0;
                        break;
                    }
                }

                occlusionData.ptd.bodyAreaMultiplier = areaMultiplier;
                if (areaMultiplier > 0)
                {
                    occlusionData.bodyCyl.Setup(occlusionData);
                    occluders[occluderCount] = occlusionData.bodyCyl;
                    occluderCount++;
                }
            }
            return false;
        }

        // a lot of stuff is actually unused in OcclusionData
        // boundsVertices (only used in the scope of OcclusionData.Update, we use local vars and inline stuff instead)
        // projectedVertices, projectedDots : part of an alternative thermal thing that isn't activated / never called
        // useDragArea is always true, so the involved code paths are never taken
        static void UpdateOcclusionData(OcclusionData occlusionData, Vector3d velocity, QuaternionDPointRotation velToUp)
        {
            Part part = occlusionData.part;

            if (part.IsNullOrDestroyed() || part.partTransform.IsNullOrDestroyed())
                return;

            Vector3 center = occlusionData.part.DragCubes.WeightedCenter;
            Vector3 size = occlusionData.part.DragCubes.WeightedSize;

            double cX = center.x;
            double cY = center.y;
            double cZ = center.z;
            double eX = size.x * 0.5;
            double eY = size.y * 0.5;
            double eZ = size.z * 0.5;
            double minX = cX - eX;
            double minY = cY - eY;
            double minZ = cZ - eZ;
            double maxX = cX + eX;
            double maxY = cY + eY;
            double maxZ = cZ + eZ;

            Matrix4x4D localToWorldMatrix = (Matrix4x4D)part.partTransform.localToWorldMatrix; // 10% of the load.

            // 10% of the load is here, probably worth it to extract the matrix components and to manually inline (the MultiplyPoint3x4 method is **not** inlined)
            Vector3d boundVert1 = localToWorldMatrix.MultiplyPoint3x4(minX, minY, minZ);
            Vector3d boundVert2 = localToWorldMatrix.MultiplyPoint3x4(maxX, maxY, maxZ);
            Vector3d boundVert3 = localToWorldMatrix.MultiplyPoint3x4(minX, minY, maxZ);
            Vector3d boundVert4 = localToWorldMatrix.MultiplyPoint3x4(minX, maxY, minZ);
            Vector3d boundVert5 = localToWorldMatrix.MultiplyPoint3x4(maxX, minY, minZ);
            Vector3d boundVert6 = localToWorldMatrix.MultiplyPoint3x4(minX, maxY, maxZ);
            Vector3d boundVert7 = localToWorldMatrix.MultiplyPoint3x4(maxX, minY, maxZ);
            Vector3d boundVert8 = localToWorldMatrix.MultiplyPoint3x4(maxX, maxY, minZ);

            double minDot = double.MaxValue;
            double maxDot = double.MinValue;
            double minExtentX = double.MaxValue;
            double minExtentY = double.MaxValue;
            double maxExtentX = double.MinValue;
            double maxExtentY = double.MinValue;

            FindDotMinMax(Vector3d.Dot(boundVert1, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert1, out double vertX, out double vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert2, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert2, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert3, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert3, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert4, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert4, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert5, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert5, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert6, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert6, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert7, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert7, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert8, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert8, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            Vector3d worldBoundsCenter = Numerics.MultiplyPoint3x4(ref localToWorldMatrix, cX, cY, cZ);
            occlusionData.centroidDot = Vector3d.Dot(worldBoundsCenter, velocity);
            occlusionData.projectedCenter = worldBoundsCenter - occlusionData.centroidDot * velocity;
            occlusionData.boundsCenter = new Vector3((float)cX, (float)cY, (float)cZ);
            occlusionData.minimumDot = minDot;
            occlusionData.maximumDot = maxDot;
            occlusionData.minExtents = new Vector2((float)minExtentX, (float)minExtentY); // minExtents / maxExtents : ideally flatten into double fields
            occlusionData.maxExtents = new Vector2((float)maxExtentX, (float)maxExtentY);

            occlusionData.extents = (occlusionData.maxExtents - occlusionData.minExtents) * 0.5f; // extents, center : ideally flatten into double fields
            occlusionData.center = occlusionData.minExtents + occlusionData.extents;

            occlusionData.projectedArea = part.DragCubes.CrossSectionalArea;
            occlusionData.invFineness = part.DragCubes.TaperDot;
            occlusionData.maxWidthDepth = part.DragCubes.Depth;

            occlusionData.projectedRadius = Math.Sqrt(occlusionData.projectedArea / Math.PI);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FindExtentsMinMax(double x, double z, ref double minExtentX, ref double minExtentY, ref double maxExtentX, ref double maxExtentY)
        {
            maxExtentX = Math.Max(maxExtentX, x);
            maxExtentY = Math.Max(maxExtentY, z);
            minExtentX = Math.Min(minExtentX, x);
            minExtentY = Math.Min(minExtentY, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FindDotMinMax(double dot, ref double minDot, ref double maxDot)
        {
            if (dot < minDot)
                minDot = dot;

            if (dot > maxDot)
                maxDot = dot;
        }

        private struct QuaternionDPointRotation
        {
            private double qx2x;
            private double qy2y;
            private double qz2z;
            private double qy2x;
            private double qz2x;
            private double qz2y;
            private double qx2w;
            private double qy2w;
            private double qz2w;

            public QuaternionDPointRotation(QuaternionD rotation)
            {
                double qx2 = rotation.x * 2.0;
                double qy2 = rotation.y * 2.0;
                double qz2 = rotation.z * 2.0;
                qx2x = rotation.x * qx2;
                qy2y = rotation.y * qy2;
                qz2z = rotation.z * qz2;
                qy2x = rotation.x * qy2;
                qz2x = rotation.x * qz2;
                qz2y = rotation.y * qz2;
                qx2w = rotation.w * qx2;
                qy2w = rotation.w * qy2;
                qz2w = rotation.w * qz2;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RotatePointGetXZ(Vector3d point, out double x, out double z)
            {
                x = (1.0 - (qy2y + qz2z)) * point.x + (qy2x - qz2w) * point.y + (qz2x + qy2w) * point.z;
                z = (qz2x - qy2w) * point.x + (qz2y + qx2w) * point.y + (1.0 - (qx2x + qy2y)) * point.z;
            }
        }

        #endregion
    }
}
