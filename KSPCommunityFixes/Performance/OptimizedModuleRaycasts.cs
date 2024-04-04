using System;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    internal class OptimizedModuleRaycasts : BasePatch
    {
        private static readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
        private static bool partModulesSyncedOnceInFixedUpdate = false;

        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(
                new PatchInfo(PatchMethodType.Transpiler,
                    AccessTools.Method(typeof(ModuleEngines), nameof(ModuleEngines.EngineExhaustDamage)),
                    this));

            patches.Add(
                new PatchInfo(PatchMethodType.Prefix,
                    AccessTools.Method(typeof(ModuleDeployableSolarPanel), nameof(ModuleDeployableSolarPanel.CalculateTrackingLOS)),
                    this));

            KSPCommunityFixes.Instance.StartCoroutine(ResetSyncOnFixedEnd());
        }

        static IEnumerator ResetSyncOnFixedEnd()
        {
            while (true)
            {
                partModulesSyncedOnceInFixedUpdate = false;
                lastVesselId = 0;
                lastTrackingTransformId = 0;
                yield return waitForFixedUpdate;
            }
        }

        static IEnumerable<CodeInstruction> ModuleEngines_EngineExhaustDamage_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_Physics_RayCast = AccessTools.Method(typeof(Physics), nameof(Physics.Raycast), new[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int) });
            MethodInfo m_RaycastNoSync = AccessTools.Method(typeof(OptimizedModuleRaycasts), nameof(RaycastNoSync));

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(m_Physics_RayCast))
                {
                    instruction.operand = m_RaycastNoSync;
                }

                yield return instruction;
            }
        }

        private static int lastVesselId;
        private static int lastTrackingTransformId;
        private static bool lastHasLoS;
        private static string lastBlocker;

        private static bool ModuleDeployableSolarPanel_CalculateTrackingLOS_Prefix(ModuleDeployableSolarPanel __instance, Vector3 trackingDirection, ref string blocker, out bool __result)
        {
            if (__instance.part.ShieldedFromAirstream && __instance.applyShielding)
            {
                blocker = "aero shielding";
                __result = false;
                return false;
            }

            int trackingTransformId = __instance.trackingTransformLocal.GetInstanceID();
            int vesselId = __instance.vessel.GetInstanceID();
            if (lastTrackingTransformId == trackingTransformId && lastVesselId == vesselId)
            {
                if (!lastHasLoS)
                {
                    __result = false;
                    blocker = lastBlocker;
                    return false;
                }
            }
            else
            {
                lastTrackingTransformId = trackingTransformId;
                lastVesselId = vesselId;

                Vector3 scaledVesselPos = ScaledSpace.LocalToScaledSpace(__instance.vessel.transform.position);
                Vector3 scaledDirection = (ScaledSpace.LocalToScaledSpace(__instance.trackingTransformLocal.position) - scaledVesselPos).normalized;

                if (Physics.Raycast(scaledVesselPos, scaledDirection, out RaycastHit scaledHit, float.MaxValue, __instance.planetLayerMask) && scaledHit.transform.NotDestroyedRefNotEquals(__instance.trackingTransformScaled))
                {
                    __instance.hit = scaledHit; // just to ensure this is populated
                    lastBlocker = scaledHit.transform.gameObject.name; // allocates a string
                    blocker = lastBlocker;
                    lastHasLoS = false;
                    __result = false;
                    return false;
                }

                lastHasLoS = true;
                lastBlocker = null;
            }

            Vector3 localPanelPos = __instance.secondaryTransform.position + trackingDirection * __instance.raycastOffset;
            __result = !RaycastNoSync(localPanelPos, trackingDirection, out RaycastHit localhit, float.MaxValue, __instance.defaultLayerMask);
            __instance.hit = localhit; // just to ensure this is populated

            if (!__result && __instance.part.IsPAWOpen() && localhit.transform.gameObject.IsNotNullOrDestroyed())
            {
                GameObject hitObject = localhit.transform.gameObject;
                if (!ReferenceEquals(hitObject.GetComponent<PQ>(), null))
                {
                    blocker = ModuleDeployableSolarPanel.cacheAutoLOC_438839;
                }
                else
                {
                    Part partUpwardsCached = FlightGlobals.GetPartUpwardsCached(hitObject);
                    if (partUpwardsCached.IsNotNullOrDestroyed())
                    {
                        blocker = partUpwardsCached.partInfo.title;
                    }
                    else
                    {
                        string tag = hitObject.tag; // allocates a string
                        if (tag.Contains("KSC"))
                            blocker = ResearchAndDevelopment.GetMiniBiomedisplayNameByUnityTag(tag, true);
                        else
                            blocker = hitObject.name;
                    }
                }
            }

            return false;
        }

        public static bool RaycastNoSync(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask)
        {
            if (!partModulesSyncedOnceInFixedUpdate)
            {
                Physics.SyncTransforms();
                partModulesSyncedOnceInFixedUpdate = true;
            }

            Physics.autoSyncTransforms = false;
            bool result = Physics.defaultPhysicsScene.Raycast(origin, direction, out hitInfo, maxDistance, layerMask);
            Physics.autoSyncTransforms = true;
            return result;
        }
    }
}
