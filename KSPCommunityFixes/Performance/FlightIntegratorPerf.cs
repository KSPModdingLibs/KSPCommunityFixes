using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using static EdyCommonTools.RotationController;
using static EdyCommonTools.Spline;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.Performance
{
    internal class FlightIntegratorPerf : BasePatch
    {
        private static Stopwatch updateOcclusionWatch = new Stopwatch();
        private static int callCount;

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusion)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusion)),
                this));

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

            // other offenders, in aero situations :

            // AddSurfaceDragDirection : 7%
            // - could turn the curves (1.6%) into lookup tables
            // - general float to double pass, not sure how practical due to working a lot with float-backed drag cubes, relevant at least for the Mathf.Pow() call
            // - multiple InverseTransform calls, use the matrix instead

            // general optimization pass on UpdateOcclusionConvection() : 4.4%

        }

        // Avoid setting RigidBody.mass and RigidBody.centerOfMass for all parts on every update if they didn't change
        // Setting these properties is quite costly on the PhysX side, especially centerOfMass (1% to 2% of the frame time
        // depending on the situation), and centerOfMass should almost never change unless somthing is changing CoMOffset.
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
                    bool shouldSetRBProperties = !part.packed;
                    float physicsMass = part.mass + part.resourceMass + __instance.GetPhysicslessChildMass(part); // don't use recursion, avoid the null check
                    physicsMass = Mathf.Clamp(physicsMass, part.partInfo.MinimumMass, Mathf.Abs(physicsMass));
                    if (part.physicsMass != physicsMass)
                    {
                        part.physicsMass = physicsMass;
                        if (shouldSetRBProperties)
                        {
                            float rbMass = Mathf.Max(part.partInfo.MinimumRBMass, physicsMass);
                            if (part.servoRb.IsNotNullOrDestroyed())
                            {
                                float halfRBMass = rbMass * 0.5f;
                                part.rb.mass = halfRBMass;
                                part.servoRb.mass = halfRBMass;
                            }
                            else
                            {
                                part.rb.mass = rbMass;
                            }
                        }
                    }

                    if (shouldSetRBProperties && part.rb.centerOfMass != part.CoMOffset)
                        part.rb.centerOfMass = part.CoMOffset;
                }
                else
                {
                    part.physicsMass = 0.0;
                }
            }

            return false;
        }

        static void FlightIntegrator_UpdateOcclusion_Prefix(bool all)
        {
            if (!all)
            {
                callCount++;
                updateOcclusionWatch.Start();
            }
        }

        static void FlightIntegrator_UpdateOcclusion_Postfix()
        {
            updateOcclusionWatch.Stop();
            if (callCount % 100 == 0)
            {
                Debug.Log($"[UpdateOcclusion] {updateOcclusionWatch.Elapsed.TotalMilliseconds / callCount:F3}ms/call");
            }
        }

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
            QuaternionDPointRotation velToUp = new QuaternionDPointRotation(FromToRotation(velocity, Vector3d.up));
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
                        if (midX < 0.0)
                            midX = 0.0;

                        double midY = Math.Min(centralExtentY, maxY) - Math.Max(centralExtentYInv, minY);
                        if (midY < 0.0)
                            midY = 0.0;

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
            QuaternionDPointRotation velToUp = new QuaternionDPointRotation(FromToRotation(velocity, Vector3d.up));
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

            Matrix4x4D localToWorldMatrix = (Matrix4x4D)part.partTransform.localToWorldMatrix; // 10% of the load. probably can't be avoided, but well...

            // 10% of the load is here, probably worth it to extract the matrix components and to manually inline (the MultiplyPoint3x4 method is **not** inlined)
            Vector3d boundVert1 = MultiplyPoint3x4(localToWorldMatrix, minX, minY, minZ);
            Vector3d boundVert2 = MultiplyPoint3x4(localToWorldMatrix, maxX, maxY, maxZ);
            Vector3d boundVert3 = MultiplyPoint3x4(localToWorldMatrix, minX, minY, maxZ);
            Vector3d boundVert4 = MultiplyPoint3x4(localToWorldMatrix, minX, maxY, minZ);
            Vector3d boundVert5 = MultiplyPoint3x4(localToWorldMatrix, maxX, minY, minZ);
            Vector3d boundVert6 = MultiplyPoint3x4(localToWorldMatrix, minX, maxY, maxZ);
            Vector3d boundVert7 = MultiplyPoint3x4(localToWorldMatrix, maxX, minY, maxZ);
            Vector3d boundVert8 = MultiplyPoint3x4(localToWorldMatrix, maxX, maxY, minZ);

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

            Vector3d worldBoundsCenter = MultiplyPoint3x4(localToWorldMatrix, cX, cY, cZ);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3d MultiplyPoint3x4(Matrix4x4D m, double x, double y, double z)
        {
            return new Vector3d(
                m.m00 * x + m.m01 * y + m.m02 * z + m.m03,
                m.m10 * x + m.m11 * y + m.m12 * z + m.m13,
                m.m20 * x + m.m21 * y + m.m22 * z + m.m23);
        }

        private static QuaternionD FromToRotation(Vector3d from, Vector3d to)
        {
            double d = Vector3d.Dot(from, to);
            double qw = Math.Sqrt(from.sqrMagnitude * to.sqrMagnitude) + d;
            double x, y, z, sqrMag;
            if (qw < 1e-12)
            {
                // vectors are 180 degrees apart
                x = from.x;
                y = from.y;
                z = -from.z;
                sqrMag = x * x + y * y + z * z;
                if (sqrMag != 1.0)
                {
                    double invNorm = 1.0 / Math.Sqrt(sqrMag);
                    x *= invNorm;
                    y *= invNorm;
                    z *= invNorm;
                }
                return new QuaternionD(x, y, z, 0.0);
            }

            Vector3d axis = Vector3d.Cross(from, to);
            x = axis.x;
            y = axis.y;
            z = axis.z;
            sqrMag = x * x + y * y + z * z + qw * qw;
            if (sqrMag != 1.0)
            {
                double invNorm = 1.0 / Math.Sqrt(sqrMag);
                x *= invNorm;
                y *= invNorm;
                z *= invNorm;
                qw *= invNorm;
            }

            return new QuaternionD(x, y, z, qw);
        }
    }

    internal struct QuaternionDPointRotation
    {
        private double qx2;
        private double qy2;
        private double qz2;
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
            qx2 = rotation.x * 2.0;
            qy2 = rotation.y * 2.0;
            qz2 = rotation.z * 2.0;
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

    public class KSPCFOcclusionData: OcclusionData
    {

        // projecte
        public KSPCFOcclusionData(PartThermalData data) : base(data)
        {

        }
    }
}
