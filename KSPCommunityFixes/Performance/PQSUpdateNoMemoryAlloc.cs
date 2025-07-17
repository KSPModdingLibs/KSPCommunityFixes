using System;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Contracts.Agents.Mentalities;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using ModuleManager.Tags;

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

        static int callCount = 0;
        static Stopwatch stockWatch = new Stopwatch();
        static Stopwatch optimizedWatch = new Stopwatch();
        static List<Vector3> normalBuffer = new List<Vector3>();

        static void PQS_BuildTangents_Override(PQ quad)
        {
            // warmup
            Stock(quad);
            Optimized(quad);

            callCount++;

            stockWatch.Start();
            Stock(quad);
            stockWatch.Stop();

            optimizedWatch.Start();
            Optimized(quad);
            optimizedWatch.Stop();

            if (callCount % 20 == 0)
                Debug.Log($"[PQS.BuildTangents] STOCK : {stockWatch.Elapsed.TotalMilliseconds / callCount:F4} ms / OPTIMIZED : {optimizedWatch.Elapsed.TotalMilliseconds / callCount:F4} ms ");
        }

        static void Stock(PQ quad)
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
        }

        static unsafe void Optimized(PQ quad)
        {
            quad.mesh.GetNormals(normalBuffer);
            Vector3[] normals = normalBuffer._items;
            Vector3 ortho = default;
            for (int i = PQS.cacheVertCount; i-- > 0;)
            {
                ref Vector3 normal = ref normals[i];
                GetOrthogonal(ref normal, ref ortho);

                float normalX = normal.x;
                float normalY = normal.y;
                float normalZ = normal.z;

                float crossX = normalY * ortho.z - normalZ * ortho.y;
                float crossY = normalZ * ortho.x - normalX * ortho.z;
                float crossZ = normalX * ortho.y - normalY * ortho.x;

                ref Vector3 tan2 = ref PQS.tan2[i];
                float dot = crossX * tan2.x + crossY * tan2.y + crossZ * tan2.z;

                ref Vector4 tangent = ref PQS.cacheTangents[i];
                tangent.x = ortho.x;
                tangent.y = ortho.y;
                tangent.z = ortho.z;
                tangent.w = dot < 0f ? -1f : 1f;
            }

            quad.mesh.tangents = PQS.cacheTangents;
        }

        public static void GetOrthogonal(ref Vector3 vector, ref Vector3 orthogonal)
        {
            if (Mathf.Abs(vector.z) < 0.999f)
            {
                float mag = (float)Math.Sqrt(vector.x * vector.x + vector.y * vector.y);
                if (mag > 0f)
                {
                    orthogonal.x = -vector.y / mag;
                    orthogonal.y = vector.x / mag;
                    orthogonal.z = 0f;
                    return;
                }
            }
            else
            {
                float mag = (float)Math.Sqrt(vector.y * vector.y + vector.z * vector.z);
                if (mag > 0f)
                {
                    orthogonal.x = 0f;
                    orthogonal.y = -vector.z / mag;
                    orthogonal.z = vector.y / mag;
                    return;
                }
            }
            orthogonal.x = 1f;
            orthogonal.y = 0f;
            orthogonal.z = 0f;
        }
    }
}