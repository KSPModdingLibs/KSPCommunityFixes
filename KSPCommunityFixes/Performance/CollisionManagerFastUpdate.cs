// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/174

// Enable profiling or debug tools for this patch :
// #define CMFU_PROFILE
// #define CMFU_DEBUG

using System;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

#if CMFU_DEBUG || CMFU_PROFILE
using System;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;
#endif

namespace KSPCommunityFixes.Performance
{
    public class CollisionManagerFastUpdate : BasePatch
    {
        protected override Version VersionMin => new Version(1, 11, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(
                new PatchInfo(PatchMethodType.Prefix,
                    AccessTools.Method(typeof(CollisionManager), nameof(CollisionManager.UpdatePartCollisionIgnores)),
                    this));
        }

        static bool CollisionManager_UpdatePartCollisionIgnores_Prefix(CollisionManager __instance)
        {
            List<ColliderInfo> colliders = GetAllPartColliders();
            UpdatePartCollisionIgnoresFast(colliders);
            return false;
        }

        internal static void UpdatePartCollisionIgnoresFast(List<ColliderInfo> colliders)
        {
#if CMFU_DEBUG
            fastResultsToVerify.Clear();
            int iterations = 0;
            int pairs = 0;
            int duplicates = 0;
            int mismatches = 0;
#endif
            int colliderCount = colliders.Count;
            for (int i = 0; i < colliderCount; i++)
            {
                ColliderInfo colliderA = colliders[i];

                for (int j = i + 1; j < colliderCount; j++)
                {
#if CMFU_DEBUG
                    iterations++;
#endif
                    ColliderInfo colliderB = colliders[j];

                    if (colliderA.rigidBodyId == colliderB.rigidBodyId)
                        continue;

                    bool ignore = colliderA.vesselId == colliderB.vesselId;
                    if (ignore && colliderA.sameVesselCollision && colliderB.sameVesselCollision && colliderA.partPersistentId != colliderB.partPersistentId)
                        ignore = false;

                    Physics.IgnoreCollision(colliderA.collider, colliderB.collider, ignore);
#if CMFU_DEBUG
                    pairs++;
                    ColliderPair pair = new ColliderPair(colliderA.collider, colliderB.collider);
                    if (fastResultsToVerify.TryGetValue(pair, out bool otherIgnore))
                    {
                        duplicates++;
                        if (otherIgnore != ignore)
                            mismatches++;
                    }
                    fastResultsToVerify[pair] = ignore;
#endif
                }
            }
#if CMFU_DEBUG
            Debug.Log($"[CollisionManagerFastUpdate] [KSPCF results] Colliders : {colliderCount} - Iterations : {iterations} - Checked pairs : {pairs} - Duplicate pairs : {duplicates} - Ignore mismatches in duplicates : {mismatches}");
#endif
        }

        internal static List<ColliderInfo> GetAllPartColliders()
        {
            bool hasLoadedEVAVessel = false;
            int count = FlightGlobals.VesselsLoaded.Count;
            while (count-- > 0)
            {
                if (FlightGlobals.VesselsLoaded[count].isEVA)
                {
                    hasLoadedEVAVessel = true;
                    break;
                }
            }

            HashSet<Collider> colliderHashSet = new HashSet<Collider>(1000);
            List<ColliderInfo> colliders = new List<ColliderInfo>(1000);
            List<Collider> colliderBuffer = new List<Collider>();

            foreach (Vessel vessel in FlightGlobals.VesselsLoaded)
            {
                int vesselId = vessel.GetInstanceID();
                for (int i = vessel.parts.Count; i-- > 0;)
                {
                    Part part = vessel.parts[i];

                    part.partTransform.GetComponentsInChildren(hasLoadedEVAVessel, colliderBuffer);
                    for (int j = colliderBuffer.Count; j-- > 0;)
                    {
                        Collider collider = colliderBuffer[j];

                        if (colliderHashSet.Contains(collider))
                            continue;

                        if ((collider.gameObject.activeInHierarchy && collider.enabled) || (hasLoadedEVAVessel && (collider.CompareTag("Ladder") || collider.CompareTag("Airlock"))))
                        {
                            colliders.Add(new ColliderInfo(collider, vesselId, part.persistentId, part.sameVesselCollision));
                            colliderHashSet.Add(collider);
                        }
                    }
                }
            }

            return colliders;
        }

