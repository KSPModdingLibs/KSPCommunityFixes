using HarmonyLib;
using KSPCommunityFixes.Library;
using System;
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

        protected override void ApplyPatches()
        {
            KSPCommunityFixes.Instance.StartCoroutine(ResetSyncOnFixedEnd());

            AddPatch(PatchType.Transpiler, typeof(ModuleEngines), nameof(ModuleEngines.EngineExhaustDamage));

            AddPatch(PatchType.Prefix, typeof(ModuleDeployableSolarPanel), nameof(ModuleDeployableSolarPanel.CalculateTrackingLOS));

            AddPatch(PatchType.Override, typeof(ModuleSurfaceFX), nameof(ModuleSurfaceFX.Update));
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

            int trackingTransformId = __instance.trackingTransformLocal.GetInstanceIDFast();
            int vesselId = __instance.vessel.GetInstanceIDFast();
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

        static void ModuleSurfaceFX_Update_Override(ModuleSurfaceFX msFX)
        {
            if (!HighLogic.LoadedSceneIsFlight || !GameSettings.SURFACE_FX)
                return;

            if (msFX.engineModule != null)
                msFX.fxScale = msFX.engineModule.GetCurrentThrust() / msFX.engineModule.GetMaxThrust() * msFX.fxMax;
            else
                msFX.fxScale = 0f;

            if (msFX.fxScale <= 0f)
            {
                ModuleSurfaceFX_ResetSrfFX(msFX);
                return;
            }

            if (msFX.vessel.radarAltitude > (msFX.vessel.vesselSize.magnitude + msFX.maxDistance) * 2.5f)
            {
                ModuleSurfaceFX_ResetSrfFX(msFX);
                return;
            }

            float transformAltitude = 0f;

            if (msFX.vessel.mainBody.ocean)
            {
                transformAltitude = FlightGlobals.getAltitudeAtPos(msFX.trf.position, msFX.vessel.mainBody);
                if (transformAltitude < 0f)
                {
                    ModuleSurfaceFX_ResetSrfFX(msFX);
                    return;
                }
            }

            msFX.raycastHit = Physics.Raycast(msFX.trf.position, msFX.trf.forward, out msFX.hitInfo, msFX.maxDistance, 1073774592);

            // added the ocean condition here, fixing the bug where the effect wouldn't show up on non-ocean bodies at negative altitudes
            if (msFX.raycastHit && (!msFX.vessel.mainBody.ocean || FlightGlobals.GetSqrAltitude(msFX.hitInfo.point, msFX.vessel.mainBody) >= 0.0))
            {
                if (msFX.hitInfo.collider.CompareTag("LaunchpadFX"))
                {
                    msFX.hit = ModuleSurfaceFX.SurfaceType.Launchpad;
                }
                else if (msFX.hitInfo.collider.CompareTag("Wheel_Piston_Collider"))
                {
                    ModuleSurfaceFX_ResetSrfFX(msFX);
                    return;
                }
                else
                {
                    msFX.hit = ModuleSurfaceFX.SurfaceType.Terrain;
                }
                msFX.point = msFX.hitInfo.point;
                msFX.normal = msFX.hitInfo.normal;
                msFX.distance = msFX.hitInfo.distance;
            }
            else if (msFX.vessel.mainBody.ocean)
            {
                if (transformAltitude > msFX.maxDistance)
                {
                    ModuleSurfaceFX_ResetSrfFX(msFX);
                    return;
                }

                float downDot = Vector3.Dot(msFX.trf.forward, -msFX.vessel.upAxis);
                if (downDot <= 0f)
                {
                    ModuleSurfaceFX_ResetSrfFX(msFX);
                    return;
                }

                msFX.normal = msFX.vessel.upAxis;
                msFX.distance = transformAltitude / downDot;
                msFX.point = msFX.trf.position + msFX.trf.forward * msFX.distance;
                msFX.hit = ModuleSurfaceFX.SurfaceType.Water;
            }
            else
            {
                ModuleSurfaceFX_ResetSrfFX(msFX);
                return;
            }

            msFX.scaledDistance = Mathf.Pow(1f - msFX.distance / msFX.maxDistance, msFX.falloff);
            msFX.ScaledFX = msFX.fxScale * msFX.scaledDistance;
            msFX.rDir = msFX.point - msFX.trf.position;
            msFX.Vsrf = Vector3.ProjectOnPlane(msFX.rDir, msFX.normal).normalized * msFX.fxScale;

            msFX.UpdateSrfFX(msFX.hitInfo);
        }

        static void ModuleSurfaceFX_ResetSrfFX(ModuleSurfaceFX msFX)
        {
            msFX.hit = ModuleSurfaceFX.SurfaceType.None;
            msFX.Vsrf.MutateZero();
            msFX.ScaledFX = 0f;
            msFX.srfFXnext = null;
            msFX.padFX = null;
            if (msFX.srfFX.IsNotNullRef())
            {
                msFX.srfFX.RemoveSource(msFX);
                msFX.srfFX = null;
            }
        }
    }
}
