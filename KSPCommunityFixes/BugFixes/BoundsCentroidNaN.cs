using System;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    // PartGeometryUtil.FindBoundsCentroid returns NaN when the bounds array is empty or every
    // bounds has zero size. This can cause a Unity crash when taking a snapshot of an empty craft,
    // and can also affect ModuleCargoBay and KerbalEVA which perform operations on that position.
    // The prefix here replaces the stock function, falling back to the unweighted average of the
    // centers when no bounds has a size, and to the origin (of localTo, when given) when there are
    // no bounds.
    internal class BoundsCentroidNaN : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(PartGeometryUtil), nameof(PartGeometryUtil.FindBoundsCentroid));
        }

        private static bool PartGeometryUtil_FindBoundsCentroid_Prefix(Bounds[] bounds, Transform localTo, out Vector3 __result)
        {
            bool local = localTo.IsNotNullOrDestroyed();
            Vector3 weightedSum = Vector3.zero;
            Vector3 sum = Vector3.zero;
            float totalWeight = 0f;
            for (int i = bounds.Length; i-- > 0;)
            {
                Vector3 center = local ? localTo.InverseTransformPoint(bounds[i].center) : bounds[i].center;
                float weight = bounds[i].size.magnitude;
                weightedSum += center * weight;
                sum += center;
                totalWeight += weight;
            }

            if (totalWeight > 0f)
                __result = weightedSum / totalWeight;
            else if (bounds.Length > 0)
                __result = sum / bounds.Length;
            else
                __result = Vector3.zero;

            return false;
        }
    }
}