        internal struct ColliderInfo
        {
            public uint partPersistentId;
            public int rigidBodyId;
            public int vesselId;
            public bool sameVesselCollision;
            public Collider collider;

            public ColliderInfo(Collider collider, int vesselId, uint partPersistentId, bool sameVesselCollision)
            {
                this.collider = collider;
                this.partPersistentId = partPersistentId;
                this.sameVesselCollision = sameVesselCollision;
                this.vesselId = vesselId;
                rigidBodyId = collider.attachedRigidbody.IsNullOrDestroyed() ? 0 : collider.attachedRigidbody.GetInstanceID();
            }
        }

#if CMFU_DEBUG
        internal static Dictionary<ColliderPair, bool> fastResultsToVerify = new Dictionary<ColliderPair, bool>();

        internal static void VerifyPartCollisionIgnores()
        {
            Debug.Log("[CollisionManagerFastUpdate] : VERIFYING RESULTS...");

            List <ColliderInfo> colliders = GetAllPartColliders();
            UpdatePartCollisionIgnoresFast(colliders);

            Dictionary<ColliderPair, bool> stockResultsToVerify = new Dictionary<ColliderPair, bool>();
            int iterations = 0;
            int pairs = 0;
            int duplicates = 0;
            int mismatches = 0;
            int colliderCount = 0;


            List<CollisionManager.VesselColliderList> allVesselColliders = CollisionManager.Instance.GetAllVesselColliders();

            foreach (CollisionManager.VesselColliderList vesselColliderList in allVesselColliders)
                foreach (CollisionManager.PartColliderList partColliderList in vesselColliderList.colliderList)
                    colliderCount += partColliderList.colliders.Count;

            int i = 0;
            for (int count = allVesselColliders.Count; i < count; i++)
            {
                int j = i;
                for (int count2 = allVesselColliders.Count; j < count2; j++)
                {
                    List<CollisionManager.PartColliderList> colliderList = allVesselColliders[i].colliderList;
                    List<CollisionManager.PartColliderList> colliderList2 = allVesselColliders[j].colliderList;
                    bool flag = i == j;
                    int k = 0;
                    for (int count3 = colliderList.Count; k < count3; k++)
                    {
                        int l = (flag ? (k + 1) : 0);
                        for (int count4 = colliderList2.Count; l < count4; l++)
                        {
                            int m = 0;
                            for (int count5 = colliderList[k].colliders.Count; m < count5; m++)
                            {
                                int n = 0;
                                for (int count6 = colliderList2[l].colliders.Count; n < count6; n++)
                                {
                                    iterations++;
                                    Collider collider = colliderList[k].colliders[m];
                                    Collider collider2 = colliderList2[l].colliders[n];
                                    if (!(collider.attachedRigidbody == collider2.attachedRigidbody))
                                    {
                                        bool ignore;
                                        if ((ignore = flag) && colliderList[k].sameVesselCollision && colliderList2[l].sameVesselCollision && colliderList[k].partPersistentId != colliderList2[l].partPersistentId)
                                        {
                                            ignore = false;
                                        }

                                        pairs++;
                                        ColliderPair pair = new ColliderPair(collider, collider2);
                                        if (stockResultsToVerify.TryGetValue(pair, out bool otherIgnore))
                                        {
                                            duplicates++;
                                            if (otherIgnore != ignore)
                                                mismatches++;
                                        }
                                        stockResultsToVerify[pair] = ignore;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            allVesselColliders.Clear();

            Debug.Log($"[CollisionManagerFastUpdate] [Stock results] Colliders : {colliderCount} - Iterations : {iterations} - Checked pairs : {pairs} - Duplicate pairs : {duplicates} - Ignore mismatches in duplicates : {mismatches}");

            int fastMissingPairs = 0;
            int fastAdditionalPairs = 0;
            int fastResultMismatchs = 0;

            foreach (KeyValuePair<ColliderPair, bool> colliderPair in stockResultsToVerify)
            {
                if (fastResultsToVerify.TryGetValue(colliderPair.Key, out bool fastResult))
                {
                    if (fastResult != colliderPair.Value)
                        fastResultMismatchs++;
                }
                else
                {
                    fastMissingPairs++;
                }
            }

            foreach (KeyValuePair<ColliderPair, bool> colliderPair in fastResultsToVerify)
            {
                if (stockResultsToVerify.TryGetValue(colliderPair.Key, out bool stockResult))
                {
                    if (stockResult != colliderPair.Value)
                        fastResultMismatchs++;
                }
                else
                {
                    fastAdditionalPairs++;
                }
            }

            Debug.Log($"[CollisionManagerFastUpdate] [Verify] Stock pairs : {stockResultsToVerify.Count} - KSPCF pairs : {fastResultsToVerify.Count} - KSPCF missing pairs : {fastMissingPairs} - KSPCF additional pairs : {fastAdditionalPairs} - Results mismatches : {fastResultMismatchs}");
        }

        internal struct ColliderPair
        {
            public Collider a;
            public Collider b;

            public ColliderPair(Collider a, Collider b)
            {
                this.a = a;
                this.b = b;
            }

            public override int GetHashCode() => a.GetHashCode() + b.GetHashCode();
            public bool Equals(ColliderPair p) => (a == p.a && b == p.b) || (a == p.b && b == p.a);
            public override bool Equals(object obj) => obj is ColliderPair other && Equals(other);
            public static bool operator ==(ColliderPair lhs, ColliderPair rhs) => lhs.Equals(rhs);
            public static bool operator !=(ColliderPair lhs, ColliderPair rhs) => !lhs.Equals(rhs);
        }
#endif
    }

#if CMFU_DEBUG || CMFU_PROFILE
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CollisionManagerProfiler : MonoBehaviour
    {
        private bool fastIsCollecting;
        private bool stockIsCollecting;
        private bool fastRequireUpdate;
        private bool stockRequireUpdate;
        private Stopwatch fastSetupWatch = new Stopwatch();
        private Stopwatch fastMainWatch = new Stopwatch();
        private Stopwatch stockSetupWatch = new Stopwatch();
        private Stopwatch stockMainWatch = new Stopwatch();
        private int j = 0;

        private string profileFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CollisionManagerProfiler.txt");

        void Update()
        {
            if (fastIsCollecting)
            {
                if (j > 100)
                {
                    fastIsCollecting = false;
                    double mainTime = fastMainWatch.Elapsed.TotalMilliseconds / 100.0;
                    double setupTime = fastSetupWatch.Elapsed.TotalMilliseconds / 100.0;
                    fastMainWatch.Reset();
                    fastSetupWatch.Reset();
                    if (!File.Exists(profileFilePath)) File.Create(profileFilePath);
                    using (StreamWriter sw = File.AppendText(profileFilePath))
                        sw.WriteLine($"[KSPCF IMPL] {mainTime:F3} ms (setup = {setupTime:F3} ms)");
                }
                else
                {
                    fastRequireUpdate = true;
                }
            }

            if (stockIsCollecting)
            {
                if (j > 100)
                {
                    stockIsCollecting = false;
                    double mainTime = stockMainWatch.Elapsed.TotalMilliseconds / 100.0;
                    double setupTime = stockSetupWatch.Elapsed.TotalMilliseconds / 100.0;
                    stockMainWatch.Reset();
                    stockSetupWatch.Reset();

                    if (!File.Exists(profileFilePath)) File.Create(profileFilePath);
                    using (StreamWriter sw = File.AppendText(profileFilePath))
                        sw.WriteLine($"[STOCK IMPL] {mainTime:F3} ms (setup = {setupTime:F3} ms)");
                }
                else
                {
                    stockRequireUpdate = true;
                }
            }
        }

        void FixedUpdate()
        {
            if (fastRequireUpdate)
            {
                fastRequireUpdate = false;
                j++;
                fastMainWatch.Start();
                fastSetupWatch.Start();
                List<CollisionManagerFastUpdate.ColliderInfo> colliders = CollisionManagerFastUpdate.GetAllPartColliders();
                fastSetupWatch.Stop();
                CollisionManagerFastUpdate.UpdatePartCollisionIgnoresFast(colliders);
                fastMainWatch.Stop();
            }

            if (stockRequireUpdate)
            {
                stockRequireUpdate = false;
                j++;
                stockMainWatch.Start();
                stockSetupWatch.Start();
                List<CollisionManager.VesselColliderList> allVesselColliders = CollisionManager.Instance.GetAllVesselColliders();
                stockSetupWatch.Stop();
                UpdatePartCollisionIgnoresStock(allVesselColliders);
                stockMainWatch.Stop();
            }
        }

        void OnGUI()
        {
            if (GUI.Button(new Rect(10, 100, 150, 20), stockIsCollecting ? "Profiling stock..." : "Profile stock"))
            {
                if (!stockIsCollecting)
                {
                    stockMainWatch.Reset();
                    stockSetupWatch.Reset();
                    stockIsCollecting = true;
                    j = 0;
                }
            }

            if (GUI.Button(new Rect(10, 120, 150, 20), fastIsCollecting ? "Profiling KSPCF..." : "Profile KSPCF"))
            {
                if (!fastIsCollecting)
                {
                    fastMainWatch.Reset();
                    fastSetupWatch.Reset();
                    fastIsCollecting = true;
                    j = 0;
                }
            }

#if CMFU_DEBUG
            if (GUI.Button(new Rect(10, 140, 150, 20), "Verify results"))
            {
                CollisionManagerFastUpdate.VerifyPartCollisionIgnores();
            }
#endif
        }

        static void UpdatePartCollisionIgnoresStock(List<CollisionManager.VesselColliderList> allVesselColliders)
        {
            int i = 0;
            for (int count = allVesselColliders.Count; i < count; i++)
            {
                int j = i;
                for (int count2 = allVesselColliders.Count; j < count2; j++)
                {
                    List<CollisionManager.PartColliderList> colliderList = allVesselColliders[i].colliderList;
                    List<CollisionManager.PartColliderList> colliderList2 = allVesselColliders[j].colliderList;
                    bool flag = i == j;
                    int k = 0;
                    for (int count3 = colliderList.Count; k < count3; k++)
                    {
                        int l = (flag ? (k + 1) : 0);
                        for (int count4 = colliderList2.Count; l < count4; l++)
                        {
                            int m = 0;
                            for (int count5 = colliderList[k].colliders.Count; m < count5; m++)
                            {
                                int n = 0;
                                for (int count6 = colliderList2[l].colliders.Count; n < count6; n++)
                                {
                                    Collider collider = colliderList[k].colliders[m];
                                    Collider collider2 = colliderList2[l].colliders[n];
                                    if (!(collider.attachedRigidbody == collider2.attachedRigidbody))
                                    {
                                        bool ignore;
                                        if ((ignore = flag) && colliderList[k].sameVesselCollision && colliderList2[l].sameVesselCollision && colliderList[k].partPersistentId != colliderList2[l].partPersistentId)
                                        {
                                            ignore = false;
                                        }
                                        Physics.IgnoreCollision(collider, collider2, ignore);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            allVesselColliders.Clear();
        }
    }
#endif
        }
