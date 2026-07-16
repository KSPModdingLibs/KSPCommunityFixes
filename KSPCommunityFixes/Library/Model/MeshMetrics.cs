using System;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// Helpers for computing the UV distribution metric of a mesh.
    /// </summary>
    /// <remarks>
    /// This mostly follows how this is implemented in the unity editor with a
    /// few fixes for things that looked obviously wrong.
    /// </remarks>
    internal static class MeshMetrics
    {
        // minimum edge length, area-threshold floor, and final-metric floor.
        const float MinLen = 0.001f;
        // minimum interior angle; rejects sliver triangles.
        const float MinAngle = 5f * Mathf.Deg2Rad;
        // minimum UV-space triangle area; guards the area divide.
        const float MinUvArea = 1e-9f;
        // if the below-mean area spread is under this, the area threshold collapses
        // to MinLen (the mesh's triangles are too uniform for the spread to mean anything).
        const float StdDevGate = 0.1f;
        // Unity's fallback when the metric can't be measure.
        const float Neutral = 1f;

        /// <summary>
        /// Bake the metric for <paramref name="uv"/> over the whole mesh.
        /// </summary>
        public static float Compute(Vector3[] verts, Vector2[] uv, int[][] subMeshTriangles)
        {
            if (verts == null || verts.Length == 0 || uv == null || subMeshTriangles == null)
                return Neutral;

            int triCount = 0;
            for (int s = 0; s < subMeshTriangles.Length; ++s)
            {
                int[] t = subMeshTriangles[s];
                if (t != null)
                    triCount += t.Length / 3;
            }
            if (triCount == 0)
                return Neutral;

            // One scratch buffer, reused across the two passes: pass 1 fills it with per-triangle 3D
            // areas (consumed to derive the threshold), pass 2 overwrites it with the kept ratios.
            float[] scratch = new float[triCount];

            // ---- Pass 1: per-triangle object-space areas -> robust small-triangle area threshold ----
            int ti = 0;
            for (int s = 0; s < subMeshTriangles.Length; ++s)
            {
                int[] t = subMeshTriangles[s];
                if (t == null)
                    continue;
                for (int k = 0; k + 2 < t.Length; k += 3)
                    scratch[ti++] = TriArea(verts[t[k]], verts[t[k + 1]], verts[t[k + 2]]);
            }

            Array.Sort(scratch, 0, triCount);      // ascending
            int keep = triCount - triCount / 10;   // robust stats over the smallest 90% of triangles

            double areaSum = 0;
            for (int i = 0; i < keep; ++i)
                areaSum += scratch[i];
            float areaMean = (float)(areaSum / keep);

            // One-sided (below-mean) standard deviation of the kept areas.
            double belowSq = 0;
            int below = 0;
            for (int i = 0; i < keep; ++i)
            {
                float d = scratch[i] - areaMean;
                if (d < 0f)
                {
                    belowSq += (double)d * d;
                    below++;
                }
            }
            float areaStdBelow = below > 0 ? (float)Math.Sqrt(belowSq / below) : 0f;

            float areaThreshold = areaMean - areaStdBelow;
            if (areaThreshold <= MinLen || areaStdBelow < StdDevGate)
                areaThreshold = MinLen;

            // ---- Pass 2: object-area / UV-area ratio for every valid triangle ----
            int count = 0;
            for (int s = 0; s < subMeshTriangles.Length; ++s)
            {
                int[] t = subMeshTriangles[s];
                if (t == null)
                    continue;
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    int i0 = t[k], i1 = t[k + 1], i2 = t[k + 2];
                    float ratio = EvalTriangle(
                        verts[i0], verts[i1], verts[i2], uv[i0], uv[i1], uv[i2], areaThreshold);
                    if (ratio > MinLen)
                        scratch[count++] = ratio;
                }
            }
            if (count == 0)
                return Neutral;

            // ---- metric = mean(ratios) + one-sided (above-mean) standard deviation ----
            double ratioSum = 0;
            for (int i = 0; i < count; ++i)
                ratioSum += scratch[i];
            float ratioMean = (float)(ratioSum / count);

            double aboveSq = 0;
            int above = 0;
            for (int i = 0; i < count; ++i)
            {
                float d = scratch[i] - ratioMean;
                if (d > 0f)
                {
                    aboveSq += (double)d * d;
                    above++;
                }
            }
            float ratioStdAbove = above > 0 ? (float)Math.Sqrt(aboveSq / above) : 0f;

            float metric = ratioMean + ratioStdAbove;
            return metric <= MinLen ? Neutral : metric;
        }

        /// <summary>Object-space area of a triangle (half the cross-product magnitude of two edges).</summary>
        static float TriArea(Vector3 a, Vector3 b, Vector3 c)
            => 0.5f * Vector3.Cross(b - a, c - a).magnitude;

        /// <summary>
        /// Object-area / UV-area for one triangle, or 0 if it's degenerate, a sliver, too small
        /// (below <paramref name="areaThreshold"/>), or has a near-zero UV footprint.
        /// </summary>
        static float EvalTriangle(
            Vector3 v0, Vector3 v1, Vector3 v2,
            Vector2 t0, Vector2 t1, Vector2 t2, float areaThreshold)
        {
            float a = (v1 - v0).magnitude;   // edge v0->v1
            float b = (v2 - v0).magnitude;   // edge v0->v2
            float c = (v1 - v2).magnitude;   // edge v2->v1
            if (a < MinLen || b < MinLen || c < MinLen)
                return 0f;

            // Every interior angle must be >= 5 degrees (law of cosines). The cosine is clamped into
            // [-1, 1] before acos so float error at a near-degenerate corner can't produce a NaN.
            float a2 = a * a, b2 = b * b, c2 = c * c;
            if (Mathf.Acos(Mathf.Clamp((a2 + b2 - c2) / (2f * a * b), -1f, 1f)) < MinAngle) return 0f;
            if (Mathf.Acos(Mathf.Clamp((c2 + b2 - a2) / (2f * b * c), -1f, 1f)) < MinAngle) return 0f;
            if (Mathf.Acos(Mathf.Clamp((c2 + a2 - b2) / (2f * c * a), -1f, 1f)) < MinAngle) return 0f;

            float area3D = 0.5f * Vector3.Cross(v2 - v0, v1 - v0).magnitude;
            // UV-space triangle area: half the absolute 2D cross product of the UV edges.
            float areaUV = 0.5f * Mathf.Abs(
                (t2.y - t0.y) * (t1.x - t0.x) - (t1.y - t0.y) * (t2.x - t0.x));

            if (area3D < areaThreshold || areaUV < MinUvArea)
                return 0f;
            return area3D / areaUV;
        }
    }
}
