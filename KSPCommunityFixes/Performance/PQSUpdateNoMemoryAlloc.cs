using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    class PQSUpdateNoMemoryAlloc : BasePatch
    {
        // This bug was introduced in KSP 1.11 when that method was refactored.
        // 
        protected override Version VersionMin => new Version(1, 11, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(PQS), nameof(PQS.BuildTangents));
        }

        private static void PQS_BuildTangents_Override(PQ quad)
        {
            Vector3[] normals = quad.mesh.normals;
            for (int i = 0; i < PQS.cacheVertCount; i++)
            {
                // original was 
                // Vector3 normal = quad.mesh.normals[i];
                // resulting in the whole array being instantiated for each vertex
                Vector3 normal = normals[i];
                Vector3 tangent = Vector3.zero;
                Vector3.OrthoNormalize(ref normal, ref tangent);
                ref Vector4 result = ref PQS.cacheTangents[i];
                result.x = tangent.x;
                result.y = tangent.y;
                result.z = tangent.z;
                result.w = Vector3.Dot(Vector3.Cross(normal, tangent), PQS.tan2[i]) < 0f ? -1f : 1f;
            }
            quad.mesh.tangents = PQS.cacheTangents;
        }

        private static float GetTimeStamp()
        {
            if (Stopwatch.IsHighResolution)
                return Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency;

            return Time.realtimeSinceStartup;
        }
    }
}
