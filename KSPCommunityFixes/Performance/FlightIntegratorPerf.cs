// Enable cross checking our implementations results with the stock implementations results
// Warning : very log-spammy and performance destroying, don't leave this enabled if you don't need to.
// #define DEBUG_FLIGHTINTEGRATOR

// More debug cross checks focused on drag cube stuff
// #define DEBUG_DRAGCUBEUPDATE

// More debug cross checks focused on aero stuff
// #define DEBUG_UPDATEAERO

using KSPCommunityFixes.Library;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static DragCubeList;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.Performance
{
    internal class FlightIntegratorPerf : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(VesselPrecalculate), nameof(VesselPrecalculate.CalculatePhysicsStats));

            AddPatch(PatchType.Override, typeof(FlightIntegrator), nameof(FlightIntegrator.Integrate));

#if !DEBUG_UPDATEAERO
            AddPatch(PatchType.Override, typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateAerodynamics));
#else
            AddPatch(PatchType.Prefix, typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateAerodynamics), nameof(FlightIntegrator_UpdateAerodynamics_DebugPrefix));
            AddPatch(PatchType.Postfix, typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateAerodynamics), nameof(FlightIntegrator_UpdateAerodynamics_DebugPostfix));
#endif

            AddPatch(PatchType.Override, typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateMassStats));

            AddPatch(PatchType.Override, typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusionSolar));
            AddPatch(PatchType.Override, typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusionBody));
            AddPatch(PatchType.Override, typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusionConvection));

            AddPatch(PatchType.Override, typeof(FlightIntegrator), nameof(FlightIntegrator.PrecalcRadiation));
            AddPatch(PatchType.Override, typeof(FlightIntegrator), nameof(FlightIntegrator.GetSunArea));
            AddPatch(PatchType.Override, typeof(FlightIntegrator), nameof(FlightIntegrator.GetBodyArea));
        }

        #region CalculatePhysicsStats

        /// <summary>
        /// 40-60% faster than the stock method depending on the situation.
        /// Hard to optimize further, a large chunk of the time is spent getting transform / rb properties (~40%)
        /// and performing unavoidable double/float conversions (~10%).
        /// </summary>
        private static void VesselPrecalculate_CalculatePhysicsStats_Override(VesselPrecalculate vp)
        {
            Vessel vessel = vp.vessel;
            bool isMasslessOrNotLoaded = true;

            if (vessel.loaded)
            {
                int partCount = vessel.Parts.Count;
                // This function is weird: positions are generally in world space, but angular calculations (velocity, MoI) are done relative to the reference transform (control point) orientation
                // Be mindful of which transform you're using!
                Transform vesselReferenceTransform = vessel.ReferenceTransform;
                TransformMatrix vesselInverseReferenceMatrix = TransformMatrix.WorldToLocal(vesselReferenceTransform);
                QuaternionD vesselInverseReferenceRotation = QuaternionD.Inverse(vesselReferenceTransform.rotation);
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
                        angularVelocity.Add(vesselInverseReferenceRotation * part.rb.angularVelocity * physicsMass);
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
                        vessel.MOI.MutateZero();
                        vessel.angularMomentum.MutateZero();
                    }
                    else
                    {
                        InertiaTensor inertiaTensor = new InertiaTensor();
                        Vector3d vesselCoM = vessel.CoMD;
                        Vector3d CoMToPart = default;
                        for (int i = 0; i < rbPartCount; i++)
                        {
                            PartVesselPreData partPreData = vesselPrePartBuffer[i];
                            Part part = vessel.parts[partPreData.partIndex];

                            // add part inertia tensor to vessel inertia tensor
                            QuaternionD princAxesRot = vesselInverseReferenceRotation * partPreData.rotation * (QuaternionD)part.rb.inertiaTensorRotation;
                            inertiaTensor.AddPartInertiaTensor(part.rb.inertiaTensor, ref princAxesRot);

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

                            Numerics.MutateSubstract(ref CoMToPart, partPreData.position, ref vesselCoM);
                            vesselInverseReferenceMatrix.MutateMultiplyVector(ref CoMToPart); // Note this uses the reference orientation, but doesn't use the translation
                            inertiaTensor.AddPartMass(rbMass, ref CoMToPart);
                        }

                        vessel.MOI = inertiaTensor.MoI;
                        vessel.angularMomentum.x = (float)(inertiaTensor.m00 * vessel.angularVelocityD.x);
                        vessel.angularMomentum.y = (float)(inertiaTensor.m11 * vessel.angularVelocityD.y);
                        vessel.angularMomentum.z = (float)(inertiaTensor.m22 * vessel.angularVelocityD.z);
                    }
                }

#if DEBUG_FLIGHTINTEGRATOR
                VerifyPhysicsStats(vp);
