﻿using KSP.Localization;
using System;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    internal class CollisionEnhancerFastUpdate : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(CollisionEnhancer), nameof(CollisionEnhancer.FixedUpdate));
        }

        private static void CollisionEnhancer_FixedUpdate_Override(CollisionEnhancer __instance)
        {
            Part part = __instance.part;
            bool hasPart = part.IsNotNullOrDestroyed();

            Vector3 position;
            if (hasPart)
            {
                position = part.partTransform.position;

                if (part.packed)
                {
                    __instance.lastPos = position;
                    __instance.wasPacked = true;
                    return;
                }
            }
            else
            {
                position = __instance.transform.position;
            }

            if (__instance.framesToSkip > 0)
            {
                __instance.lastPos = position;
                __instance.framesToSkip--;
                return;
            }

            if (hasPart && (part.vessel.IsNullOrDestroyed() || part.vessel.heightFromTerrain > 1000f))
            {
                __instance.lastPos = position;
                return;
            }

            if (!__instance.wasPacked)
                __instance.lastPos -= FloatingOrigin.Offset;
            else
                __instance.wasPacked = false;

            CollisionEnhancerBehaviour mode = __instance.OnTerrainPunchThrough;

            if (mode < CollisionEnhancerBehaviour.COLLIDE // only handle EXPLODE, TRANSLATE_BACK and TRANSLATE_BACK_SPLAT
                && !CollisionEnhancer.bypass
                && (!hasPart || part.State != PartStates.DEAD)
                && (__instance.lastPos - position).sqrMagnitude > CollisionEnhancer.minDistSqr
                && Physics.Linecast(__instance.lastPos, position, out RaycastHit hit, 32768, QueryTriggerInteraction.Ignore)) // linecast against the "LocalScenery" layer
            {
                Vector3 rbVelocity = __instance.rb.velocity;
                string objectName = hasPart ? part.partInfo?.name ?? __instance.name : __instance.name;
                Debug.Log($"[Collision Enhancer] {mode.ToString()} on \"{objectName}\" (vel: {rbVelocity.magnitude.ToString("F0")})");

                if (mode == CollisionEnhancerBehaviour.EXPLODE
                    && !CheatOptions.NoCrashDamage
                    && rbVelocity.sqrMagnitude > part.crashTolerance * part.crashTolerance)
                {
                    GameEvents.onCollision.Fire(new EventReport(FlightEvents.COLLISION, part, part.partInfo.title, Localizer.Format("#autoLOC_204427")));
                    part.explode();
                }
                else
                {
                    Vector3 upAxis = FlightGlobals.getUpAxis(FlightGlobals.currentMainBody, hit.point);

                    if (!hit.point.IsInvalid())
                        __instance.transform.position = hit.point + upAxis * CollisionEnhancer.upFactor;

                    if (mode == CollisionEnhancerBehaviour.TRANSLATE_BACK_SPLAT)
                    {
                        __instance.rb.velocity = Vector3.zero;
                    }
                    else
                    {
                        Vector3 frameVelocity = Krakensbane.GetFrameVelocityV3f();
                        Vector3 totalVelocity = rbVelocity + frameVelocity;
                        float totalVelMagnitude = totalVelocity.magnitude;
                        Vector3 totalVelNormalized = totalVelMagnitude > 1E-05f ? totalVelocity / totalVelMagnitude : Vector3.zero;
                        Vector3 newVel = Vector3.Reflect(totalVelNormalized, hit.normal * Mathf.Sign(Vector3.Dot(hit.normal, upAxis))).normalized * totalVelMagnitude * __instance.translateBackVelocityFactor - frameVelocity;
                        __instance.rb.velocity = newVel.IsInvalid() ? Vector3.zero : newVel;
                    }
                }

                if (hasPart)
                {
                    GameEvents.OnCollisionEnhancerHit.Fire(part, hit);
                    GameEvents.onPartExplodeGroundCollision.Fire(part);
                }
            }

            __instance.lastPos = position;
        }
    }
}
