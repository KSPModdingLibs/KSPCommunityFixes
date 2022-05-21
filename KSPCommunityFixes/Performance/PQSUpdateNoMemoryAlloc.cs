using System;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    class PQSUpdateNoMemoryAlloc : BasePatch
    {
        // This bug was introduced in KSP 1.11 when that method was refactored.
        // 
        protected override Version VersionMin => new Version(1, 11, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PQS), nameof(PQS.BuildTangents)),
                this));
        }

        static bool PQS_BuildTangents_Prefix(PQ quad)
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
                PQS.cacheTangents[i].x = tangent.x;
                PQS.cacheTangents[i].y = tangent.y;
                PQS.cacheTangents[i].z = tangent.z;
                PQS.cacheTangents[i].w = Vector3.Dot(Vector3.Cross(normal, tangent), PQS.tan2[i]) < 0f ? -1f : 1f;
            }
            quad.mesh.tangents = PQS.cacheTangents;
            return false;
        }
    }
}
