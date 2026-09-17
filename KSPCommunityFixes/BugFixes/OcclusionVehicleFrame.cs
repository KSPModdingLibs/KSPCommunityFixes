using System;
using HarmonyLib;
using KSPCommunityFixes.Library;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    /// <summary>
    /// Judge the thermal occlusion between parts in a frame fixed to the vessel instead of the world axes.<para/>
    /// Stock's convection, solar and body occlusion (<c>FlightIntegrator.UpdateOcclusionConvection/Solar/Body</c>)
    /// reduce each part to an axis-aligned rectangle - the drag-cube box corners projected along the airflow, the sun
    /// vector or the local vertical, then bounded in the x/z coordinates of <c>Quaternion.FromToRotation(direction,
    /// Vector3.up)</c> (<see cref="OcclusionData.Update"/>) - and a part is shaded by how much of its rectangle the
    /// rectangles of the parts in front of it cover (<see cref="OcclusionData.GetShockStats"/>,
    /// <see cref="OcclusionData.GetCylinderOcclusion"/>). Those x/z axes are Unity's world axes carried along by the
    /// shortest rotation, so the vessel's silhouette sits at an angle in them that depends on the planet's azimuth in
    /// world coordinates, the heading and the inclination - not on anything about the vessel or the flow. A rectangle
    /// turned 45 degrees in that frame gets a bounding box up to 41 % wider, so a part poking out past a blunt part
    /// ahead of it is counted exposed at one angle and covered at another.<para/>
    /// With this patch the x axis is the vessel's lateral axis (<see cref="Vessel.ReferenceTransform"/>.right, projected
    /// across the direction; the nose stands in when the vessel flies sideways), so the rectangles are the vessel's own
    /// and the answer is a property of the vessel and its attitude alone. Same rectangles, same cones, same arithmetic.
    /// Applies to the stock <see cref="OcclusionData.Update"/> and to the <c>FlightIntegratorPerf</c> reimplementation.
    /// </summary>
    class OcclusionVehicleFrame : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        internal static bool IsEnabled { get; private set; }

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(OcclusionData), nameof(OcclusionData.Update));
        }

        protected override void OnPatchApplied()
        {
            IsEnabled = true;
        }

        /// <summary>
        /// The rotation taking <paramref name="direction"/> to +Y and the vessel's lateral axis to +X: the frame the
        /// rectangles are bounded in. Falls back to stock's world-up frame without a vessel or a control point.
        /// </summary>
        internal static QuaternionD Frame(Vector3d direction, Vessel vessel)
        {
            Transform reference = vessel.IsNullOrDestroyed() ? null : vessel.ReferenceTransform;
            if (reference.IsNullOrDestroyed())
                return Numerics.FromToRotation(direction, Vector3d.up);

            Vector3d x = reference.right;
            x -= Vector3d.Dot(x, direction) * direction;
            if (x.sqrMagnitude < 1e-6)
            {
                x = reference.up;
                x -= Vector3d.Dot(x, direction) * direction;
                if (x.sqrMagnitude < 1e-6)
                    return Numerics.FromToRotation(direction, Vector3d.up);
            }
            x.Normalize();
            // LookRotation(forward, up) sends local +Z to forward and +Y to up, so local +X goes to
            // up x forward = direction x (x x direction) = x; its inverse takes world x to +X and the direction to +Y.
            Vector3d z = Vector3d.Cross(x, direction);
            return QuaternionD.Inverse(Quaternion.LookRotation(z, direction));
        }

        // OcclusionData.Update with the projection frame taken from the vessel. Only reached when FlightIntegratorPerf
        // is disabled; that patch has its own copy of this loop (FlightIntegratorPerf.UpdateOcclusionData) and asks
        // Frame() for the rotation.
        private static void OcclusionData_Update_Override(OcclusionData od, Vector3 velocity, bool useDragArea)
        {
            Part part = od.part;
            if (part.IsNullOrDestroyed() || part.partTransform.IsNullOrDestroyed())
                return;

            od.CreateCornerArray();
            Matrix4x4 localToWorldMatrix = part.partTransform.localToWorldMatrix;
            Vector3 center = localToWorldMatrix.MultiplyPoint3x4(od.boundsCenter);
            od.centroidDot = Vector3d.Dot(center, velocity);
            od.projectedCenter = (Vector3d)center - od.centroidDot * (Vector3d)velocity;
            Quaternion frame = (Quaternion)Frame(velocity, part.vessel);
            od.minimumDot = double.MaxValue;
            od.maximumDot = double.MinValue;
            od.minExtents = new Vector2(float.MaxValue, float.MaxValue);
            od.maxExtents = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 vertex = localToWorldMatrix.MultiplyPoint3x4(od.boundsVertices[i]);
                double dot = Vector3d.Dot(vertex, velocity);
                od.projectedVertices[i] = (Vector3d)vertex - dot * (Vector3d)velocity;
                Vector3 inFrame = frame * vertex;
                od.maxExtents.x = Math.Max(od.maxExtents.x, inFrame.x);
                od.maxExtents.y = Math.Max(od.maxExtents.y, inFrame.z);
                od.minExtents.x = Math.Min(od.minExtents.x, inFrame.x);
                od.minExtents.y = Math.Min(od.minExtents.y, inFrame.z);
                if (dot < od.minimumDot) od.minimumDot = dot;
                if (dot > od.maximumDot) od.maximumDot = dot;
                od.projectedDots[i] = (float)dot;
            }
            od.extents = (od.maxExtents - od.minExtents) * 0.5f;
            od.center = od.minExtents + od.extents;
            if (useDragArea)
            {
                od.projectedArea = part.DragCubes.CrossSectionalArea;
                od.invFineness = part.DragCubes.TaperDot;
                od.maxWidthDepth = part.DragCubes.Depth;
            }
            else
            {
                od.projectedArea = part.DragCubes.GetCubeAreaDir(velocity);
                od.invFineness = part.DragCubes.GetCubeCoeffDir(velocity);
                od.maxWidthDepth = part.DragCubes.GetCubeDepthDir(velocity);
            }
            od.projectedRadius = Math.Sqrt(od.projectedArea / Math.PI);
        }
    }
}
