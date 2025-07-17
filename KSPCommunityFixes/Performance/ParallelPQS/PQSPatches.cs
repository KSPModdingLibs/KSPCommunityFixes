using Steamworks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using static FinePrint.ContractDefs;
using static HarmonyLib.Code;

namespace KSPCommunityFixes.Performance.ParallelPQS
{
    public class FastPQ : PQ
    {
        public PQSExtension sphereRootExtension;
    }

    public class PQSPatches : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(PQSCache), nameof(PQSCache.IncreasePQCache));
            AddPatch(PatchType.Override, typeof(PQSCache), nameof(PQSCache.DestroyQuad));
            AddPatch(PatchType.Override, typeof(PQS), nameof(PQS.AssignQuad));
            AddPatch(PatchType.Override, typeof(PQS), nameof(PQS.UpdateQuads));
            AddPatch(PatchType.Override, typeof(PQ), nameof(PQ.UpdateSubdivision));
            AddPatch(PatchType.Override, typeof(PQ), nameof(PQ.Subdivide));
        }

        private static void PQSCache_IncreasePQCache_Override(PQSCache pqsCache, int addCount)
        {
            for (int i = 0; i < addCount; i++)
            {
                GameObject gameObject = new GameObject();
                FastPQ pQ = gameObject.AddComponent<FastPQ>();
                pQ.meshFilter = gameObject.AddComponent<MeshFilter>();
                pQ.meshRenderer = gameObject.AddComponent<MeshRenderer>();
                gameObject.transform.parent = pqsCache.transform;
                gameObject.transform.localPosition = Vector3.zero;
                gameObject.transform.localRotation = Quaternion.identity;
                gameObject.transform.localScale = Vector3.one;
                pQ.mesh = new Mesh();
                pQ.mesh.vertices = PQS.cacheVerts;
                pQ.mesh.uv = PQS.cacheUVs;
                pQ.mesh.normals = PQS.cacheNormals;
                pQ.mesh.tangents = PQS.cacheTangents;
                pQ.mesh.triangles = PQS.cacheIndices[0];
                pQ.mesh.colors = PQS.cacheColors;
                pQ.meshFilter.sharedMesh = pQ.mesh;
                pQ.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                pQ.meshRenderer.receiveShadows = false;
                pQ.verts = (Vector3[])PQS.cacheVerts.Clone();
                pQ.vertNormals = (Vector3[])PQS.cacheNormals.Clone();
                pQ.edgeNormals = new Vector3[4][];
                for (int j = 0; j < 4; j++)
                {
                    pQ.edgeNormals[j] = new Vector3[PQS.cacheSideVertCount];
                }
                gameObject.SetActive(value: false);
                pqsCache.cachePQUnassigned.Push(pQ);
            }
            pqsCache.cachePQUnassignedCount += addCount;
            pqsCache.cachePQTotalCount += addCount;
        }

        private static void PQSCache_DestroyQuad_Override(PQSCache pqsCache, PQ quad)
        {
            quad.north = null;
            quad.south = null;
            quad.east = null;
            quad.west = null;
            quad.parent = null;
            quad.quadRoot = null;
            quad.sphereRoot = null;
            ((FastPQ)quad).sphereRootExtension = null;
            quad.subdivideThresholdFactor = 1.0;
            quad.isBuilt = false;
            quad.isSubdivided = false;
            quad.isActive = false;
            quad.isVisible = false;
            quad.transform.parent = pqsCache.transform;
            quad.meshRenderer.enabled = false;
            quad.gameObject.SetActive(value: false);
            pqsCache.cachePQAssignedCount--;
            pqsCache.cachePQUnassignedCount++;
            pqsCache.cachePQUnassigned.Push(quad);
        }

        private static PQ PQS_AssignQuad_Override(PQS pqs, int subdiv)
        {
            FastPQ quad = (FastPQ)pqs.cache.GetQuad();
            quad.transform.parent = pqs.transform;
            if (pqs.useSharedMaterial)
            {
                quad.meshRenderer.sharedMaterial = pqs.surfaceMaterial;
            }
            else
            {
                quad.meshRenderer.material = pqs.surfaceMaterial;
            }
            if (GameSettings.CELESTIAL_BODIES_CAST_SHADOWS)
            {
                quad.meshRenderer.shadowCastingMode = (pqs.meshCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
                quad.meshRenderer.receiveShadows = pqs.meshRecieveShadows;
            }
            else
            {
                quad.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                quad.meshRenderer.receiveShadows = pqs.meshRecieveShadows;
            }
            quad.meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            quad.sphereRoot = pqs;
            quad.sphereRootExtension = PQSExtension.Get(pqs);
            quad.gameObject.layer = pqs.gameObject.layer;
            quad.subdivision = subdiv;
            quad.subdivideThresholdFactor = 1.0;
            quad.quadRoot = null;
            quad.id = pqs.pqID++;
            quad.gameObject.SetActive(value: true);
            quad.isActive = true;
            quad.isForcedInvisible = false;
            return quad;
        }

        private static void PQS_UpdateQuads_Override(PQS pqs)
        {
            if (pqs.quads == null)
                return;

            PQSExtension pqsExtension = PQSExtension.Get(pqs);
            pqsExtension.StartOutOfTimeTimer();

            pqs.isThinking = true;
            //pqs.maxFrameEnd = Time.realtimeSinceStartup + pqs.maxFrameTime;
            int quadCount = pqs.quads.Length;
            for (int i = 1; i < quadCount; i++)
            {
                PQ pq = pqs.quads[i];
                int prevIdx = i - 1;
                while (prevIdx >= 0 && !((pqs.relativeTargetPosition - pqs.quads[prevIdx].positionPlanetRelative).sqrMagnitude <= (pqs.relativeTargetPosition - pq.positionPlanetRelative).sqrMagnitude))
                {
                    pqs.quads[prevIdx + 1] = pqs.quads[prevIdx];
                    prevIdx--;
                }
                pqs.quads[prevIdx + 1] = pq;
            }

            pqs.quads[0].UpdateSubdivision();
            pqs.quads[1].UpdateSubdivision();
            pqs.quads[2].UpdateSubdivision();
            pqs.quads[3].UpdateSubdivision();
            pqs.quads[4].UpdateSubdivision();
            pqs.quads[5].UpdateSubdivision();

            if (pqs.reqCustomNormals)
                pqs.UpdateEdges();

            pqs.isThinking = false;
            pqsExtension.StopOutOfTimeTimer();
        }

        private static void PQ_UpdateSubdivision_Override(FastPQ pq)
        {
            //pq.UpdateTargetRelativity();

            double cosTheta = Vector3d.Dot(pq.positionPlanetRelative, pq.sphereRoot.relativeTargetPositionNormalized);
            pq.gcd1 = Math.Sqrt(2.0 * (1.0 - cosTheta)) * pq.sphereRoot.radius * 1.3;
            pq.gcDist = pq.gcd1 + Math.Abs(pq.sphereRoot.targetHeight) - pq.angularinterval;


            pq.outOfTime = pq.sphereRootExtension.IsOutOfTime;
            //pq.outOfTime = Time.realtimeSinceStartup > pq.sphereRoot.maxFrameEnd;

            pq.isPendingCollapse = false;
            if (pq.isSubdivided)
            {
                pq.meshRenderer.enabled = false; // is this really necessary ?
                bool flag = pq.gcDist > pq.sphereRoot.collapseThresholds[pq.subdivision] * pq.subdivideThresholdFactor;
                if (pq.subdivision <= pq.sphereRoot.maxLevel && (!flag || pq.outOfTime))
                {
                    if (flag)
                        pq.isPendingCollapse = true;

                    for (int i = 0; i < 4; i++)
                        if (pq.subNodes[i].IsNotNullOrDestroyed())
                            pq.subNodes[i].UpdateSubdivision();
                }
                else if (!pq.Collapse())
                {
                    for (int j = 0; j < 4; j++)
                        if (pq.subNodes[j].IsNotNullOrDestroyed())
                            pq.subNodes[j].UpdateSubdivision();
                }
            }
            else if (pq.subdivision >= pq.sphereRoot.minLevel && (!(pq.gcDist < pq.sphereRoot.subdivisionThresholds[pq.subdivision] * pq.subdivideThresholdFactor) || pq.subdivision >= pq.sphereRoot.maxLevelAtCurrentTgtSpeed || pq.outOfTime))
            {
                pq.UpdateVisibility();
            }
            else
            {
                pq.Subdivide();
            }

            if (pq.onUpdate != null)
                pq.onUpdate(pq);
        }

        private static bool PQ_Subdivide_Override(PQ pq)
        {
            if (pq.north.subdivision < pq.subdivision)
                return false;

            if (pq.east.subdivision < pq.subdivision)
                return false;

            if (pq.south.subdivision < pq.subdivision)
                return false;

            if (pq.west.subdivision < pq.subdivision)
                return false;

            if (pq.isSubdivided)
                return true;

            if (!pq.isActive)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (pq.subNodes[i].IsNotNullOrDestroyed())
                {
                    pq.subNodes[i].isActive = true;
                    Debug.Log(pq.subNodes[i].gameObject.name);
                    Debug.Break();
                    continue;
                }

                PQ subNode = pq.sphereRoot.AssignQuad(pq.subdivision + 1);
                int num = i % 2;
                int num2 = i / 2;
                subNode.scalePlaneRelative = pq.scalePlaneRelative * 0.5;
                subNode.scalePlanetRelative = pq.sphereRoot.radius * subNode.scalePlaneRelative;

                if (pq.quadRoot.IsNullOrDestroyed())
                    subNode.quadRoot = pq;
                else
                    subNode.quadRoot = pq.quadRoot;

                subNode.CreateParent = pq;
                subNode.positionParentRelative = subNode.quadRoot.planeRotation * new Vector3d(((double)num - 0.5) * pq.scalePlaneRelative, 0.0, ((double)num2 - 0.5) * pq.scalePlaneRelative);
                subNode.positionPlanePosition = pq.positionPlanePosition + subNode.positionParentRelative;
                subNode.positionPlanetRelative = subNode.positionPlanePosition.normalized;
                subNode.positionPlanet = subNode.positionPlanetRelative * pq.sphereRoot.GetSurfaceHeight(subNode.positionPlanetRelative);
                subNode.plane = pq.plane;
                subNode.sphereRoot = pq.sphereRoot;
                ((FastPQ)subNode).sphereRootExtension = ((FastPQ)pq).sphereRootExtension;
                subNode.subdivision = pq.subdivision + 1;
                subNode.parent = pq;
                subNode.Corner = i;
                subNode.name = pq.gameObject.name + i;
                subNode.gameObject.layer = pq.gameObject.layer;
                pq.sphereRoot.QuadCreated(subNode);
                pq.subNodes[i] = subNode;
            }

            pq.subNodes[0].north = pq.subNodes[2];
            pq.subNodes[0].east = pq.subNodes[1];
            pq.subNodes[1].north = pq.subNodes[3];
            pq.subNodes[1].west = pq.subNodes[0];
            pq.subNodes[2].south = pq.subNodes[0];
            pq.subNodes[2].east = pq.subNodes[3];
            pq.subNodes[3].south = pq.subNodes[1];
            pq.subNodes[3].west = pq.subNodes[2];

            PQ left;
            PQ right;

            if (pq.north.subdivision == pq.subdivision && pq.north.isSubdivided)
            {
                pq.north.GetEdgeQuads(pq, out left, out right);
                pq.subNodes[2].north = left;
                pq.subNodes[3].north = right;
                left.SetNeighbour(pq, pq.subNodes[2]);
                right.SetNeighbour(pq, pq.subNodes[3]);
            }
            else
            {
                pq.subNodes[2].north = pq.north;
                pq.subNodes[3].north = pq.north;
            }

            if (pq.south.subdivision == pq.subdivision && pq.south.isSubdivided)
            {
                pq.south.GetEdgeQuads(pq, out left, out right);
                pq.subNodes[1].south = left;
                pq.subNodes[0].south = right;
                left.SetNeighbour(pq, pq.subNodes[1]);
                right.SetNeighbour(pq, pq.subNodes[0]);
            }
            else
            {
                pq.subNodes[1].south = pq.south;
                pq.subNodes[0].south = pq.south;
            }

            if (pq.east.subdivision == pq.subdivision && pq.east.isSubdivided)
            {
                pq.east.GetEdgeQuads(pq, out left, out right);
                pq.subNodes[3].east = left;
                pq.subNodes[1].east = right;
                left.SetNeighbour(pq, pq.subNodes[3]);
                right.SetNeighbour(pq, pq.subNodes[1]);
            }
            else
            {
                pq.subNodes[3].east = pq.east;
                pq.subNodes[1].east = pq.east;
            }

            if (pq.west.subdivision == pq.subdivision && pq.west.isSubdivided)
            {
                pq.west.GetEdgeQuads(pq, out left, out right);
                pq.subNodes[0].west = left;
                pq.subNodes[2].west = right;
                left.SetNeighbour(pq, pq.subNodes[0]);
                right.SetNeighbour(pq, pq.subNodes[2]);
            }
            else
            {
                pq.subNodes[0].west = pq.west;
                pq.subNodes[2].west = pq.west;
            }

            if (pq.sphereRoot.reqUVQuad)
            {
                PQ.uvDel = pq.uvDelta * 0.5f;
                PQ.uvMidPoint.x = pq.uvSW.x + PQ.uvDel.x;
                PQ.uvMidPoint.y = pq.uvSW.y + PQ.uvDel.y;
                PQ.uvMidS.x = PQ.uvMidPoint.x;
                PQ.uvMidS.y = pq.uvSW.y;
                PQ.uvMidW.x = pq.uvSW.x;
                PQ.uvMidW.y = PQ.uvMidPoint.y;
                pq.subNodes[0].uvSW = PQ.uvMidPoint;
                pq.subNodes[0].uvDelta = PQ.uvDel;
                pq.subNodes[1].uvSW = PQ.uvMidW;
                pq.subNodes[1].uvDelta = PQ.uvDel;
                pq.subNodes[2].uvSW = PQ.uvMidS;
                pq.subNodes[2].uvDelta = PQ.uvDel;
                pq.subNodes[3].uvSW = pq.uvSW;
                pq.subNodes[3].uvDelta = PQ.uvDel;
            }

            pq.isSubdivided = true;
            pq.SetInvisible();

            for (int i = 0; i < 4; i++)
            {
                PQ subNode = pq.subNodes[i];
                if (subNode.north.IsNullOrDestroyed() || subNode.south.IsNullOrDestroyed() || subNode.east.IsNullOrDestroyed() || subNode.west.IsNullOrDestroyed())
                {
                    Debug.Log("Subdivide: " + pq.gameObject.name + " " + i);
                    Debug.Break();
                }

                if (!subNode.isCached)
                    subNode.SetupQuad(pq, (PQ.QuadChild)i);

                if (pq.sphereRoot.quadAllowBuild)
                    subNode.UpdateVisibility();
            }
            pq.north.QueueForNormalUpdate();
            pq.south.QueueForNormalUpdate();
            pq.east.QueueForNormalUpdate();
            pq.west.QueueForNormalUpdate();

            PQ rightmostCornerPQ = pq.GetRightmostCornerPQ(pq.north);
            if (rightmostCornerPQ.IsNotNullOrDestroyed())
                rightmostCornerPQ.QueueForCornerNormalUpdate();

            rightmostCornerPQ = pq.GetRightmostCornerPQ(pq.west);
            if (rightmostCornerPQ.IsNotNullOrDestroyed())
                rightmostCornerPQ.QueueForCornerNormalUpdate();

            rightmostCornerPQ = pq.GetRightmostCornerPQ(pq.south);
            if (rightmostCornerPQ.IsNotNullOrDestroyed())
                rightmostCornerPQ.QueueForCornerNormalUpdate();

            rightmostCornerPQ = pq.GetRightmostCornerPQ(pq.east);
            if (rightmostCornerPQ.IsNotNullOrDestroyed())
                rightmostCornerPQ.QueueForCornerNormalUpdate();

            return true;
        }
    }

    public class PQSExtension
    {
        private static Dictionary<PQS, PQSExtension> pqsExtensions = new Dictionary<PQS, PQSExtension>();

        public static PQSExtension Get(PQS pqs)
        {
            if (!pqsExtensions.TryGetValue(pqs, out PQSExtension pqsExtension))
            {
                pqsExtension = new PQSExtension(pqs);
                pqsExtensions[pqs] = pqsExtension;
            }

            return pqsExtension;
        }

        private PQS pqs;
        private Timer outOfTimeTimer;
        private bool outOfTime;

        public bool IsOutOfTime => outOfTime;

        public PQSExtension(PQS pqs)
        {
            this.pqs = pqs;
            outOfTimeTimer = new Timer(OnOutOfTime, null, Timeout.Infinite, Timeout.Infinite);
        }

        private void OnOutOfTime(object state)
        {
            outOfTime = true;
        }

        public void StartOutOfTimeTimer()
        {
            outOfTime = false;
            outOfTimeTimer.Change(0, (int)pqs.maxFrameTime);
        }

        public void StopOutOfTimeTimer()
        {
            outOfTime = true;
            outOfTimeTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }
}