#endif
            }

            if (isMasslessOrNotLoaded)
            {
                if (vessel.packed)
                {
                    if (vessel.LandedOrSplashed)
                    {
                        vessel.CoMD = vp.worldSurfacePos + vp.worldSurfaceRot * vessel.localCoM;
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
                    vessel.rb_velocity.MutateZero();
                    vessel.rb_velocityD.MutateZero();
                    vessel.velocityD.MutateZero();
                    vessel.angularVelocity.MutateZero();
                    vessel.angularVelocityD.MutateZero();
                }
                vessel.MOI.MutateZero();
                vessel.angularMomentum.MutateZero();
            }
            vp.firstStatsRunComplete = true;
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

            public void AddPartInertiaTensor(Vector3 principalMoments, ref QuaternionD princAxesRot)
            {
                double principalMomentsX = principalMoments.x;
                double principalMomentsY = principalMoments.y;
                double principalMomentsZ = principalMoments.z;

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
                double ir0x = principalMomentsX * (1.0 - (ipy2y + ipz2z));
                double ir0y = principalMomentsY * (ipy2x + ipz2w);
                double ir0z = principalMoments.z * (ipz2x - ipy2w);

                // inverse rotate column 1
                double ir1x = principalMomentsX * (ipy2x - ipz2w);
                double ir1y = principalMomentsY * (1.0 - (ipx2x + ipz2z));
                double ir1z = principalMomentsZ * (ipz2y + ipx2w);

                // inverse rotate column 2
                double ir2x = principalMomentsX * (ipz2x + ipy2w);
                double ir2y = principalMomentsY * (ipz2y - ipx2w);
                double ir2z = principalMomentsZ * (1.0 - (ipx2x + ipy2y));

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

            public void AddPartMass(double partMass, ref Vector3d partPosition)
            {
                double massLever = partMass * partPosition.sqrMagnitude;
                double invMass = -partMass;

                m00 += invMass * partPosition.x * partPosition.x + massLever;
                m11 += invMass * partPosition.y * partPosition.y + massLever;
                m22 += invMass * partPosition.z * partPosition.z + massLever;
            }

            public Vector3 MoI => new Vector3((float)m00, (float)m11, (float)m22);
        }

#if DEBUG_FLIGHTINTEGRATOR
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
#endif

        #endregion

        #region Integrate and UpdateAerodynamics

        /// <summary>
        /// Per FlightIntegrator.FixedUpdate() call global cache for avoiding redundant computations on a per-subsystem / per-part basis.
        /// Used in the Integrate > UpdateAerodynamics code paths
        /// </summary>
        private class FIFixedUpdateIntegrationData
        {
            public Vector3 vesselPreIntegrationAccelF;
            public bool hasVesselPreIntegrationAccel;
            public Vector3 krakensbaneFrameVelocityF;

            public float mach;

            public double dragMult;
            public double dragTail;
            public double dragTip;
            public double dragSurf;
            public double dragCdPower;

            public double liftMach;

            private long currentFixedUpdate;
            private int currentVesselInstanceId;

            public void PopulateIfNeeded(FlightIntegrator fi)
            {
                if (currentFixedUpdate == KSPCommunityFixes.FixedUpdateCount && currentVesselInstanceId == fi.vessel.GetInstanceIDFast())
                    return;

                currentFixedUpdate = KSPCommunityFixes.FixedUpdateCount;
                currentVesselInstanceId = fi.vessel.GetInstanceIDFast();

                vesselPreIntegrationAccelF = fi.vessel.precalc.integrationAccel;
                hasVesselPreIntegrationAccel = !vesselPreIntegrationAccelF.IsZero();
                krakensbaneFrameVelocityF = Krakensbane.GetFrameVelocity();

                mach = (float)fi.mach;

                dragMult = PhysicsGlobals.Instance.dragCurveMultiplier.Evaluate(mach);
                dragTail = PhysicsGlobals.Instance.dragCurveTail.Evaluate(mach);
                dragTip = PhysicsGlobals.Instance.dragCurveTip.Evaluate(mach);
                dragSurf = PhysicsGlobals.Instance.dragCurveSurface.Evaluate(mach);
                dragCdPower = PhysicsGlobals.Instance.dragCurveCdPower.Evaluate(mach);
                liftMach = PhysicsGlobals.BodyLiftCurve.liftMachCurve.Evaluate(mach);
            }
        }

        private static readonly FIFixedUpdateIntegrationData fiData = new FIFixedUpdateIntegrationData();

        private static void FlightIntegrator_Integrate_Override(FlightIntegrator fi, Part part)
        {
            fiData.PopulateIfNeeded(fi);

            bool hasRb = part.rb.IsNotNullOrDestroyed();
            bool hasServoRb = part.servoRb.IsNotNullOrDestroyed();

            // base force integration
            if (hasRb)
            {
                if (fiData.hasVesselPreIntegrationAccel)
                    part.rb.AddForce(fiData.vesselPreIntegrationAccelF, ForceMode.Acceleration);

                if (!part.force.IsZero())
                    part.rb.AddForce(part.force);

                if (!part.torque.IsZero())
                    part.rb.AddTorque(part.torque);

                for (int i = part.forces.Count; i-- > 0;)
                {
                    Part.ForceHolder force = part.forces[i];
                    part.rb.AddForceAtPosition(force.force, force.pos);
                }
            }

            if (hasServoRb && fiData.hasVesselPreIntegrationAccel)
            {
                part.servoRb.AddForce(fiData.vesselPreIntegrationAccelF, ForceMode.Acceleration);
            }

            part.forces.Clear();
            part.force.Zero();
            part.torque.Zero();

            fi.UpdateAerodynamics(part);
            int childCount = part.children.Count;
            for (int i = 0; i < childCount; i++)
            {
                Part child = part.children[i];
                if (child.isAttached)
                {
                    fi.Integrate(child);
                }
            }
        }

        private static void FlightIntegrator_UpdateAerodynamics_Override(FlightIntegrator fi, Part part)
        {
            fiData.PopulateIfNeeded(fi);
            bool hasRb = part.rb.IsNotNullOrDestroyed();
            bool hasServoRb = part.servoRb.IsNotNullOrDestroyed();

            Rigidbody partOrParentRb = part.rb;
            if (!hasRb)
            {
                Part parent = part.parent;
                while (partOrParentRb.IsNullOrDestroyed() && parent.IsNotNullOrDestroyed())
                {
                    partOrParentRb = parent.rb;
                    parent = parent.parent;
                }
            }

            if (partOrParentRb.IsNotNullOrDestroyed())
            {
                part.aerodynamicArea = 0.0;
                part.exposedArea = fi.CalculateAreaExposed(part);
                part.submergedDynamicPressurekPa = 0.0;
                part.dynamicPressurekPa = 0.0;


                if (part.angularDragByFI)
                {
#if !DEBUG_UPDATEAERO
                    if (hasRb)
                        part.rb.angularDrag = 0f;

                    if (hasServoRb)
                        part.servoRb.angularDrag = 0f;
#else
                    kspcf_rb_AngularDrag = 0f;
#endif
                }

                part.dragVector = partOrParentRb.velocity + fiData.krakensbaneFrameVelocityF;
                part.dragVectorSqrMag = part.dragVector.sqrMagnitude;
                part.dragScalar = 0f;

                bool hasVelocity;
                if (part.dragVectorSqrMag != 0f)
                {
                    hasVelocity = true;
                    part.dragVectorMag = Mathf.Sqrt(part.dragVectorSqrMag);
                    part.dragVectorDir = part.dragVector / part.dragVectorMag;
                    part.dragVectorDirLocal = -part.partTransform.InverseTransformDirection(part.dragVectorDir);
                }
                else
                {
                    hasVelocity = false;
                    part.dragVectorMag = 0f;
                    part.dragVectorDir.MutateZero();
                    part.dragVectorDirLocal.MutateZero();
                }

                double submergedPortion = part.submergedPortion;

                if (!part.ShieldedFromAirstream && (part.atmDensity > 0.0 || submergedPortion > 0.0))
                {
                    if (!part.dragCubes.none)
                    {
                        SetDragCubeDrag(part.dragCubes, part.dragVectorDirLocal);
                    }

                    part.aerodynamicArea = fi.CalculateAerodynamicArea(part);
                    if (fi.cacheApplyDrag && hasVelocity && (partOrParentRb.RefEquals(part.rb) || fi.cacheApplyDragToNonPhysicsParts))
                    {
                        double emergedPortion = 1.0;
                        bool isInWater = false;
                        double pressure;
                        if (fi.currentMainBody.ocean)
                        {
                            if (submergedPortion > 0.0)
                            {
                                isInWater = true;
                                double waterDensity = fi.currentMainBody.oceanDensity * 1000.0;
                                if (submergedPortion >= 1.0)
                                {
                                    emergedPortion = 0.0;
                                    part.submergedDynamicPressurekPa = waterDensity;
                                    pressure = waterDensity * fi.cacheBuoyancyWaterAngularDragScalar * part.waterAngularDragMultiplier;
                                }
                                else
                                {
                                    emergedPortion = 1.0 - submergedPortion;
                                    part.submergedDynamicPressurekPa = waterDensity;
                                    pressure = part.staticPressureAtm * emergedPortion + submergedPortion * waterDensity * fi.cacheBuoyancyWaterAngularDragScalar * part.waterAngularDragMultiplier;
                                }
                            }
                            else
                            {
                                part.dynamicPressurekPa = part.atmDensity;
                                pressure = part.staticPressureAtm;
                            }
                        }
                        else
                        {
                            part.dynamicPressurekPa = part.atmDensity;
                            pressure = part.staticPressureAtm;
                        }

                        double dragSqrMag = 0.0005 * part.dragVectorSqrMag;
                        part.dynamicPressurekPa *= dragSqrMag;
                        part.submergedDynamicPressurekPa *= dragSqrMag;
                        if (hasRb && part.angularDragByFI)
                        {
                            if (isInWater)
                            {
                                pressure += part.dynamicPressurekPa * FlightIntegrator.KPA2ATM * emergedPortion;
                                pressure += part.submergedDynamicPressurekPa * FlightIntegrator.KPA2ATM * fi.cacheBuoyancyWaterAngularDragScalar * part.waterAngularDragMultiplier * submergedPortion;
                            }
                            else
                            {
                                pressure = part.dynamicPressurekPa * FlightIntegrator.KPA2ATM;
                            }

                            if (pressure < 0.0)
                                pressure = 0.0;


#if !DEBUG_UPDATEAERO
                            float rbAngularDrag = part.angularDrag * (float)pressure * fi.cacheAngularDragMultiplier;
                            part.rb.angularDrag = rbAngularDrag;
                            if (hasServoRb)
                                part.servoRb.angularDrag = rbAngularDrag;
#else
                            kspcf_rb_AngularDrag = part.angularDrag * (float)pressure * fi.cacheAngularDragMultiplier;
#endif
                        }

                        double dragValue = fi.CalculateDragValue(part) * fi.pseudoReDragMult;
                        if (!double.IsNaN(dragValue) && dragValue != 0.0)
                        {
                            part.dragScalar = (float)(part.dynamicPressurekPa * dragValue * emergedPortion) * fi.cacheDragMultiplier;
#if !DEBUG_UPDATEAERO
                            fi.ApplyAeroDrag(part, partOrParentRb, fi.cacheDragUsesAcceleration ? ForceMode.Acceleration : ForceMode.Force); // TODO: inline to avoid the "is part ? is parent ?" check
#endif
                        }
                        else
                        {
                            part.dragScalar = 0f;
                        }

                        if (!part.hasLiftModule && (!part.bodyLiftOnlyUnattachedLiftActual || part.bodyLiftOnlyProvider == null || !part.bodyLiftOnlyProvider.IsLifting))
                        {
                            double bodyLiftScalar = part.bodyLiftMultiplier * fi.cacheBodyLiftMultiplier * fiData.liftMach;
                            if (isInWater)
                                bodyLiftScalar *= part.dynamicPressurekPa * emergedPortion + part.submergedDynamicPressurekPa * part.submergedLiftScalar * submergedPortion;
                            else
                                bodyLiftScalar *= part.dynamicPressurekPa;

                            part.bodyLiftScalar = (float)bodyLiftScalar;
                            if (part.bodyLiftScalar != 0f && part.DragCubes.LiftForce != Vector3.zero && !part.DragCubes.LiftForce.IsInvalid())
                            {
#if !DEBUG_UPDATEAERO
                                fi.ApplyAeroLift(part, partOrParentRb, fi.cacheDragUsesAcceleration ? ForceMode.Acceleration : ForceMode.Force); // TODO: inline to avoid the "is part ? is parent ?" check
#endif
                            }
                        }
                    }
                }
            }
        }

#if DEBUG_UPDATEAERO
        private static float kspcf_rb_AngularDrag;
        private static float kspcf_dynamicPressurekPa;
        private static Vector3 kspcf_dragVectorDir;
        private static float kspcf_dragScalar;
        private static Vector3 kspcf_liftForce;
        private static float kspcf_liftScalar;

        private static void FlightIntegrator_UpdateAerodynamics_DebugPrefix(FlightIntegrator __instance, Part part)
        {
            FlightIntegrator_UpdateAerodynamics_Override(__instance, part);
            kspcf_dynamicPressurekPa = (float)part.dynamicPressurekPa; // comparing as float since this is computed from floats
            kspcf_dragVectorDir = part.dragVectorDir;
            kspcf_dragScalar = part.dragScalar;
            kspcf_liftForce = part.DragCubes.LiftForce;
            kspcf_liftScalar = part.bodyLiftScalar;
        }

        private static void FlightIntegrator_UpdateAerodynamics_DebugPostfix(Part part)
        {
            float stock_rb_angularDrag = part.rb.IsNotNullOrDestroyed() ? part.rb.angularDrag : 0f;
            float stock_dynamicPressurekPa = (float)part.dynamicPressurekPa; // comparing as float since this is computed from floats
            Vector3 stock_dragVectorDir = part.dragVectorDir;
            float stock_dragScalar = part.dragScalar;
            Vector3 stock_liftForce = part.DragCubes.LiftForce;
            float stock_liftScalar = part.bodyLiftScalar;

            if (!Numerics.AlmostEqual(stock_rb_angularDrag, kspcf_rb_AngularDrag, 20))
                Debug.Log($"[FIAeroDebug] Mismatching RB angularDrag : {Math.Abs(stock_rb_angularDrag - kspcf_rb_AngularDrag)}");

            if (!Numerics.AlmostEqual(stock_dynamicPressurekPa, kspcf_dynamicPressurekPa, 20))
                Debug.Log($"[FIAeroDebug] Mismatching dynamicPressurekPa : {Math.Abs(stock_dynamicPressurekPa - kspcf_dynamicPressurekPa)}");

            if (stock_dragVectorDir != kspcf_dragVectorDir)
                Debug.Log($"[FIAeroDebug] Mismatching dragVectorDir : {(stock_dragVectorDir - kspcf_dragVectorDir).magnitude}");

            if (!Numerics.AlmostEqual(stock_dragScalar, kspcf_dragScalar, 20))
                Debug.Log($"[FIAeroDebug] Mismatching dragScalar : {Math.Abs(stock_dragScalar - kspcf_dragScalar)}");

            if (stock_liftForce != kspcf_liftForce)
                Debug.Log($"[FIAeroDebug] Mismatching liftForce : {(stock_liftForce - kspcf_liftForce).magnitude}");

            if (!Numerics.AlmostEqual(stock_liftScalar, kspcf_liftScalar, 20))
                Debug.Log($"[FIAeroDebug] Mismatching liftScalar : {Math.Abs(stock_liftScalar - kspcf_liftScalar)}");
        }
#endif

        /// <summary>
        /// Replacement for DragCubes.SetDrag() and DragCubes.DragCubeAddSurfaceDragDirection()
        /// </summary>
        private static void SetDragCubeDrag(DragCubeList dragCubes, Vector3 direction)
        {
            direction *= -1f;

            if (dragCubes.rotateDragVector)
                direction = dragCubes.dragVectorRotation * direction;

            double dotSum = 0.0;
            double area = 0.0;
            double areaDrag = 0.0;
            double crossSectionalArea = 0.0;
            double exposedArea = 0.0;
            double liftForceX = 0.0;
            double liftForceY = 0.0;
            double liftForceZ = 0.0;
            double depth = 0.0;
            double taperDot = 0.0;
            double dragCoeff = 0.0;

            for (int i = 0; i < 6; i++)
            {
                Vector3 faceDir = faceDirections[i];
                double areaOccluded = dragCubes.areaOccluded[i];
                double weightedDrag = dragCubes.weightedDrag[i];

                double dot = Vector3.Dot(direction, faceDir);
                double dotNormalized = (dot + 1.0) * 0.5;
                double drag; // = PhysicsGlobals.DragCurveValue(__instance.SurfaceCurves, dotNormalized, machNumber);

                if (dotNormalized <= 0.5)
                    drag = Numerics.Lerp(fiData.dragTail, fiData.dragSurf, dotNormalized * 2.0) * fiData.dragMult;
                else
                    drag = Numerics.Lerp(fiData.dragSurf, fiData.dragTip, (dotNormalized - 0.5) * 2.0) * fiData.dragMult;

                double areaOccludedByDrag = areaOccluded * drag;
                area += areaOccludedByDrag;
                double dragCd = weightedDrag;

                if (dragCd < 1.0)
                    dragCd = Math.Pow(dragCubes.DragCurveCd.Evaluate((float)weightedDrag), fiData.dragCdPower);

                areaDrag += areaOccludedByDrag * dragCd;
                crossSectionalArea += areaOccluded * Numerics.Clamp01(dot);

                double weightedDragMod = (!(weightedDrag < 1.0) || !(weightedDrag > 0.01)) ? 1.0 : (1.0 / weightedDrag);
                exposedArea += areaOccludedByDrag / fiData.dragMult * weightedDragMod;

                if (dot > 0.0)
                {
                    dotSum += dot;
                    double bodyLift = dragCubes.BodyLiftCurve.liftCurve.Evaluate((float)dot);
                    double weightedBodylift = dot * areaOccluded * weightedDrag * bodyLift * -1.0;

                    if (!double.IsNaN(weightedBodylift))
                    {
                        liftForceX += faceDir.x * weightedBodylift;
                        liftForceY += faceDir.y * weightedBodylift;
                        liftForceZ += faceDir.z * weightedBodylift;
                    }

                    depth += dot * dragCubes.weightedDepth[i];
                    taperDot += dot * weightedDragMod;
                }
            }

            if (dotSum > 0.0)
            {
                double invDotSum = 1f / dotSum;
                depth *= invDotSum;
                taperDot *= invDotSum;
            }

            if (area > 0.0)
            {
                dragCoeff = areaDrag / area;
                areaDrag = area * dragCoeff;
            }
            else
            {
                dragCoeff = 0.0;
                areaDrag = 0.0;
            }

#if !DEBUG_DRAGCUBEUPDATE
            dragCubes.cubeData = new CubeData()
            {
                dragVector = direction,
                liftForce = new Vector3((float)liftForceX, (float)liftForceY, (float)liftForceZ),
                area = (float)area,
                areaDrag = (float)areaDrag,
                depth = (float)depth,
                crossSectionalArea = (float)crossSectionalArea,
                exposedArea = (float)exposedArea,
                dragCoeff = (float)dragCoeff,
                taperDot = (float)taperDot
            };
#else
            CubeData kspcfData = new CubeData()
            {
                dragVector = direction,
                liftForce = new Vector3((float)liftForceX, (float)liftForceY, (float)liftForceZ),
                area = (float)area,
                areaDrag = (float)areaDrag,
                depth = (float)depth,
                crossSectionalArea = (float)crossSectionalArea,
                exposedArea = (float)exposedArea,
                dragCoeff = (float)dragCoeff,
                taperDot = (float)taperDot
            };

            CubeData stockData = new CubeData(dragCubes.cubeData);
            dragCubes.AddSurfaceDragDirection(direction, fiData.mach, ref stockData);

            if (kspcfData.dragVector != stockData.dragVector)
                Debug.Log($"mismatching dragVector {(kspcfData.dragVector - stockData.dragVector).magnitude}");

            if (kspcfData.liftForce != stockData.liftForce)
                Debug.Log($"mismatching liftForce {(kspcfData.liftForce - stockData.liftForce).magnitude}");

            if (Math.Abs(kspcfData.area - stockData.area) > 1e-4f)
                Debug.Log($"mismatching area {Math.Abs(kspcfData.area - stockData.area)}");

            if (Math.Abs(kspcfData.areaDrag - stockData.areaDrag) > 1e-5f)
                Debug.Log($"mismatching areaDrag {Math.Abs(kspcfData.areaDrag - stockData.areaDrag)}");

            if (Math.Abs(kspcfData.depth - stockData.depth) > 1e-5f)
                Debug.Log($"mismatching depth {Math.Abs(kspcfData.depth - stockData.depth)}");

            if (Math.Abs(kspcfData.crossSectionalArea - stockData.crossSectionalArea) > 1e-5f)
                Debug.Log($"mismatching crossSectionalArea {Math.Abs(kspcfData.crossSectionalArea - stockData.crossSectionalArea)}");

            if (Math.Abs(kspcfData.exposedArea - stockData.exposedArea) > 1e-4f)
                Debug.Log($"mismatching exposedArea {Math.Abs(kspcfData.exposedArea - stockData.exposedArea)}");

            if (Math.Abs(kspcfData.dragCoeff - stockData.dragCoeff) > 1e-5f)
                Debug.Log($"mismatching dragCoeff {Math.Abs(kspcfData.dragCoeff - stockData.dragCoeff)}");

            if (Math.Abs(kspcfData.taperDot - stockData.taperDot) > 1e-5f)
                Debug.Log($"mismatching taperDot {Math.Abs(kspcfData.taperDot - stockData.taperDot)}");

            dragCubes.cubeData = kspcfData;
#endif
        }

#endregion

        #region UpdateMassStats

        // Avoid setting RigidBody.mass and RigidBody.centerOfMass for all parts on every update if they didn't change
        // Setting these properties is quite costly on the PhysX side, especially centerOfMass (1% to 2% of the frame time
        // depending on the situation), and centerOfMass should almost never change unless something is changing CoMOffset.
        // Setting mass is less costly and will change relatively often but avoiding setting when unecessary is still a decent improvement.
        // We also take the opportunity to make a few optimizations (faster null checks, inlined inner loop, using the PartResourceList
        // backing list instead of going through the custom indexer...)
        static void FlightIntegrator_UpdateMassStats_Override(FlightIntegrator fi)
        {
            List<Part> parts = fi.vessel.parts;
            int partCount = parts.Count;
            for (int i = partCount; i-- > 0;)
            {
                Part part = parts[i];

                List<PartResource> partResources = part._resources.dict.list;
                double resourceMass = 0.0;
                double resourceThermalMass = 0.0;
                for (int j = partResources.Count; j-- > 0;)
                {
                    PartResource partResource = partResources[j];
                    double resMass = partResource.amount * partResource.info._density;
                    resourceMass += resMass;
                    resourceThermalMass += resMass * partResource.info._specificHeatCapacity;
                }

                part.resourceMass = (float)resourceMass;
                part.resourceThermalMass = resourceThermalMass;
                part.thermalMass = part.mass * fi.cacheStandardSpecificHeatCapacity * part.thermalMassModifier + part.resourceThermalMass;
                fi.SetSkinThermalMass(part);
                part.thermalMass = Math.Max(part.thermalMass - part.skinThermalMass, 0.1);
                part.thermalMassReciprocal = 1.0 / part.thermalMass;
            }

            for (int i = partCount; i-- > 0;)
            {
                Part part = parts[i];
                if (part.rb.IsNotNullOrDestroyed())
                {
                    float physicsMass = part.mass + part.resourceMass + GetPhysicslessChildsMass(part);
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
        }


        // Recursion is faster than a stack based approach here
        private static float GetPhysicslessChildsMass(Part part)
        {
            float mass = 0f;
            for (int i = part.children.Count; i-- > 0;)
            {
                if (part.children[i].rb.IsNullOrDestroyed())
                {
                    Part childPart = part.children[i];
                    mass += childPart.mass + childPart.resourceMass + GetPhysicslessChildsMass(childPart);
                }
            }
            return mass;
        }


        #endregion

        #region UpdateOcclusion

        static void FlightIntegrator_UpdateOcclusionConvection_Override(FlightIntegrator fi)
        {
            if (fi.mach <= 1.0)
            {
                if (fi.wasMachConvectionEnabled)
                {
                    for (int i = 0; i < fi.partThermalDataCount; i++)
                    {
                        PartThermalData partThermalData = fi.partThermalDataList[i];
                        partThermalData.convectionCoeffMultiplier = 1.0;
                        partThermalData.convectionAreaMultiplier = 1.0;
                        partThermalData.convectionTempMultiplier = 1.0;
                    }
                    fi.wasMachConvectionEnabled = false;
                }
                return;
            }

            bool requiresSort = false;
            if (fi.partThermalDataCount != fi.partThermalDataList.Count)
            {
                fi.recreateThermalGraph = true;
                fi.CheckThermalGraph();
            }

            List<OcclusionData> occlusionDataList = fi.occlusionConv;
            OcclusionCone[] occluders = fi.occludersConvection;
            Vector3d velocity = fi.nVel;

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

            double sqrtMach = Math.Sqrt(fi.mach);
            double sqrtMachAng = Math.Asin(1.0 / sqrtMach);
            double detachAngle = 0.7957 * (1.0 - 1.0 / (fi.mach * sqrtMach));

            OcclusionData occlusionData = fi.occlusionConv[lastPartIndex];
            PartThermalData ptd = occlusionData.ptd;

            ptd.convectionCoeffMultiplier = 1.0;
            ptd.convectionAreaMultiplier = 1.0;
            ptd.convectionTempMultiplier = 1.0;

            occlusionData.convCone.Setup(occlusionData, sqrtMach, sqrtMachAng, detachAngle);

            // We do a maybe risky trick here. OcclusionCone.Setup computes shockAngle in radians, but only the tangent of that angle
            // is ever used, a bit latter in the inner loop. So we do the conversion here to avoid having to do it O(n²) times latter.
            occlusionData.convCone.shockAngle = Math.Tan(occlusionData.convCone.shockAngle);

            fi.occludersConvection[0] = occlusionData.convCone;
            fi.occludersConvectionCount = 1;
            //FXCamera.Instance.ApplyObliqueness((float)occlusionData.convCone.shockAngle); // empty method

            int index = lastPartIndex;
            while (index-- > 0)
            {
                occlusionData = fi.occlusionConv[index];
                ptd = occlusionData.ptd;
                ptd.convectionCoeffMultiplier = 1.0;
                ptd.convectionAreaMultiplier = 1.0;
                ptd.convectionTempMultiplier = 1.0;

                double minExtentsX = occlusionData.minExtents.x;
                double minExtentsY = occlusionData.minExtents.y;
                double maxExtentsX = occlusionData.maxExtents.x;
                double maxExtentsY = occlusionData.maxExtents.y;

                double projectedCenterX = occlusionData.projectedCenter.x;
                double projectedCenterY = occlusionData.projectedCenter.y;
                double projectedCenterZ = occlusionData.projectedCenter.z;

                for (int i = 0; i < fi.occludersConvectionCount; i++)
                {
                    OcclusionCone occluder = occluders[i];
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
                    double rectRect = 0.0;
                    if (maxX >= centralExtentXInv && minX <= centralExtentX && maxY >= centralExtentYInv && minY <= centralExtentY && mid != 0.0)
                    {
                        double midX = Math.Min(centralExtentX, maxX) - Math.Max(centralExtentXInv, minX);
                        if (midX < 0.0) midX = 0.0;

                        double midY = Math.Min(centralExtentY, maxY) - Math.Max(centralExtentYInv, minY);
                        if (midY < 0.0) midY = 0.0;

                        rectRect = midX * midY / mid;
                        if (double.IsNaN(rectRect)) // it could be nice to put that outside the inner loop
                        {
                            rectRect = 0.0;
                            if (GameSettings.FI_LOG_TEMP_ERROR)
                                Debug.LogError($"[FlightIntegrator]: For part " + occlusionData.ptd.part.name + ", rectRect is NaN");
                        }
                    }

                    double areaOfIntersection = 1.0;
                    if (rectRect < 0.99)
                    {
                        double angleDiff = occluder.shockNoseDot - occlusionData.centroidDot;
                        double existingConeRadius = occluder.radius + angleDiff * occluder.shockAngle; // This used to be Math.Tan(occluder.shockAngle)

                        double x = projectedCenterX - occluder.center.x;
                        double y = projectedCenterY - occluder.center.y;
                        double z = projectedCenterZ - occluder.center.z;
                        double sqrDistance = x * x + y * y + z * z;

                        areaOfIntersection = OcclusionData.AreaOfIntersection(existingConeRadius, occlusionData.projectedRadius, sqrDistance);
                    }
                    else
                    {
                        rectRect = 1.0;
                    }

                    double num4 = 1.0 - areaOfIntersection;
                    double num5 = areaOfIntersection - rectRect;
                    ptd.convectionTempMultiplier = num4 * ptd.convectionTempMultiplier + num5 * occluder.shockConvectionTempMult + rectRect * occluder.occludeConvectionTempMult;
                    ptd.convectionCoeffMultiplier = num4 * ptd.convectionCoeffMultiplier + num5 * occluder.shockConvectionCoeffMult + rectRect * occluder.occludeConvectionCoeffMult;

                    double shockStats = 1.0 - rectRect + rectRect * occluder.occludeConvectionAreaMult;

                    ptd.convectionAreaMultiplier *= shockStats;
                    if (ptd.convectionAreaMultiplier < 0.001)
                    {
                        ptd.convectionAreaMultiplier = 0.0;
                        break;
                    }
                }
                if (ptd.convectionAreaMultiplier > 0.0)
                {
                    occlusionData.convCone.Setup(occlusionData, sqrtMach, sqrtMachAng, detachAngle);
                    occlusionData.convCone.shockAngle = Math.Tan(occlusionData.convCone.shockAngle);
                    fi.occludersConvection[fi.occludersConvectionCount] = occlusionData.convCone;
                    fi.occludersConvectionCount++;
                }
            }

            return;
        }

        static void FlightIntegrator_UpdateOcclusionSolar_Override(FlightIntegrator fi)
        {
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
        }

        static void FlightIntegrator_UpdateOcclusionBody_Override(FlightIntegrator fi)
        {
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

            TransformMatrix localToWorldMatrix = TransformMatrix.LocalToWorld(part.partTransform);

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

            Vector3d worldBoundsCenter = localToWorldMatrix.MultiplyPoint3x4(cX, cY, cZ);
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

        #region PrecalcRadiation and related methods

        // Roughly twice faster than the stock implementation.
        // Most of the gains are from optimized and manually inlined versions of GetSunArea() and GetBodyArea().
        // They are virtual method that could in theory be overriden with MFI, but given how narrow scoped and tightly tied
        // the drag cube implementation they are, it is very unlikely to ever happen.
        // About half the remaining time is in getting the transform matrix from Unity. Once again, it would be immensely 
        // benefical not having to do that again and again in every subsystem...
        private static void FlightIntegrator_PrecalcRadiation_Override(FlightIntegrator fi, PartThermalData ptd)
        {
            Part part = ptd.part;
            double radfactor = fi.cacheRadiationFactor * 0.001;
            ptd.emissScalar = part.emissiveConstant * radfactor;
            ptd.absorbScalar = part.absorptiveConstant * radfactor;
            ptd.sunFlux = 0.0;
            ptd.bodyFlux = fi.bodyEmissiveFlux + fi.bodyAlbedoFlux;

            ptd.expFlux = 0.0;
            ptd.unexpFlux = 0.0;
            ptd.brtUnexposed = fi.backgroundRadiationTemp;
            ptd.brtExposed = Numerics.Lerp(fi.backgroundRadiationTemp, fi.backgroundRadiationTempExposed, ptd.convectionTempMultiplier);

            if (part.DragCubes.None || part.ShieldedFromAirstream)
                return;

            bool computeSunFlux = fi.vessel.directSunlight;
            bool computeBodyFlux = ptd.bodyFlux > 0.0;

            if (!computeSunFlux && !computeBodyFlux)
                return;

            double unexposedRadiativeArea = part.radiativeArea * (1.0 - part.skinExposedAreaFrac);

            if (computeSunFlux)
            {
                // Inlining this would allow to reclaim a bit of perf, but GetSunArea() is MFI-overridable, and actually overriden by FAR
                double sunArea = fi.GetSunArea(ptd);

                if (sunArea > 0.0)
                {
                    ptd.sunFlux = ptd.absorbScalar * fi.solarFlux;
                    if (ptd.exposed)
                    {
                        double sunDot = (Vector3d.Dot(fi.sunVector, fi.nVel) + 1.0) * 0.5;
                        double sunExpArea = Math.Min(sunArea, part.skinExposedArea * sunDot);
                        double sunUnexpArea = Math.Min(sunArea - sunExpArea, unexposedRadiativeArea * (1.0 - sunDot));
                        ptd.expFlux += ptd.sunFlux * sunExpArea;
                        ptd.unexpFlux += ptd.sunFlux * sunUnexpArea;
                    }
                    else
                    {
                        ptd.expFlux += ptd.sunFlux * sunArea;
                    }
                }
            }

            if (computeBodyFlux)
            {
                // Inlining this would allow to reclaim a bit of perf, but GetBodyArea() is MFI-overridable, and actually overriden by FAR
                double bodyArea = fi.GetBodyArea(ptd);

                if (bodyArea > 0.0)
                {
                    ptd.bodyFlux = Numerics.Lerp(0.0, ptd.bodyFlux, fi.densityThermalLerp) * ptd.absorbScalar;
                    if (ptd.exposed)
                    {
                        double bodyDot = (Vector3.Dot(-fi.vessel.upAxis, fi.nVel) + 1.0) * 0.5;
                        double bodyExpArea = Math.Min(bodyArea, part.skinExposedArea * bodyDot);
                        double bodyUnexpArea = Math.Min(bodyArea - bodyExpArea, unexposedRadiativeArea * (1.0 - bodyDot));
                        ptd.expFlux += ptd.bodyFlux * bodyExpArea;
                        ptd.unexpFlux += ptd.bodyFlux * bodyUnexpArea;
                    }
                    else
                    {
                        ptd.expFlux += ptd.bodyFlux * bodyArea;
                    }
                }
            }
        }

        // we manually inline the call to GetSunArea() in PrecalcRadiation(), but this is also used
        // in CalculateAnalyticTemperature(), so that should help there too.
        private static double FlightIntegrator_GetSunArea_Override(FlightIntegrator fi, PartThermalData ptd)
        {
            if (ptd.part.DragCubes.None)
                return 0.0;

            Vector3 sunLocalDir = ptd.part.partTransform.InverseTransformDirection(fi.sunVector);
            float[] dragCubeAreaOccluded = ptd.part.DragCubes.areaOccluded;

            double sunArea = 0.0;
            if (sunLocalDir.x > 0.0)
                sunArea += dragCubeAreaOccluded[0] * sunLocalDir.x; // right
            else
                sunArea += dragCubeAreaOccluded[1] * -sunLocalDir.x; // left

            if (sunLocalDir.y > 0.0)
                sunArea += dragCubeAreaOccluded[2] * sunLocalDir.y; // up
            else
                sunArea += dragCubeAreaOccluded[3] * -sunLocalDir.y; // down

            if (sunLocalDir.z > 0.0)
                sunArea += dragCubeAreaOccluded[4] * sunLocalDir.z; // forward
            else
                sunArea += dragCubeAreaOccluded[5] * -sunLocalDir.z; // back

            return sunArea * ptd.sunAreaMultiplier;
        }

        // we manually inline the call to GetBodyArea() in PrecalcRadiation(), but this is also used
        // in CalculateAnalyticTemperature(), so that should help there too.
        private static double FlightIntegrator_GetBodyArea_Override(FlightIntegrator fi, PartThermalData ptd)
        {
            if (ptd.part.DragCubes.None)
                return 0.0;

            Vector3 bodyLocalDir = ptd.part.partTransform.InverseTransformDirection(-fi.vessel.upAxis);
            float[] dragCubeAreaOccluded = ptd.part.DragCubes.areaOccluded;

            double bodyArea = 0.0;
            if (bodyLocalDir.x > 0.0)
                bodyArea += dragCubeAreaOccluded[0] * bodyLocalDir.x; // right
            else
                bodyArea += dragCubeAreaOccluded[1] * -bodyLocalDir.x; // left

            if (bodyLocalDir.y > 0.0)
                bodyArea += dragCubeAreaOccluded[2] * bodyLocalDir.y; // up
            else
                bodyArea += dragCubeAreaOccluded[3] * -bodyLocalDir.y; // down

            if (bodyLocalDir.z > 0.0)
                bodyArea += dragCubeAreaOccluded[4] * bodyLocalDir.z; // forward
            else
                bodyArea += dragCubeAreaOccluded[5] * -bodyLocalDir.z; // back

            return bodyArea * ptd.bodyAreaMultiplier;
        }

        #endregion
    }
}
