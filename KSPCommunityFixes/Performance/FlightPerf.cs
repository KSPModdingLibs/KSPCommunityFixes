// Enable cross checking our implementations results with the stock implementations results
// Warning : very log-spammy and performance destroying, don't leave this enabled if you don't need to.
// #define DEBUG_FLIGHTINTEGRATOR

/* Perf comparison (patch disabled vs enabled)
| FPS                                     | Mean  | Mean (P) | Diff | Worst 1% | Worst 1% (P) | Diff |
| --------------------------------------- | ----- | -------- | ---- | -------- | ------------ | ---- |
| Acapello (150 parts) - Launchpad        | 238,7 | 247,8    | 4%   | 162,1    | 175,6        | 8%   |
| Acapello (150 parts) - Atmo flight      | 237,1 | 250      | 5%   | 140,7    | 153,5        | 9%   |
| Acapello (150 parts) - Orbit            | 287,3 | 312,9    | 9%   | 129,5    | 181,5        | 40%  |
| SSTO (500 parts) - Launchpad            | 98,7  | 116,2    | 18%  | 73,1     | 96,1         | 31%  |
| SSTO (500 parts) - Atmo flight          | 85,1  | 100,4    | 18%  | 54,8     | 68,8         | 26%  |
| SSTO (500 parts) - Orbit                | 118,4 | 153      | 29%  | 70       | 85,9         | 23%  |
| Big launcher (1000 parts) - Launchpad   | 62,2  | 88,4     | 42%  | 39,2     | 70,5         | 80%  |
| Big launcher (1000 parts) - Atmo flight | 28,2  | 59,4     | 111% | 18,9     | 35           | 85%  |
| Big launcher (1000 parts) - Orbit       | 56,3  | 90,4     | 61%  | 27,9     | 39,9         | 43%  |
*/

using CompoundParts;
using HarmonyLib;
using Highlighting;
using KSP.Localization;
using KSP.UI.Screens.Flight;
using KSPCommunityFixes.Library;
using KSPCommunityFixes.Library.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;
using static DragCubeList;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.Performance
{
    internal class FlightPerf : BasePatch
    {
        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusionSolar)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusionBody)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateOcclusionConvection)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.UpdateMassStats)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(VesselPrecalculate), nameof(VesselPrecalculate.CalculatePhysicsStats)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.Integrate)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.isKerbalEVA)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.FindNodeApproaches)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.OnLoad)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(CModuleLinkedMesh), nameof(CModuleLinkedMesh.TrackAnchor)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.PrecalcRadiation)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.OnDestroy)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Highlighter), nameof(Highlighter.UpdateRenderers)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(CollisionEnhancer), nameof(CollisionEnhancer.FixedUpdate)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(VolumeNormalizer), nameof(VolumeNormalizer.Update)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(TemperatureGaugeSystem), nameof(TemperatureGaugeSystem.Update)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(FloatingOrigin), nameof(FloatingOrigin.setOffset)),
                this));
        }

        private static HashSet<int> activePS = new HashSet<int>(200);

        private static bool FloatingOrigin_setOffset_Prefix(FloatingOrigin __instance, Vector3d refPos, Vector3d nonFrame)
        {
            if (refPos.IsInvalid())
                return false;

            if (double.IsInfinity(refPos.sqrMagnitude))
                return false;

            __instance.SetOffsetThisFrame = true;
            __instance.offset = refPos;
            __instance.reverseoffset = new Vector3d(0.0 - refPos.x, 0.0 - refPos.y, 0.0 - refPos.z);
            __instance.offsetNonKrakensbane = __instance.offset + nonFrame;

            Vector3 offsetF = __instance.offset;
            Vector3 offsetNonKrakensbaneF = __instance.offsetNonKrakensbane;
            float deltaTime = Time.deltaTime;
            Vector3 frameVelocity = Krakensbane.GetFrameVelocity();

            activePS.Clear();

            List<CelestialBody> bodies = FlightGlobals.Bodies;
            for (int i = bodies.Count; i-- > 0;)
                bodies[i].position -= __instance.offsetNonKrakensbane;

            bool needCoMRecalc = __instance.offset.sqrMagnitude > __instance.CoMRecalcOffsetMaxSqr;
            List<Vessel> vessels = FlightGlobals.Vessels;

            for (int i = vessels.Count; i-- > 0;)
            {
                Vessel vessel = vessels[i];

                if (vessel.state == Vessel.State.DEAD)
                    continue;

                Vector3d vesselOffset = (!vessel.loaded || vessel.packed || vessel.LandedOrSplashed) ? __instance.offsetNonKrakensbane : __instance.offset;
                vessel.SetPosition((Vector3d)vessel.transform.position - vesselOffset);

                if (needCoMRecalc && vessel.packed)
                {
                    vessel.precalc.CalculatePhysicsStats();
                }
                else
                {
                    vessel.CoMD -= vesselOffset;
                    vessel.CoM = vessel.CoMD;
                }

                // Update legacy (?) particle system
                for (int j = vessel.parts.Count; j-- > 0;)
                {
                    Part part = vessel.parts[j];

                    if (part.fxGroups.Count == 0)
                        continue;

                    bool partDataComputed = false;
                    bool hasRigidbody = false;
                    Vector3 partVelocity = Vector3.zero;

                    for (int k = part.fxGroups.Count; k-- > 0;)
                    {
                        FXGroup fXGroup = part.fxGroups[k];
                        for (int l = fXGroup.fxEmittersNewSystem.Count; l-- > 0;)
                        {
                            ParticleSystem particleSystem = fXGroup.fxEmittersNewSystem[l];
                            
                            int particleCount = particleSystem.particleCount;
                            if (particleCount == 0 || particleSystem.main.simulationSpace != ParticleSystemSimulationSpace.World)
                                continue;

                            activePS.Add(particleSystem.GetInstanceIDFast());

                            if (!partDataComputed)
                            {
                                partDataComputed = true;
                                Rigidbody partRB = part.Rigidbody;
                                if (partRB.IsNotNullOrDestroyed())
                                {
                                    hasRigidbody = true;
                                    partVelocity = partRB.velocity + frameVelocity;
                                }
                            }

                            NativeArray<ParticleSystem.Particle> particleBuffer = particleSystem.GetParticlesNativeArray(ref particleCount);
                            for (int pIdx = particleCount; pIdx-- > 0;)
                            {
                                Vector3 particlePos = particleBuffer.GetParticlePosition(pIdx);

                                if (hasRigidbody)
                                {
                                    float scalar = UnityEngine.Random.value * deltaTime;
                                    particlePos.Substract(partVelocity.x * scalar, partVelocity.y * scalar, partVelocity.z * scalar);
                                }

                                particlePos.Substract(offsetNonKrakensbaneF);
                                particleBuffer.SetParticlePosition(pIdx, particlePos);
                            }
                            particleSystem.SetParticles(particleBuffer, particleCount);
                        }
                    }
                }
            }

            // update "new" (but just as shitty) particle system (this replicate a call to EffectBehaviour.OffsetParticles())
            Vector3 systemVelocity = Vector3.zero;

            List<ParticleSystem> pSystems = EffectBehaviour.emitters;
            for (int i = pSystems.Count; i-- > 0;)
            {
                ParticleSystem particleSystem = pSystems[i];

                if (particleSystem.IsNullOrDestroyed())
                {
                    pSystems.RemoveAt(i);
                    continue;
                }

                int particleCount = particleSystem.particleCount;
                if (particleCount == 0 || particleSystem.main.simulationSpace != ParticleSystemSimulationSpace.World)
                    continue;

                activePS.Add(particleSystem.GetInstanceIDFast());

                bool hasRigidbody = false;
                Rigidbody rb = particleSystem.GetComponentInParent<Rigidbody>();
                if (rb.IsNotNullRef())
                {
                    hasRigidbody = true;
                    systemVelocity = rb.velocity + frameVelocity;
                }

                NativeArray<ParticleSystem.Particle> particleBuffer = particleSystem.GetParticlesNativeArray(ref particleCount);
                for (int pIdx = particleCount; pIdx-- > 0;)
                {
                    Vector3 particlePos = particleBuffer.GetParticlePosition(pIdx);

                    if (hasRigidbody)
                    {
                        float scalar = UnityEngine.Random.value * deltaTime;
                        particlePos.Substract(systemVelocity.x * scalar, systemVelocity.y * scalar, systemVelocity.z * scalar);
                    }

                    particlePos.Substract(offsetNonKrakensbaneF);
                    particleBuffer.SetParticlePosition(pIdx, particlePos);
                }
                particleSystem.SetParticles(particleBuffer, particleCount);
            }

            List<KSPParticleEmitter> pSystemsKSP = EffectBehaviour.kspEmitters;
            for (int i = pSystemsKSP.Count; i-- > 0;)
            {
                KSPParticleEmitter particleSystemKSP = pSystemsKSP[i];

                if (particleSystemKSP.IsNullOrDestroyed())
                {
                    pSystemsKSP.RemoveAt(i);
                    continue;
                }

                int particleCount = particleSystemKSP.ps.particleCount;
                if (particleCount == 0 || !particleSystemKSP.useWorldSpace)
                    continue;

                activePS.Add(particleSystemKSP.ps.GetInstanceIDFast());

                bool hasRigidbody = false;
                Rigidbody rb = particleSystemKSP.GetComponentInParent<Rigidbody>();
                if (rb.IsNotNullRef())
                {
                    hasRigidbody = true;
                    systemVelocity = rb.velocity + frameVelocity;
                }

                NativeArray<ParticleSystem.Particle> particleBuffer = particleSystemKSP.ps.GetParticlesNativeArray(ref particleCount);
                for (int pIdx = particleCount; pIdx-- > 0;)
                {
                    Vector3 particlePos = particleBuffer.GetParticlePosition(pIdx);

                    if (hasRigidbody)
                    {
                        float scalar = UnityEngine.Random.value * deltaTime;
                        particlePos.Substract(systemVelocity.x * scalar, systemVelocity.y * scalar, systemVelocity.z * scalar);
                    }

                    particlePos.Substract(offsetNonKrakensbaneF);
                    particleBuffer.SetParticlePosition(pIdx, particlePos);
                }
                particleSystemKSP.ps.SetParticles(particleBuffer, particleCount);
            }

            // Just have another handling of the same stuff, sometimes overlapping, sometimes not, because why not ?
            for (int i = __instance.particleSystems.Count; i-- > 0;)
            {
                ParticleSystem particleSystem = __instance.particleSystems[i];
                if (particleSystem.IsNullOrDestroyed() || activePS.Contains(particleSystem.GetInstanceIDFast()))
                {
                    __instance.particleSystems.RemoveAt(i);
                    continue;
                }

                int particleCount = particleSystem.particleCount;
                if (particleCount == 0)
                    continue;

                if (particleSystem.main.simulationSpace != ParticleSystemSimulationSpace.World)
                    continue;

                if (activePS.Contains(particleSystem.GetInstanceIDFast()))
                {
                    __instance.particleSystems.RemoveAt(i);
                    continue;
                }

                NativeArray<ParticleSystem.Particle> particleBuffer = particleSystem.GetParticlesNativeArray(ref particleCount);
                for (int pIdx = particleCount; pIdx-- > 0;)
                {
                    Vector3 particlePos = particleBuffer.GetParticlePosition(pIdx);
                    particlePos.Substract(offsetNonKrakensbaneF);
                    particleBuffer.SetParticlePosition(pIdx, particlePos);
                }

                particleSystem.SetParticles(particleBuffer, particleCount);
            }

            // more particle system (explosions, fireworks...) moving in here, but this is getting silly, I don't care anymore...
            if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.radarAltitude < __instance.altToStopMovingExplosions)
                FXMonger.OffsetPositions(-__instance.offsetNonKrakensbane);

            for (int i = FlightGlobals.physicalObjects.Count; i-- > 0;)
            {
                physicalObject physicalObject = FlightGlobals.physicalObjects[i];
                if (physicalObject.IsNotNullOrDestroyed())
                {
                    Transform obj = physicalObject.transform;
                    obj.position -= offsetF;
                }
            }

            FloatingOrigin.TerrainShaderOffset += __instance.offsetNonKrakensbane;
            GameEvents.onFloatingOriginShift.Fire(__instance.offset, nonFrame);

            return false;
        }

        private static bool TemperatureGaugeSystem_Update_Prefix(TemperatureGaugeSystem __instance)
        {
            if (!FlightGlobals.ready || !HighLogic.LoadedSceneIsFlight)
                return false;

            if (GameSettings.TEMPERATURE_GAUGES_MODE < 1 || CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight)
            {
                if (__instance.gaugeCount > 0)
                    __instance.DestroyGauges();

                return false;
            }

            if (__instance.TestRecreate())
                __instance.CreateGauges();

            if (__instance.TestRebuild())
                __instance.RebuildGaugeList();

            if (__instance.gaugeCount == 0)
                return false;

            __instance.visibleGauges.Clear();
            for (int i = __instance.gaugeCount; i-- > 0;)
            {
                TemperatureGauge gauge = __instance.gauges[i];
                gauge.GaugeUpdate();
                if (gauge.gaugeActive)
                    __instance.visibleGauges.Add(gauge);
            }

            __instance.visibleGaugeCount = __instance.visibleGauges.Count;

            if (__instance.visibleGaugeCount > 0)
            {
                __instance.visibleGauges.Sort();

                for (int i = 0; i < __instance.visibleGaugeCount; i++)
                    __instance.visibleGauges[i].rTrf.SetSiblingIndex(i);
            }
            return false;
        }

        private static bool VolumeNormalizer_Update_Prefix(VolumeNormalizer __instance)
        {
            float newVolume;
            if (GameSettings.SOUND_NORMALIZER_ENABLED)
            {
                __instance.threshold = GameSettings.SOUND_NORMALIZER_THRESHOLD;
                __instance.sharpness = GameSettings.SOUND_NORMALIZER_RESPONSIVENESS;
                AudioListener.GetOutputData(__instance.samples, 0);
                __instance.level = 0f;

                for (int i = 0; i < __instance.sampleCount; i += 1 + GameSettings.SOUND_NORMALIZER_SKIPSAMPLES)
                    __instance.level = Mathf.Max(__instance.level, Mathf.Abs(__instance.samples[i]));

                if (__instance.level > __instance.threshold)
                    newVolume = __instance.threshold / __instance.level;
                else
                    newVolume = 1f;

                newVolume = Mathf.Lerp(AudioListener.volume, newVolume * GameSettings.MASTER_VOLUME, __instance.sharpness * Time.deltaTime);
            }
            else
            {
                newVolume = Mathf.Lerp(AudioListener.volume, GameSettings.MASTER_VOLUME, __instance.sharpness * Time.deltaTime);
            }

            if (newVolume != __instance.volume)
                AudioListener.volume = newVolume;

            __instance.volume = newVolume;

            return false;

        }

        private static bool CollisionEnhancer_FixedUpdate_Prefix(CollisionEnhancer __instance)
        {
            Part part = __instance.part;
            Vector3 position = part.partTransform.position;

            if (part.packed)
            {
                __instance.lastPos = position;
                __instance.wasPacked = true;
                return false;
            }

            if (__instance.framesToSkip > 0)
            {
                __instance.lastPos = position;
                __instance.framesToSkip--;
                return false;
            }

            if (part.vessel.heightFromTerrain > 1000f)
            {
                __instance.lastPos = position;
                return false;
            }

            if (!__instance.wasPacked)
                __instance.lastPos -= FloatingOrigin.Offset;
            else
                __instance.wasPacked = false;

            CollisionEnhancerBehaviour mode = __instance.OnTerrainPunchThrough;

            if (mode < CollisionEnhancerBehaviour.COLLIDE // only handle EXPLODE, TRANSLATE and TRANSLATE_BACK_SPLAT
                && !CollisionEnhancer.bypass 
                && part.State != PartStates.DEAD 
                && (__instance.lastPos - position).sqrMagnitude > CollisionEnhancer.minDistSqr
                && Physics.Linecast(__instance.lastPos, position, out RaycastHit hit, 32768, QueryTriggerInteraction.Ignore)) // linecast against the "LocalScenery" layer
            {
                Vector3 rbVelocity = __instance.rb.velocity;
                Debug.Log("[F: " + Time.frameCount + "]: [" + __instance.name + "] Collision Enhancer Punch Through - vel: " + rbVelocity.magnitude, __instance.gameObject);

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
                GameEvents.OnCollisionEnhancerHit.Fire(part, hit);
                GameEvents.onPartExplodeGroundCollision.Fire(part);
            }

            __instance.lastPos = position;

            return false;
        }

        private static void Part_OnDestroy_Postfix(Part __instance)
        {
            if (__instance.hl.IsNullOrDestroyed())
                return;

            Highlighter hl = __instance.hl;
            if (hl.highlightableRenderers != null)
            {
                for (int i = hl.highlightableRenderers.Count; i-- > 0;)
                    hl.highlightableRenderers[i].CleanUp();

                hl.highlightableRenderers.Clear();
            }

            hl.renderersDirty = true;
        }

        private static bool Highlighter_UpdateRenderers_Prefix(Highlighter __instance, out bool __result)
        {
            __result = __instance.renderersDirty;
            if (__result)
            {
                List<Renderer> renderers = new List<Renderer>();
                __instance.GrabRenderers(__instance.tr, ref renderers);
                __instance.highlightableRenderers = new List<Highlighter.RendererCache>();
                int count = renderers.Count;
                for (int i = 0; i < count; i++)
                {
                    Highlighter.RendererCache item = new Highlighter.RendererCache(renderers[i], __instance.opaqueMaterial, __instance.zTestFloat, __instance.stencilRefFloat);
                    __instance.highlightableRenderers.Add(item);
                }
                __instance.highlighted = false;
                __instance.renderersDirty = false;
                __instance.currentColor = Color.clear;
            }
            return false;
        }

        private static bool FlightIntegrator_PrecalcRadiation_Prefix(FlightIntegrator __instance, PartThermalData ptd)
        {
            Part part = ptd.part;
            double num = __instance.cacheRadiationFactor * 0.001;
            ptd.emissScalar = part.emissiveConstant * num;
            ptd.absorbScalar = part.absorptiveConstant * num;
            ptd.sunFlux = 0.0;
            ptd.bodyFlux = __instance.bodyEmissiveFlux + __instance.bodyAlbedoFlux;
            double num2 = part.radiativeArea * (1.0 - part.skinExposedAreaFrac);
            ptd.expFlux = 0.0;
            ptd.unexpFlux = 0.0;
            ptd.brtUnexposed = __instance.backgroundRadiationTemp;
            ptd.brtExposed = Numerics.Lerp(__instance.backgroundRadiationTemp, __instance.backgroundRadiationTempExposed, ptd.convectionTempMultiplier);

            if (part.DragCubes.None || part.ShieldedFromAirstream)
                return false;

            bool computeSunFlux = __instance.vessel.directSunlight;
            bool computeBodyFlux = ptd.bodyFlux > 0.0;

            if (!computeSunFlux && !computeBodyFlux)
                return false;

            float[] dragCubeAreaOccluded = part.DragCubes.areaOccluded;

            if (computeSunFlux)
            {
                Vector3 sunLocalDir = part.partTransform.InverseTransformDirection(__instance.sunVector);

                double sunArea = 0.0;
                if (sunLocalDir.x > 0.0)
                    sunArea += dragCubeAreaOccluded[0] * sunLocalDir.x; // right
                else
                    sunArea += dragCubeAreaOccluded[1] * -sunLocalDir.x; // left

                if (sunLocalDir.y > 0.0)
                    sunArea += dragCubeAreaOccluded[2] * sunLocalDir.y; // up
                else
                    sunArea += dragCubeAreaOccluded[3] * -sunLocalDir.y; // down

                if (sunLocalDir.z > 0.0)
                    sunArea += dragCubeAreaOccluded[4] * sunLocalDir.z; // forward
                else
                    sunArea += dragCubeAreaOccluded[5] * -sunLocalDir.z; // back

                sunArea *= ptd.sunAreaMultiplier;

                if (sunArea > 0.0)
                {
                    ptd.sunFlux = ptd.absorbScalar * __instance.solarFlux;
                    if (ptd.exposed)
                    {
                        double num3 = ((double)Vector3.Dot(__instance.sunVector, __instance.nVel) + 1.0) * 0.5;
                        double num4 = Math.Min(sunArea, part.skinExposedArea * num3);
                        double num5 = Math.Min(sunArea - num4, num2 * (1.0 - num3));
                        ptd.expFlux += ptd.sunFlux * num4;
                        ptd.unexpFlux += ptd.sunFlux * num5;
                    }
                    else
                    {
                        ptd.expFlux += ptd.sunFlux * sunArea;
                    }
                }
            }

            if (computeBodyFlux)
            {
                Vector3 bodyLocalDir = part.partTransform.InverseTransformDirection(-__instance.vessel.upAxis);

                double bodyArea = 0.0;
                if (bodyLocalDir.x > 0.0)
                    bodyArea += dragCubeAreaOccluded[0] * bodyLocalDir.x; // right
                else
                    bodyArea += dragCubeAreaOccluded[1] * -bodyLocalDir.x; // left

                if (bodyLocalDir.y > 0.0)
                    bodyArea += dragCubeAreaOccluded[2] * bodyLocalDir.y; // up
                else
                    bodyArea += dragCubeAreaOccluded[3] * -bodyLocalDir.y; // down

                if (bodyLocalDir.z > 0.0)
                    bodyArea += dragCubeAreaOccluded[4] * bodyLocalDir.z; // forward
                else
                    bodyArea += dragCubeAreaOccluded[5] * -bodyLocalDir.z; // back

                bodyArea *= ptd.bodyAreaMultiplier;

                if (bodyArea > 0.0)
                {
                    ptd.bodyFlux = UtilMath.Lerp(0.0, ptd.bodyFlux, __instance.densityThermalLerp) * ptd.absorbScalar;
                    if (ptd.exposed)
                    {
                        double num6 = (Vector3.Dot(-__instance.vessel.upAxis, __instance.nVel) + 1f) * 0.5f;
                        double num7 = Math.Min(bodyArea, part.skinExposedArea * num6);
                        double num8 = Math.Min(bodyArea - num7, num2 * (1.0 - num6));
                        ptd.expFlux += ptd.bodyFlux * num7;
                        ptd.unexpFlux += ptd.bodyFlux * num8;
                    }
                    else
                    {
                        ptd.expFlux += ptd.bodyFlux * bodyArea;
                    }
                }
            }

            return false;
        }

        private static bool CModuleLinkedMesh_TrackAnchor_Prefix(CModuleLinkedMesh __instance, bool setTgtAnchor, Vector3 rDir, Vector3 rPos, Quaternion rRot)
        {
            CModuleLinkedMesh st = __instance;

            if (st.targetAnchor.IsNotNullOrDestroyed())
            {
                if (!st.tweakingTarget && !st.part.PartTweakerSelected)
                {
                    if (setTgtAnchor && st.compoundPart.IsNotNullOrDestroyed() && st.compoundPart.transform.IsNotNullOrDestroyed())
                    {
                        st.targetAnchor.position = st.compoundPart.transform.TransformPoint(rPos);
                        st.targetAnchor.rotation = st.compoundPart.transform.rotation * rRot;
                    }
                }
                else
                {
                    st.compoundPart.targetPosition = st.transform.InverseTransformPoint(st.targetAnchor.position);
                    st.compoundPart.targetRotation = st.targetAnchor.localRotation;
                    st.compoundPart.UpdateWorldValues();
                }
            }

            bool lineRotationComputed = false;
            Quaternion lineRotation = default;
            if (st.endCap.IsNotNullOrDestroyed())
            {
                Vector3 vector = st.line.position - st.endCap.position;
                float magnitude = vector.magnitude;
                if (magnitude != 0f)
                {
                    if (magnitude < st.lineMinimumLength)
                    {
                        st.line.gameObject.SetActive(false);
                    }
                    else
                    {
                        st.line.gameObject.SetActive(true);
                        lineRotation = Quaternion.LookRotation(vector / magnitude, st.transform.forward);
                        lineRotationComputed = true;
                        st.line.rotation = lineRotation;
                        Vector3 localScale = st.line.localScale;
                        st.line.localScale = new Vector3(localScale.x, localScale.y, magnitude * st.part.scaleFactor);
                        st.endCap.rotation = lineRotation;
                    }
                }
            }
            else if (st.transform.IsNotNullOrDestroyed() && st.targetAnchor.IsNotNullOrDestroyed())
            {
                Vector3 vector = st.transform.position - st.targetAnchor.position;
                float magnitude = vector.magnitude;
                if (magnitude != 0f)
                {
                    if (magnitude < st.lineMinimumLength)
                    {
                        st.line.gameObject.SetActive(false);
                    }
                    else
                    {
                        st.line.gameObject.SetActive(true);
                        if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                        {
                            Debug.LogError(string.Concat("[CModuleLinkedMesh]: Object ", st.name, ": Look vector magnitude invalid. Vector is (", vector.x, ", ", vector.y, ", ", vector.z, "). Transform ", st.transform.position.IsInvalid() ? "invalid" : "valid", " ", st.transform.position, ", target ", st.targetAnchor.position.IsInvalid() ? "invalid" : "valid", ", ", st.targetAnchor.position));
                            return false;
                        }
                        lineRotation = Quaternion.LookRotation(vector / magnitude, st.transform.forward);
                        lineRotationComputed = true;
                        st.line.rotation = lineRotation;
                        Vector3 localScale = st.line.localScale;
                        st.line.localScale = new Vector3(localScale.x, localScale.y, magnitude * st.part.scaleFactor);
                    }
                }
            }
            if (st.startCap.IsNotNullOrDestroyed())
            {
                if (!lineRotationComputed)
                    st.startCap.rotation = st.line.rotation;
                else
                    st.startCap.rotation = lineRotation;
            }

            return false;
        }

        /// <summary>
        /// Very unlikely to be necessary, but in theory nodeType could differ from the string stored in the nodeTypes hashset
        /// </summary>
        private static void ModuleDockingNode_OnLoad_Postfix(ModuleDockingNode __instance)
        {
            if (__instance.nodeTypes.Count == 1)
                __instance.nodeType = __instance.nodeTypes.First();
        }

        /// <summary>
        /// This is called n² times where n is the amount of loaded docking port modules in the scene.
        /// We optimize the method, mainly by avoiding going through the hashset of modules types if there is only one type defined
        /// which is seemingly always the case (I didn't found any stock or modded part having multiple ones).
        /// Test case with 20 docking ports : 1.6% of the frame time in stock, 0.8% with the patch
        /// </summary>
        private static bool ModuleDockingNode_FindNodeApproaches_Prefix(ModuleDockingNode __instance, out ModuleDockingNode __result)
        {
            __result = null;
            if (__instance.part.packed)
                return false;

            for (int i = FlightGlobals.VesselsLoaded.Count; i-- > 0;)
            {
                Vessel vessel = FlightGlobals.VesselsLoaded[i];

                if (vessel.packed)
                    continue;

                for (int j = vessel.dockingPorts.Count; j-- > 0;)
                {
                    if (!(vessel.dockingPorts[j] is ModuleDockingNode other))
                        continue;

                    if (other.part.IsNullOrDestroyed()
                        || other.part.RefEquals(__instance.part)
                        || other.part.State == PartStates.DEAD
                        || other.state != __instance.st_ready.name
                        || other.gendered != __instance.gendered
                        || (__instance.gendered && other.genderFemale == __instance.genderFemale)
                        || other.snapRotation != __instance.snapRotation
                        || (__instance.snapRotation && other.snapOffset != __instance.snapOffset))
                    {
                        continue;
                    }

                    bool checkRequired = false;
                    // fast path when only one node type
                    if (__instance.nodeTypes.Count == 1)
                    {
                        if (other.nodeTypes.Count == 1)
                            checkRequired = __instance.nodeType == other.nodeType;
                        else
                            checkRequired = other.nodeTypes.Contains(__instance.nodeType);
                    }
                    // slow path checking the hashSet
                    else
                    {
                        foreach (string nodeType in __instance.nodeTypes)
                        {
                            if (other.nodeTypes.Count == 1)
                                checkRequired = nodeType == other.nodeType;
                            else
                                checkRequired = other.nodeTypes.Contains(nodeType);

                            if (checkRequired)
                                break;
                        }
                    }

                    if (checkRequired && __instance.CheckDockContact(__instance, other, __instance.acquireRange, __instance.acquireMinFwdDot, __instance.acquireMinRollDot))
                    {
                        __result = other;
                        return false;
                    }

                }
            }

            return false;
        }

        private class FIIntegrationData
        {
            public Vector3 vesselPreIntegrationAccelF;
            public bool hasVesselPreIntegrationAccel;
            public Vector3 krakensbaneFrameVelocityF;

            public double dragMult;
            public double dragTail;
            public double dragTip;
            public double dragSurf;
            public double dragCdPower;

            public double liftMach;

            public void Populate(FlightIntegrator fi)
            {
                vesselPreIntegrationAccelF = fi.vessel.precalc.integrationAccel;
                hasVesselPreIntegrationAccel = !vesselPreIntegrationAccelF.IsZero();
                krakensbaneFrameVelocityF = Krakensbane.GetFrameVelocity();

                float mach = (float)fi.mach;

                dragMult = PhysicsGlobals.Instance.dragCurveMultiplier.Evaluate(mach);
                dragTail = PhysicsGlobals.Instance.dragCurveTail.Evaluate(mach);
                dragTip = PhysicsGlobals.Instance.dragCurveTip.Evaluate(mach);
                dragSurf = PhysicsGlobals.Instance.dragCurveSurface.Evaluate(mach);
                dragCdPower = PhysicsGlobals.Instance.dragCurveCdPower.Evaluate(mach);

                liftMach = PhysicsGlobals.BodyLiftCurve.liftMachCurve.Evaluate(mach); // maybe should be done on demand, as this doesn't apply in all cases...
            }
        }

        private static FastStack<Part> partStack = new FastStack<Part>();
        private static FIIntegrationData fiData = new FIIntegrationData();

        private static bool FlightIntegrator_Integrate_Prefix(FlightIntegrator __instance)
        {
            fiData.Populate(__instance);

            // preorder traversal of the part tree
            partStack.EnsureCapacity(__instance.vessel.parts.Count);
            partStack.Push(__instance.partRef);
            while (partStack.TryPop(out Part part))
            {
                IntegratePart(__instance, fiData, part);

                for (int i = part.children.Count; i-- > 0;)
                {
                    Part childPart = part.children[i];
                    if (childPart.isAttached)
                        partStack.Push(childPart);
                }
            }

            return false;
        }

        private static void IntegratePart(FlightIntegrator fi, FIIntegrationData fiData, Part part)
        {
            bool hasRb = part.rb.IsNotNullOrDestroyed();
            bool hasServoRb = part.servoRb.IsNotNullOrDestroyed();

            // base force integration
            if (hasRb)
            {
                if (fiData.hasVesselPreIntegrationAccel)
                    part.rb.AddForce(fiData.vesselPreIntegrationAccelF, ForceMode.Acceleration);

                if (!part.force.IsZero())
                    part.rb.AddForce(part.force);

                if (!part.torque.IsZero())
                    part.rb.AddTorque(part.torque);

                for (int i = part.forces.Count; i-- > 0;)
                {
                    Part.ForceHolder force = part.forces[i];
                    part.rb.AddForceAtPosition(force.force, force.pos);
                }
            }

            if (hasServoRb)
            {
                part.servoRb.AddForce(fiData.vesselPreIntegrationAccelF, ForceMode.Acceleration);
            }
            part.forces.Clear();
            part.force.Zero();
            part.torque.Zero();

            // UpdateAerodynamics(part);
            Rigidbody partOrParentRb = part.rb;
            if (!hasRb)
            {
                Part parent = part.parent;
                while (partOrParentRb.IsNullOrDestroyed() && parent.IsNotNullOrDestroyed())
                {
                    partOrParentRb = parent.rb;
                    parent = parent.parent;
                }
            }

            if (partOrParentRb.IsNotNullOrDestroyed())
            {
                part.aerodynamicArea = 0.0;
                part.exposedArea = fi.CalculateAreaExposed(part);
                part.submergedDynamicPressurekPa = 0.0;
                part.dynamicPressurekPa = 0.0;

                if (part.angularDragByFI)
                {
                    if (hasRb)
                        part.rb.angularDrag = 0f;

                    if (hasServoRb)
                        part.servoRb.angularDrag = 0f;
                }

                part.dragVector = partOrParentRb.velocity + fiData.krakensbaneFrameVelocityF;
                part.dragVectorSqrMag = part.dragVector.sqrMagnitude;

                bool hasVelocity = false;
                if (part.dragVectorSqrMag != 0f)
                {
                    hasVelocity = true;
                    part.dragVectorMag = Mathf.Sqrt(part.dragVectorSqrMag);
                    part.dragVectorDir = part.dragVector / part.dragVectorMag;
                    part.dragVectorDirLocal = -part.partTransform.InverseTransformDirection(part.dragVectorDir);
                    part.dragScalar = 0f;
                }
                else
                {
                    part.dragVectorMag = 0f;
                    part.dragVectorDir.Zero();
                    part.dragVectorDirLocal.Zero();
                    part.dragScalar = 0f;
                }

                double submergedPortion = part.submergedPortion;

                if (!part.ShieldedFromAirstream && (part.atmDensity > 0.0 || submergedPortion > 0.0))
                {
                    if (!part.dragCubes.none)
                    {
                        SetDragCubeDrag(fiData, part.dragCubes, part.dragVectorDirLocal);
                    }

                    part.aerodynamicArea = fi.CalculateAerodynamicArea(part);
                    if (fi.cacheApplyDrag && hasVelocity && (partOrParentRb.RefEquals(part.rb) || fi.cacheApplyDragToNonPhysicsParts))
                    {
                        double emergedPortion = 1.0;
                        bool isInWater = false;
                        double pressure;
                        if (fi.currentMainBody.ocean)
                        {
                            if (submergedPortion > 0.0)
                            {
                                isInWater = true;
                                double waterDensity = fi.currentMainBody.oceanDensity * 1000.0;
                                if (submergedPortion >= 1.0)
                                {
                                    emergedPortion = 0.0;
                                    part.submergedDynamicPressurekPa = waterDensity;
                                    pressure = waterDensity * fi.cacheBuoyancyWaterAngularDragScalar * part.waterAngularDragMultiplier;
                                }
                                else
                                {
                                    emergedPortion = 1.0 - submergedPortion;
                                    part.submergedDynamicPressurekPa = waterDensity;
                                    pressure = part.staticPressureAtm * emergedPortion + submergedPortion * waterDensity * fi.cacheBuoyancyWaterAngularDragScalar * part.waterAngularDragMultiplier;
                                }
                            }
                            else
                            {
                                part.dynamicPressurekPa = part.atmDensity;
                                pressure = part.staticPressureAtm;
                            }
                        }
                        else
                        {
                            part.dynamicPressurekPa = part.atmDensity;
                            pressure = part.staticPressureAtm;
                        }

                        double dragSqrMag = 0.0005 * part.dragVectorSqrMag;
                        part.dynamicPressurekPa *= dragSqrMag;
                        part.submergedDynamicPressurekPa *= dragSqrMag;
                        if (hasRb && part.angularDragByFI)
                        {
                            if (isInWater)
                            {
                                pressure += part.dynamicPressurekPa * FlightIntegrator.KPA2ATM * emergedPortion;
                                pressure += part.submergedDynamicPressurekPa * FlightIntegrator.KPA2ATM * fi.cacheBuoyancyWaterAngularDragScalar * part.waterAngularDragMultiplier * submergedPortion;
                            }
                            else
                            {
                                pressure = part.dynamicPressurekPa * FlightIntegrator.KPA2ATM;
                            }

                            if (pressure < 0.0)
                                pressure = 0.0;

                            float rbAngularDrag = part.angularDrag * (float)pressure * fi.cacheAngularDragMultiplier;
                            part.rb.angularDrag = rbAngularDrag;
                            if (hasServoRb)
                                part.servoRb.angularDrag = rbAngularDrag;
                        }

                        double dragValue = fi.CalculateDragValue(part) * fi.pseudoReDragMult;
                        if (!double.IsNaN(dragValue) && dragValue != 0.0)
                        {
                            part.dragScalar = (float)(part.dynamicPressurekPa * dragValue * emergedPortion) * fi.cacheDragMultiplier;
                            fi.ApplyAeroDrag(part, partOrParentRb, fi.cacheDragUsesAcceleration ? ForceMode.Acceleration : ForceMode.Force);
                        }
                        else
                        {
                            part.dragScalar = 0f;
                        }

                        if (!part.hasLiftModule && (!part.bodyLiftOnlyUnattachedLiftActual || part.bodyLiftOnlyProvider == null || !part.bodyLiftOnlyProvider.IsLifting))
                        {
                            double bodyLiftScalar = part.bodyLiftMultiplier * fi.cacheBodyLiftMultiplier * fiData.liftMach;
                            if (isInWater)
                                bodyLiftScalar *= part.dynamicPressurekPa * emergedPortion + part.submergedDynamicPressurekPa * part.submergedLiftScalar * submergedPortion;
                            else
                                bodyLiftScalar *= part.dynamicPressurekPa;

                            part.bodyLiftScalar = (float)bodyLiftScalar;
                            if (part.bodyLiftScalar != 0f && part.DragCubes.LiftForce != Vector3.zero && !part.DragCubes.LiftForce.IsInvalid())
                            {
                                fi.ApplyAeroLift(part, partOrParentRb, fi.cacheDragUsesAcceleration ? ForceMode.Acceleration : ForceMode.Force);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Replacement for DragCubes.SetDrag() and DragCubes.DragCubeAddSurfaceDragDirection()
        /// </summary>
        private static void SetDragCubeDrag(FIIntegrationData fiData, DragCubeList dragCubes, Vector3 direction)
        {
            direction *= -1f;

            if (dragCubes.rotateDragVector)
                direction = dragCubes.dragVectorRotation * direction;
                
            double dotSum = 0.0;
            double area = 0.0;
            double areaDrag = 0.0;
            double crossSectionalArea = 0.0;
            double exposedArea = 0.0;
            double liftForceX = 0.0;
            double liftForceY = 0.0;
            double liftForceZ = 0.0;
            double depth = 0.0;
            double taperDot = 0.0;
            double dragCoeff = 0.0;

            for (int i = 0; i < 6; i++)
            {
                Vector3 faceDir = faceDirections[i];
                double areaOccluded = dragCubes.areaOccluded[i];
                double weightedDrag = dragCubes.weightedDrag[i];

                double dot = Vector3.Dot(direction, faceDir);
                double dotNormalized = (dot + 1.0) * 0.5;
                double drag; // = PhysicsGlobals.DragCurveValue(__instance.SurfaceCurves, dotNormalized, machNumber);

                if (dotNormalized <= 0.5)
                    drag = Numerics.Lerp(fiData.dragTail, fiData.dragSurf, dotNormalized * 2.0) * fiData.dragMult;
                else
                    drag = Numerics.Lerp(fiData.dragSurf, fiData.dragTip, (dotNormalized - 0.5) * 2.0) * fiData.dragMult;

                double areaOccludedByDrag = areaOccluded * drag;
                area += areaOccludedByDrag;
                double dragCd = areaOccludedByDrag;

                if (dragCd < 1.0)
                    dragCd = Math.Pow(dragCubes.DragCurveCd.Evaluate((float)weightedDrag), fiData.dragCdPower);

                areaDrag += areaOccludedByDrag * dragCd;
                crossSectionalArea += areaOccluded * Numerics.Clamp01(dot);

                double weightedDragMod = (!(weightedDrag < 1.0) || !(weightedDrag > 0.01)) ? 1.0 : (1.0 / weightedDrag);
                exposedArea += areaOccludedByDrag / fiData.dragMult * weightedDragMod;

                if (dot > 0.0)
                {
                    dotSum += dot;
                    double bodyLift = dragCubes.BodyLiftCurve.liftCurve.Evaluate((float)dot);
                    double weightedBodylift = dot * areaOccluded * weightedDrag * bodyLift * -1.0;

                    if (!double.IsNaN(weightedBodylift) && weightedBodylift > 0.0)
                    {
                        liftForceX += faceDir.x * weightedBodylift;
                        liftForceY += faceDir.y * weightedBodylift;
                        liftForceZ += faceDir.z * weightedBodylift;
                    }

                    depth += dot * dragCubes.weightedDepth[i];
                    taperDot += dot * weightedDragMod;
                }
            }

            if (dotSum > 0.0)
            {
                double invDotSum = 1f / dotSum;
                depth *= invDotSum;
                taperDot *= invDotSum;
            }

            if (area > 0.0)
            {
                dragCoeff = areaDrag / area;
                areaDrag = area * dragCoeff;
            }
            else
            {
                dragCoeff = 0.0;
                areaDrag = 0.0;
            }

            dragCubes.cubeData = new CubeData()
            {
                dragVector = direction,
                liftForce = new Vector3((float)liftForceX, (float)liftForceY, (float)liftForceZ),
                area = (float)area,
                areaDrag = (float)areaDrag,
                depth = (float)depth,
                crossSectionalArea = (float)crossSectionalArea,
                exposedArea = (float)exposedArea,
                dragCoeff = (float)dragCoeff,
                taperDot = (float)taperDot
            };
        }

        private static bool Part_isKerbalEVA_Prefix(Part __instance, out bool __result)
        {
            if (__instance.modules == null)
            {
                __result = false;
                return false;
            }

            __instance.cachedModules ??= new Dictionary<Type, PartModule>();

            if (!__instance.cachedModules.TryGetValue(typeof(KerbalEVA), out PartModule module))
            {
                List<PartModule> modules = __instance.modules.modules;
                for (int i = modules.Count; i-- > 0;)
                {
                    if (modules[i] is KerbalEVA)
                    {
                        module = modules[i];
                        break;
                    }
                }
                __instance.cachedModules[typeof(KerbalEVA)] = module;
            }

            __result = module.IsNotNullRef();
            return false;
        }

        #region VesselPrecalculate.CalculatePhysicsStats optimizations

        /// <summary>
        /// 40-60% faster than the stock method depending on the situation.
        /// Hard to optimize further, a large chunk of the time is spent getting transform / rb properties (~40%)
        /// and performing unavoidable double/float conversions (~10%).
        /// </summary>
        private static bool VesselPrecalculate_CalculatePhysicsStats_Prefix(VesselPrecalculate __instance)
        {
            Vessel vessel = __instance.vessel;
            bool isMasslessOrNotLoaded = true;

            if (vessel.loaded)
            {
                int partCount = vessel.Parts.Count;
                // This function is weird: positions are generally in world space, but angular calculations (velocity, MoI) are done relative to the reference transform (control point) orientation
                // Be mindful of which transform you're using!
                Transform vesselReferenceTransform = vessel.ReferenceTransform;
                TransformMatrix vesselInverseReferenceMatrix = TransformMatrix.WorldToLocal(vesselReferenceTransform);
                QuaternionD vesselInverseReferenceRotation = QuaternionD.Inverse(vesselReferenceTransform.rotation);
                Vector3d com = Vector3d.zero;
                Vector3d velocity = Vector3d.zero;
                Vector3d angularVelocity = Vector3d.zero;
                double vesselMass = 0.0;

                if (vessel.packed && partCount > 0)
                    Physics.SyncTransforms();

                VesselPrePartBufferEnsureCapacity(partCount);
                int index = partCount;
                int rbPartCount = 0;
                while (index-- > 0)
                {
                    Part part = vessel.parts[index];
                    if (part.rb.IsNotNullOrDestroyed())
                    {
                        Vector3d partPosition = part.partTransform.position;
                        QuaternionD partRotation = part.partTransform.rotation;
                        vesselPrePartBuffer[rbPartCount] = new PartVesselPreData(partPosition, partRotation, index);
                        rbPartCount++;
#if DEBUG_FLIGHTINTEGRATOR
                        double deviation = ((partPosition + partRotation * part.CoMOffset) - part.rb.worldCenterOfMass).magnitude;
                        if (deviation > 0.001)
                            Debug.LogWarning($"[KSPCF:FIPerf] KSPCF calculated WorldCenterOfMass is deviating from stock by {deviation:F3}m for part {part.partInfo.title} on vessel {vessel.GetDisplayName()}");
#endif
                        double physicsMass = part.physicsMass;
                        // note : on flight scene load, the parts RBs center of mass won't be set until the vessel gets out of the packed
                        // state (see FI.UpdateMassStats()), it will initially be set to whatever PhysX has computed from the RB colliders.
                        // This result in an inconsistent vessel CoM (and all derived stats) being computed for several frames. 
                        // For performance reasons we don't use rb.worldCenterOfMass, but instead re-compute it from Part.CoMOffset, but
                        // this also has the side effect of fixing those inconsistencies.
                        com.Add((partPosition + partRotation * part.CoMOffset) * physicsMass);
                        velocity.Add((Vector3d)part.rb.velocity * physicsMass);
                        angularVelocity.Add(vesselInverseReferenceRotation * part.rb.angularVelocity * physicsMass);
                        vesselMass += physicsMass;
                    }
                }

                if (vesselMass > 0.0)
                {
                    isMasslessOrNotLoaded = false;
                    vessel.totalMass = vesselMass;
                    double vesselMassRecip = 1.0 / vesselMass;
                    vessel.CoMD = com * vesselMassRecip;
                    vessel.rb_velocityD = velocity * vesselMassRecip;
                    vessel.velocityD = vessel.rb_velocityD + Krakensbane.GetFrameVelocity();
                    vessel.CoM = vessel.CoMD;
                    vessel.localCoM = vessel.vesselTransform.InverseTransformPoint(vessel.CoM);
                    vessel.rb_velocity = vessel.rb_velocityD;
                    vessel.angularVelocityD = angularVelocity * vesselMassRecip;
                    vessel.angularVelocity = vessel.angularVelocityD;

                    if (vessel.angularVelocityD == Vector3d.zero && vessel.packed)
                    {
                        vessel.MOI.Zero();
                        vessel.angularMomentum.Zero();
                    }
                    else
                    {
                        InertiaTensor inertiaTensor = new InertiaTensor();
                        for (int i = 0; i < rbPartCount; i++)
                        {
                            PartVesselPreData partPreData = vesselPrePartBuffer[i];
                            Part part = vessel.parts[partPreData.partIndex];

                            // add part inertia tensor to vessel inertia tensor
                            Vector3d principalMoments = part.rb.inertiaTensor;
                            QuaternionD princAxesRot = vesselInverseReferenceRotation * partPreData.rotation * (QuaternionD)part.rb.inertiaTensorRotation;
                            inertiaTensor.AddPartInertiaTensor(principalMoments, princAxesRot);

                            // add part mass and position contribution to vessel inertia tensor
                            double rbMass = Math.Max(part.partInfo.minimumRBMass, part.physicsMass);
                            // Note : the stock MoI code fails to account for the additional RB of servo parts.
                            // On servo parts, the part physicsMass is redistributed equally between the part RB and the servo RB, and since when
                            // computing the MoI, the stock code uses only the rb.mass, some mass will be unacounted for. Ideally we should do
                            // the full additional MoI calcs with the servo RB, but as a shorthand fix we just include the whole physicsMass instead
                            // of half of it like what stock would do. If we want to replicate exactely stock, uncomment those :
                            // if (part.servoRb.IsNotNullRef())
                            //     rbMass *= 0.5;
                            // Note 2 : another side effect of using Part.physicsMass instead of rb.mass is that mass will be correct on scene
                            // loads, before FI.UpdateMassStats() has run (when it hasn't run yet, rb.mass is set to 1 for all parts)
                            Vector3d CoMToPart = vesselInverseReferenceMatrix.MultiplyVector(partPreData.position - vessel.CoMD); // Note this uses the reference orientation, but doesn't use the translation
                            inertiaTensor.AddPartMass(rbMass, CoMToPart);
                        }

                        vessel.MOI = inertiaTensor.MoI;
                        vessel.angularMomentum.x = (float)(inertiaTensor.m00 * vessel.angularVelocityD.x);
                        vessel.angularMomentum.y = (float)(inertiaTensor.m11 * vessel.angularVelocityD.y);
                        vessel.angularMomentum.z = (float)(inertiaTensor.m22 * vessel.angularVelocityD.z);
                    }
                }

#if DEBUG_FLIGHTINTEGRATOR
                VerifyPhysicsStats(__instance);
#endif
            }

            if (isMasslessOrNotLoaded)
            {
                if (vessel.packed)
                {
                    if (vessel.LandedOrSplashed)
                    {
                        vessel.CoMD = __instance.worldSurfacePos + __instance.worldSurfaceRot * vessel.localCoM;
                    }
                    else
                    {
                        if (!vessel.orbitDriver.Ready)
                        {
                            vessel.orbitDriver.orbit.Init();
                            vessel.orbitDriver.updateFromParameters(setPosition: false);
                        }
                        vessel.CoMD = vessel.mainBody.position + vessel.orbitDriver.pos;
                    }
                }
                else
                {
                    vessel.CoMD = vessel.vesselTransform.TransformPoint(vessel.localCoM);
                }

                vessel.CoM = vessel.CoMD;

                if (vessel.rootPart.IsNotNullOrDestroyed() && vessel.rootPart.rb.IsNotNullOrDestroyed())
                {
                    vessel.rb_velocity = vessel.rootPart.rb.GetPointVelocity(vessel.CoM);
                    vessel.rb_velocityD = vessel.rb_velocity;
                    vessel.velocityD = (Vector3d)vessel.rb_velocity + Krakensbane.GetFrameVelocity();
                    vessel.angularVelocityD = (vessel.angularVelocity = Quaternion.Inverse(vessel.ReferenceTransform.rotation) * vessel.rootPart.rb.angularVelocity);
                }
                else
                {
                    vessel.rb_velocity.Zero();
                    vessel.rb_velocityD.Zero();
                    vessel.velocityD.Zero();
                    vessel.angularVelocity.Zero();
                    vessel.angularVelocityD.Zero();
                }
                vessel.MOI.Zero();
                vessel.angularMomentum.Zero();
            }
            __instance.firstStatsRunComplete = true;
            return false;
        }

        private readonly struct PartVesselPreData
        {
            public readonly int partIndex;
            public readonly Vector3d position;
            public readonly QuaternionD rotation;

            public PartVesselPreData(Vector3d position, QuaternionD rotation, int partIndex)
            {
                this.position = position;
                this.rotation = rotation;
                this.partIndex = partIndex;
            }
        }

        private static void VesselPrePartBufferEnsureCapacity(int partCount)
        {
            if (vesselPrePartBuffer.Length < partCount)
                vesselPrePartBuffer = new PartVesselPreData[(int)(partCount * 1.25)];
        }

        private static PartVesselPreData[] vesselPrePartBuffer = new PartVesselPreData[300];

        private struct InertiaTensor
        {
            public double m00;
            public double m11;
            public double m22;

            public void AddPartInertiaTensor(Vector3d principalMoments, QuaternionD princAxesRot)
            {
                // inverse the princAxesRot quaternion
                double invpx = -princAxesRot.x;
                double invpy = -princAxesRot.y;
                double invpz = -princAxesRot.z;

                // prepare inverse rotation
                double ipx2 = invpx * 2.0;
                double ipy2 = invpy * 2.0;
                double ipz2 = invpz * 2.0;
                double ipx2x = invpx * ipx2;
                double ipy2y = invpy * ipy2;
                double ipz2z = invpz * ipz2;
                double ipy2x = invpx * ipy2;
                double ipz2x = invpx * ipz2;
                double ipz2y = invpy * ipz2;
                double ipx2w = princAxesRot.w * ipx2;
                double ipy2w = princAxesRot.w * ipy2;
                double ipz2w = princAxesRot.w * ipz2;

                // inverse rotate column 0
                double ir0x = principalMoments.x * (1.0 - (ipy2y + ipz2z));
                double ir0y = principalMoments.y * (ipy2x + ipz2w);
                double ir0z = principalMoments.z * (ipz2x - ipy2w);

                // inverse rotate column 1
                double ir1x = principalMoments.x * (ipy2x - ipz2w);
                double ir1y = principalMoments.y * (1.0 - (ipx2x + ipz2z));
                double ir1z = principalMoments.z * (ipz2y + ipx2w);

                // inverse rotate column 2
                double ir2x = principalMoments.x * (ipz2x + ipy2w);
                double ir2y = principalMoments.y * (ipz2y - ipx2w);
                double ir2z = principalMoments.z * (1.0 - (ipx2x + ipy2y));

                // prepare rotation
                double qx2 = princAxesRot.x * 2.0;
                double qy2 = princAxesRot.y * 2.0;
                double qz2 = princAxesRot.z * 2.0;
                double qx2x = princAxesRot.x * qx2;
                double qy2y = princAxesRot.y * qy2;
                double qz2z = princAxesRot.z * qz2;
                double qy2x = princAxesRot.x * qy2;
                double qz2x = princAxesRot.x * qz2;
                double qz2y = princAxesRot.y * qz2;
                double qx2w = princAxesRot.w * qx2;
                double qy2w = princAxesRot.w * qy2;
                double qz2w = princAxesRot.w * qz2;

                // rotate column 0
                m00 += (1.0 - (qy2y + qz2z)) * ir0x + (qy2x - qz2w) * ir0y + (qz2x + qy2w) * ir0z;

                // rotate column 1
                m11 += (qy2x + qz2w) * ir1x + (1.0 - (qx2x + qz2z)) * ir1y + (qz2y - qx2w) * ir1z;

                // rotate column 2
                m22 += (qz2x - qy2w) * ir2x + (qz2y + qx2w) * ir2y + (1.0 - (qx2x + qy2y)) * ir2z;
            }

            public void AddPartMass(double partMass, Vector3d partPosition)
            {
                double massLever = partMass * partPosition.sqrMagnitude;
                double invMass = -partMass;

                m00 += invMass * partPosition.x * partPosition.x + massLever;
                m11 += invMass * partPosition.y * partPosition.y + massLever;
                m22 += invMass * partPosition.z * partPosition.z + massLever;
            }

            public Vector3 MoI => new Vector3((float)m00, (float)m11, (float)m22);
        }

        private static void VerifyPhysicsStats(VesselPrecalculate vesselPre)
        {
            Vessel vessel = vesselPre.Vessel;
            if (!vessel.loaded)
                return;

            Transform referenceTransform = vessel.ReferenceTransform;
            int partCount = vessel.Parts.Count;
            QuaternionD vesselInverseRotation = QuaternionD.Inverse(referenceTransform.rotation);
            Vector3d pCoM = Vector3d.zero;
            Vector3d pVel = Vector3d.zero;
            Vector3d pAngularVel = Vector3d.zero;
            double vesselMass = 0.0; // vessel.totalMass
            int index = partCount;
            while (index-- > 0)
            {
                Part part = vessel.parts[index];
                if (part.rb != null)
                {
                    double physicsMass = part.physicsMass;
                    pCoM += (Vector3d)part.rb.worldCenterOfMass * physicsMass;
                    Vector3d vector3d = (Vector3d)part.rb.velocity * physicsMass;
                    pVel += vector3d;
                    pAngularVel += vesselInverseRotation * part.rb.angularVelocity * physicsMass;
                    vesselMass += physicsMass;
                }
            }
            if (vesselMass > 0.0)
            {
                double vesselMassRecip = 1.0 / vesselMass;
                Vector3d vCoMD = pCoM * vesselMassRecip; // vessel.CoMD
                Vector3d vRbVelD = pVel * vesselMassRecip; // vessel.rb_velocityD
                Vector3d vAngVelD = pAngularVel * vesselMassRecip; // vessel.angularVelocityD
                Vector3 vMoI = vessel.MOI;
                if (vAngVelD == Vector3d.zero && vessel.packed)
                {
                    vMoI.Zero();
                }
                else
                {
                    Matrix4x4 inertiaTensor = Matrix4x4.zero;
                    Matrix4x4 mIdentity = Matrix4x4.identity;
                    Matrix4x4 m2 = Matrix4x4.identity;
                    Matrix4x4 m3 = Matrix4x4.identity;
                    Quaternion vesselInverseRotationF = vesselInverseRotation;
                    for (int i = 0; i < partCount; i++)
                    {
                        Part part2 = vessel.parts[i];
                        if (part2.rb != null)
                        {
                            KSPUtil.ToDiagonalMatrix2(part2.rb.inertiaTensor, ref mIdentity);
                            Quaternion partRot = vesselInverseRotationF * part2.transform.rotation * part2.rb.inertiaTensorRotation;
                            Quaternion invPartRot = Quaternion.Inverse(partRot);
                            Matrix4x4 mPart = Matrix4x4.TRS(Vector3.zero, partRot, Vector3.one);
                            Matrix4x4 invMPart = Matrix4x4.TRS(Vector3.zero, invPartRot, Vector3.one);
                            Matrix4x4 right = mPart * mIdentity * invMPart;
                            KSPUtil.Add(ref inertiaTensor, ref right);
                            Vector3 lever = referenceTransform.InverseTransformDirection(part2.rb.position - vCoMD);
                            KSPUtil.ToDiagonalMatrix2(part2.rb.mass * lever.sqrMagnitude, ref m2);
                            KSPUtil.Add(ref inertiaTensor, ref m2);
                            KSPUtil.OuterProduct2(lever, (0f - part2.rb.mass) * lever, ref m3);
                            KSPUtil.Add(ref inertiaTensor, ref m3);
                        }
                    }
                    vMoI = KSPUtil.Diag(inertiaTensor);
                }

                string warnings = string.Empty;

                double vMassDiff = Math.Abs(vesselMass - vessel.totalMass);
                if (vMassDiff > vesselMass / 1e6)
                    warnings += $"Mass diverging by {vMassDiff:F6} ({vMassDiff / (vesselMass > 0.0 ? vesselMass : 1.0):P5}) ";

                double vCoMDDiff = (vessel.CoMD - vCoMD).magnitude;
                if (vCoMDDiff > vCoMD.magnitude / 1e6)
                    warnings += $"CoM diverging by {vCoMDDiff:F6} ({vCoMDDiff / (vCoMD.magnitude > 0.0 ? vCoMD.magnitude : 1.0):P5}) ";

                double vVelDiff = (vessel.rb_velocityD - vRbVelD).magnitude;
                if (vVelDiff > vRbVelD.magnitude / 1e6)
                    warnings += $"Velocity diverging by {vVelDiff:F6} ({vVelDiff / (vRbVelD.magnitude > 0.0 ? vRbVelD.magnitude : 1.0):P5}) ";

                double vAngVelDDiff = (vessel.angularVelocityD - vAngVelD).magnitude;
                if (vAngVelDDiff > vAngVelD.magnitude / 1e6)
                    warnings += $"Angular velocity diverging by {vAngVelDDiff:F6} ({vAngVelDDiff / (vAngVelD.magnitude > 0.0 ? vAngVelD.magnitude : 1.0):P5}) ";

                double vMoIDiff = (vessel.MOI - vMoI).magnitude;
                if (vMoIDiff > vMoI.magnitude / 1e5)
                    warnings += $"MoI diverging by {vMoIDiff:F6} ({vMoIDiff / (vMoI.magnitude > 0.0 ? vMoI.magnitude : 1.0):P5}) ";

                if (warnings.Length > 0)
                {
                    Debug.LogWarning($"[KSPCF:FIPerf] CalculatePhysicsStats : diverging stats for vessel {vessel.GetDisplayName()}\n{warnings}");
                }
            }
        }

        #endregion

        #region FlightIntegrator.UpdateMassStats optimizations

        // Avoid setting RigidBody.mass and RigidBody.centerOfMass for all parts on every update if they didn't change
        // Setting these properties is quite costly on the PhysX side, especially centerOfMass (1% to 2% of the frame time
        // depending on the situation), and centerOfMass should almost never change unless something is changing CoMOffset.
        // Setting mass is less costly and will change relatively often but avoiding setting when unecessary is still a decent improvement.
        // We also take the opportunity to make a few optimizations (faster null checks, inlined inner loop, using the PartResourceList
        // backing list instead of going through the custom indexer...)
        static bool FlightIntegrator_UpdateMassStats_Prefix(FlightIntegrator __instance)
        {
            List<Part> parts = __instance.vessel.parts;
            int partCount = parts.Count;
            for (int i = partCount; i-- > 0;)
            {
                Part part = parts[i];

                List<PartResource> partResources = part._resources.dict.list;
                float resourceMass = 0f;
                double resourceThermalMass = 0.0;
                for (int j = partResources.Count; j-- > 0;)
                {
                    PartResource partResource = partResources[j];
                    float resMass = (float)partResource.amount * partResource.info.density;
                    resourceMass += resMass;
                    resourceThermalMass += resMass * partResource.info._specificHeatCapacity;
                }

                part.resourceMass = resourceMass;
                part.resourceThermalMass = resourceThermalMass;
                part.thermalMass = part.mass * __instance.cacheStandardSpecificHeatCapacity * part.thermalMassModifier + part.resourceThermalMass;
                __instance.SetSkinThermalMass(part);
                part.thermalMass = Math.Max(part.thermalMass - part.skinThermalMass, 0.1);
                part.thermalMassReciprocal = 1.0 / part.thermalMass;
            }

            for (int i = partCount; i-- > 0;)
            {
                Part part = parts[i];
                if (part.rb.IsNotNullOrDestroyed())
                {
                    float physicsMass = part.mass + part.resourceMass + GetPhysicslessChildsMass(part);
                    physicsMass = Mathf.Clamp(physicsMass, part.partInfo.MinimumMass, Mathf.Abs(physicsMass));
                    part.physicsMass = physicsMass;

                    if (!part.packed)
                    {
                        float rbMass = Mathf.Max(part.partInfo.MinimumRBMass, physicsMass);
                        bool hasServoRB = part.servoRb.IsNotNullOrDestroyed();

                        if (hasServoRB)
                            rbMass *= 0.5f;

                        // unfortunately, there is some internal fp manipulation when setting rb.mass
                        // resulting in tiny deltas between what we set and the value we read back.
                        if (Math.Abs(part.rb.mass - rbMass) > rbMass / 1e6f)
                        {
                            part.rb.mass = rbMass;
                            if (hasServoRB)
                                part.servoRb.mass = rbMass;
                        }

                        // Either this doesn't happen with rb.centerOfMass, or the built-in Vector3 equality
                        // epsilon takes cares of it. I guess this might happen still if a large offset is defined...
                        if (part.rb.centerOfMass != part.CoMOffset)
                            part.rb.centerOfMass = part.CoMOffset;
                    }
                }
                else
                {
                    part.physicsMass = 0.0;
                }
            }

            return false;
        }


        // Recursion is faster than a stack based approach here
        private static float GetPhysicslessChildsMass(Part part)
        {
            float mass = 0f;
            for (int i = part.children.Count; i-- > 0;)
            {
                if (part.children[i].rb.IsNullOrDestroyed())
                {
                    Part childPart = part.children[i];
                    mass += childPart.mass + childPart.resourceMass + GetPhysicslessChildsMass(childPart);
                }
            }
            return mass;
        }


        #endregion

        #region FlightIntegrator.UpdateOcclusion optimizations

        static bool FlightIntegrator_UpdateOcclusionConvection_Prefix(FlightIntegrator __instance)
        {
            FlightIntegrator fi = __instance;

            if (fi.mach <= 1.0)
            {
                if (fi.wasMachConvectionEnabled)
                {
                    for (int i = 0; i < fi.partThermalDataCount; i++)
                    {
                        PartThermalData partThermalData = fi.partThermalDataList[i];
                        partThermalData.convectionCoeffMultiplier = 1.0;
                        partThermalData.convectionAreaMultiplier = 1.0;
                        partThermalData.convectionTempMultiplier = 1.0;
                    }
                    fi.wasMachConvectionEnabled = false;
                }
                return false;
            }

            bool requiresSort = false;
            if (fi.partThermalDataCount != fi.partThermalDataList.Count)
            {
                fi.recreateThermalGraph = true;
                fi.CheckThermalGraph();
            }

            List<OcclusionData> occlusionDataList = fi.occlusionConv;
            OcclusionCone[] occluders = fi.occludersConvection;
            Vector3d velocity = fi.nVel;

            int lastPartIndex = fi.partThermalDataCount - 1;
            int partIndex = fi.partThermalDataCount;
            QuaternionDPointRotation velToUp = new QuaternionDPointRotation(Numerics.FromToRotation(velocity, Vector3d.up));
            while (partIndex-- > 0)
            {
                OcclusionData occlusionDataToUpdate = occlusionDataList[partIndex];
                UpdateOcclusionData(occlusionDataToUpdate, velocity, velToUp);
                if (!requiresSort && partIndex < lastPartIndex && occlusionDataList[partIndex + 1].maximumDot < occlusionDataToUpdate.maximumDot)
                    requiresSort = true;
            }

            if (requiresSort)
                occlusionDataList.Sort();

            double sqrtMach = Math.Sqrt(fi.mach);
            double sqrtMachAng = Math.Asin(1.0 / sqrtMach);
            double detachAngle = 0.7957 * (1.0 - 1.0 / (fi.mach * sqrtMach));

            OcclusionData occlusionData = fi.occlusionConv[lastPartIndex];
            PartThermalData ptd = occlusionData.ptd;

            ptd.convectionCoeffMultiplier = 1.0;
            ptd.convectionAreaMultiplier = 1.0;
            ptd.convectionTempMultiplier = 1.0;

            occlusionData.convCone.Setup(occlusionData, sqrtMach, sqrtMachAng, detachAngle);

            // We do a maybe risky trick here. OcclusionCone.Setup computes shockAngle in radians, but only the tangent of that angle
            // is ever used, a bit latter in the inner loop. So we do the conversion here to avoid having to do it O(n²) times latter.
            occlusionData.convCone.shockAngle = Math.Tan(occlusionData.convCone.shockAngle);

            fi.occludersConvection[0] = occlusionData.convCone;
            fi.occludersConvectionCount = 1;
            //FXCamera.Instance.ApplyObliqueness((float)occlusionData.convCone.shockAngle); // empty method

            int index = lastPartIndex;
            while (index-- > 0)
            {
                occlusionData = fi.occlusionConv[index];
                ptd = occlusionData.ptd;
                ptd.convectionCoeffMultiplier = 1.0;
                ptd.convectionAreaMultiplier = 1.0;
                ptd.convectionTempMultiplier = 1.0;

                double minExtentsX = occlusionData.minExtents.x;
                double minExtentsY = occlusionData.minExtents.y;
                double maxExtentsX = occlusionData.maxExtents.x;
                double maxExtentsY = occlusionData.maxExtents.y;

                double projectedCenterX = occlusionData.projectedCenter.x;
                double projectedCenterY = occlusionData.projectedCenter.y;
                double projectedCenterZ = occlusionData.projectedCenter.z;

                for (int i = 0; i < fi.occludersConvectionCount; i++)
                {
                    OcclusionCone occluder = occluders[i];
                    double offsetX = occluder.offset.x;
                    double offsetY = occluder.offset.y;
                    double minX = offsetX + minExtentsX;
                    double minY = offsetY + minExtentsY;
                    double maxX = offsetX + maxExtentsX;
                    double maxY = offsetY + maxExtentsY;
                    double centralExtentX = occluder.extents.x;
                    double centralExtentY = occluder.extents.y;
                    double centralExtentXInv = -centralExtentX;
                    double centralExtentYInv = -centralExtentY;

                    double mid = (maxX - minX) * (maxY - minY);
                    double rectRect = 0.0;
                    if (maxX >= centralExtentXInv && minX <= centralExtentX && maxY >= centralExtentYInv && minY <= centralExtentY && mid != 0.0)
                    {
                        double midX = Math.Min(centralExtentX, maxX) - Math.Max(centralExtentXInv, minX);
                        if (midX < 0.0) midX = 0.0;

                        double midY = Math.Min(centralExtentY, maxY) - Math.Max(centralExtentYInv, minY);
                        if (midY < 0.0) midY = 0.0;

                        rectRect = midX * midY / mid;
                        if (double.IsNaN(rectRect)) // it could be nice to put that outside the inner loop
                        {
                            rectRect = 0.0;
                            if (GameSettings.FI_LOG_TEMP_ERROR)
                                Debug.LogError($"[FlightIntegrator]: For part " + occlusionData.ptd.part.name + ", rectRect is NaN");
                        }
                    }

                    double areaOfIntersection = 1.0;
                    if (rectRect < 0.99)
                    {
                        double angleDiff = occluder.shockNoseDot - occlusionData.centroidDot;
                        double existingConeRadius = occluder.radius + angleDiff * occluder.shockAngle; // This used to be Math.Tan(occluder.shockAngle)

                        double x = projectedCenterX - occluder.center.x;
                        double y = projectedCenterY - occluder.center.y;
                        double z = projectedCenterZ - occluder.center.z;
                        double sqrDistance = x * x + y * y + z * z;

                        areaOfIntersection = OcclusionData.AreaOfIntersection(existingConeRadius, occlusionData.projectedRadius, sqrDistance);
                    }
                    else
                    {
                        rectRect = 1.0;
                    }

                    double num4 = 1.0 - areaOfIntersection;
                    double num5 = areaOfIntersection - rectRect;
                    ptd.convectionTempMultiplier = num4 * ptd.convectionTempMultiplier + num5 * occluder.shockConvectionTempMult + rectRect * occluder.occludeConvectionTempMult;
                    ptd.convectionCoeffMultiplier = num4 * ptd.convectionCoeffMultiplier + num5 * occluder.shockConvectionCoeffMult + rectRect * occluder.occludeConvectionCoeffMult;

                    double shockStats = 1.0 - rectRect + rectRect * occluder.occludeConvectionAreaMult;

                    ptd.convectionAreaMultiplier *= shockStats;
                    if (ptd.convectionAreaMultiplier < 0.001)
                    {
                        ptd.convectionAreaMultiplier = 0.0;
                        break;
                    }
                }
                if (ptd.convectionAreaMultiplier > 0.0)
                {
                    occlusionData.convCone.Setup(occlusionData, sqrtMach, sqrtMachAng, detachAngle);
                    occlusionData.convCone.shockAngle = Math.Tan(occlusionData.convCone.shockAngle);
                    fi.occludersConvection[fi.occludersConvectionCount] = occlusionData.convCone;
                    fi.occludersConvectionCount++;
                }
            }

            return false;
        }

        static bool FlightIntegrator_UpdateOcclusionSolar_Prefix(FlightIntegrator __instance)
        {
            FlightIntegrator fi = __instance;
            List<OcclusionData> occlusionDataList = fi.occlusionSun;
            OcclusionCylinder[] occluders = fi.occludersSun;
            Vector3d velocity = fi.sunVector;

            bool requiresSort = false;

            if (fi.partThermalDataCount != fi.partThermalDataList.Count)
            {
                fi.recreateThermalGraph = true;
                fi.CheckThermalGraph();
            }

            int lastPartIndex = fi.partThermalDataCount - 1;
            int partIndex = fi.partThermalDataCount;
            QuaternionDPointRotation velToUp = new QuaternionDPointRotation(Numerics.FromToRotation(velocity, Vector3d.up));
            while (partIndex-- > 0)
            {
                OcclusionData occlusionDataToUpdate = occlusionDataList[partIndex];
                UpdateOcclusionData(occlusionDataToUpdate, velocity, velToUp);
                if (!requiresSort && partIndex < lastPartIndex && occlusionDataList[partIndex + 1].maximumDot < occlusionDataToUpdate.maximumDot)
                    requiresSort = true;
            }

            if (requiresSort)
                occlusionDataList.Sort();

            OcclusionData occlusionData = occlusionDataList[lastPartIndex];
            occlusionData.ptd.sunAreaMultiplier = 1.0;
            occlusionData.sunCyl.Setup(occlusionData);
            occluders[0] = occlusionData.sunCyl;

            // O(n²) [n = part count] situation here, so micro-optimizing the inner loop is critical.
            int occluderCount = 1;
            int index = lastPartIndex;
            while (index-- > 0)
            {
                occlusionData = occlusionDataList[index];
                double minExtentsX = occlusionData.minExtents.x;
                double minExtentsY = occlusionData.minExtents.y;
                double maxExtentsX = occlusionData.maxExtents.x;
                double maxExtentsY = occlusionData.maxExtents.y;

                double areaMultiplier = 1.0;

                for (int i = 0; i < occluderCount; i++)
                {
                    // GetCylinderOcclusion
                    OcclusionCylinder occluder = occluders[i];
                    double offsetX = occluder.offset.x;
                    double offsetY = occluder.offset.y;
                    double minX = offsetX + minExtentsX;
                    double minY = offsetY + minExtentsY;
                    double maxX = offsetX + maxExtentsX;
                    double maxY = offsetY + maxExtentsY;
                    double centralExtentX = occluder.extents.x;
                    double centralExtentY = occluder.extents.y;
                    double centralExtentXInv = -centralExtentX;
                    double centralExtentYInv = -centralExtentY;

                    double mid = (maxX - minX) * (maxY - minY);
                    if (maxX >= centralExtentXInv && minX <= centralExtentX && maxY >= centralExtentYInv && minY <= centralExtentY && mid != 0.0)
                    {
                        double midX = Math.Min(centralExtentX, maxX) - Math.Max(centralExtentXInv, minX);
                        if (midX < 0.0) midX = 0.0;

                        double midY = Math.Min(centralExtentY, maxY) - Math.Max(centralExtentYInv, minY);
                        if (midY < 0.0) midY = 0.0;

                        double rectRect = midX * midY / mid;
                        if (double.IsNaN(rectRect)) // it could be nice to put that outside the inner loop
                        {
                            if (GameSettings.FI_LOG_TEMP_ERROR)
                                Debug.LogError("[FlightIntegrator]: For part " + occlusionData.ptd.part.name + ", rectRect is NaN");
                        }
                        else
                        {
                            areaMultiplier -= rectRect;
                        }
                    }

                    if (areaMultiplier < 0.001)
                    {
                        areaMultiplier = 0.0;
                        break;
                    }
                }

                occlusionData.ptd.sunAreaMultiplier = areaMultiplier;
                if (areaMultiplier > 0)
                {
                    occlusionData.sunCyl.Setup(occlusionData);
                    occluders[occluderCount] = occlusionData.sunCyl;
                    occluderCount++;
                }
            }

            return false;
        }

        static bool FlightIntegrator_UpdateOcclusionBody_Prefix(FlightIntegrator __instance)
        {
            FlightIntegrator fi = __instance;
            List<OcclusionData> occlusionDataList = fi.occlusionBody;
            OcclusionCylinder[] occluders = fi.occludersBody;
            Vector3d velocity = -fi.vessel.upAxis;

            bool requiresSort = false;

            if (fi.partThermalDataCount != fi.partThermalDataList.Count)
            {
                fi.recreateThermalGraph = true;
                fi.CheckThermalGraph();
            }

            int lastPartIndex = fi.partThermalDataCount - 1;
            int partIndex = fi.partThermalDataCount;
            QuaternionDPointRotation velToUp = new QuaternionDPointRotation(Numerics.FromToRotation(velocity, Vector3d.up));
            while (partIndex-- > 0)
            {
                OcclusionData occlusionDataToUpdate = occlusionDataList[partIndex];
                UpdateOcclusionData(occlusionDataToUpdate, velocity, velToUp);
                if (!requiresSort && partIndex < lastPartIndex && occlusionDataList[partIndex + 1].maximumDot < occlusionDataToUpdate.maximumDot)
                    requiresSort = true;
            }

            if (requiresSort)
                occlusionDataList.Sort();

            OcclusionData occlusionData = occlusionDataList[lastPartIndex];
            occlusionData.ptd.bodyAreaMultiplier = 1.0;
            occlusionData.bodyCyl.Setup(occlusionData);
            occluders[0] = occlusionData.bodyCyl;

            int occluderCount = 1;
            int index = lastPartIndex;
            while (index-- > 0)
            {
                occlusionData = occlusionDataList[index];
                double minExtentsX = occlusionData.minExtents.x;
                double minExtentsY = occlusionData.minExtents.y;
                double maxExtentsX = occlusionData.maxExtents.x;
                double maxExtentsY = occlusionData.maxExtents.y;

                double areaMultiplier = 1.0;

                for (int i = 0; i < occluderCount; i++)
                {
                    // GetCylinderOcclusion
                    OcclusionCylinder occluder = occluders[i];
                    double offsetX = occluder.offset.x;
                    double offsetY = occluder.offset.y;
                    double minX = offsetX + minExtentsX;
                    double minY = offsetY + minExtentsY;
                    double maxX = offsetX + maxExtentsX;
                    double maxY = offsetY + maxExtentsY;
                    double centralExtentX = occluder.extents.x;
                    double centralExtentY = occluder.extents.y;

                    double mid = (maxX - minX) * (maxY - minY);
                    if (!(maxX < 0.0 - centralExtentX) && !(minX > centralExtentX) && !(maxY < 0.0 - centralExtentY) && !(minY > centralExtentY) && mid != 0.0)
                    {
                        double rectRect = Math.Max(0.0, Math.Min(centralExtentX, maxX) - Math.Max(0.0 - centralExtentX, minX)) * Math.Max(0.0, Math.Min(centralExtentY, maxY) - Math.Max(0.0 - centralExtentY, minY)) / mid;
                        if (double.IsNaN(rectRect))
                        {
                            if (GameSettings.FI_LOG_TEMP_ERROR)
                                Debug.LogError("[FlightIntegrator]: For part " + occlusionData.ptd.part.name + ", rectRect is NaN");
                        }
                        else
                        {
                            areaMultiplier -= rectRect;
                        }
                    }

                    if (areaMultiplier < 0.001)
                    {
                        areaMultiplier = 0.0;
                        break;
                    }
                }

                occlusionData.ptd.bodyAreaMultiplier = areaMultiplier;
                if (areaMultiplier > 0)
                {
                    occlusionData.bodyCyl.Setup(occlusionData);
                    occluders[occluderCount] = occlusionData.bodyCyl;
                    occluderCount++;
                }
            }
            return false;
        }

        // a lot of stuff is actually unused in OcclusionData
        // boundsVertices (only used in the scope of OcclusionData.Update, we use local vars and inline stuff instead)
        // projectedVertices, projectedDots : part of an alternative thermal thing that isn't activated / never called
        // useDragArea is always true, so the involved code paths are never taken
        static void UpdateOcclusionData(OcclusionData occlusionData, Vector3d velocity, QuaternionDPointRotation velToUp)
        {
            Part part = occlusionData.part;

            if (part.IsNullOrDestroyed() || part.partTransform.IsNullOrDestroyed())
                return;

            Vector3 center = occlusionData.part.DragCubes.WeightedCenter;
            Vector3 size = occlusionData.part.DragCubes.WeightedSize;

            double cX = center.x;
            double cY = center.y;
            double cZ = center.z;
            double eX = size.x * 0.5;
            double eY = size.y * 0.5;
            double eZ = size.z * 0.5;
            double minX = cX - eX;
            double minY = cY - eY;
            double minZ = cZ - eZ;
            double maxX = cX + eX;
            double maxY = cY + eY;
            double maxZ = cZ + eZ;

            TransformMatrix localToWorldMatrix = TransformMatrix.LocalToWorld(part.partTransform);

            // 10% of the load is here, probably worth it to extract the matrix components and to manually inline (the MultiplyPoint3x4 method is **not** inlined)
            Vector3d boundVert1 = localToWorldMatrix.MultiplyPoint3x4(minX, minY, minZ);
            Vector3d boundVert2 = localToWorldMatrix.MultiplyPoint3x4(maxX, maxY, maxZ);
            Vector3d boundVert3 = localToWorldMatrix.MultiplyPoint3x4(minX, minY, maxZ);
            Vector3d boundVert4 = localToWorldMatrix.MultiplyPoint3x4(minX, maxY, minZ);
            Vector3d boundVert5 = localToWorldMatrix.MultiplyPoint3x4(maxX, minY, minZ);
            Vector3d boundVert6 = localToWorldMatrix.MultiplyPoint3x4(minX, maxY, maxZ);
            Vector3d boundVert7 = localToWorldMatrix.MultiplyPoint3x4(maxX, minY, maxZ);
            Vector3d boundVert8 = localToWorldMatrix.MultiplyPoint3x4(maxX, maxY, minZ);

            double minDot = double.MaxValue;
            double maxDot = double.MinValue;
            double minExtentX = double.MaxValue;
            double minExtentY = double.MaxValue;
            double maxExtentX = double.MinValue;
            double maxExtentY = double.MinValue;

            FindDotMinMax(Vector3d.Dot(boundVert1, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert1, out double vertX, out double vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert2, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert2, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert3, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert3, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert4, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert4, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert5, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert5, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert6, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert6, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert7, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert7, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            FindDotMinMax(Vector3d.Dot(boundVert8, velocity), ref minDot, ref maxDot);
            velToUp.RotatePointGetXZ(boundVert8, out vertX, out vertZ);
            FindExtentsMinMax(vertX, vertZ, ref minExtentX, ref minExtentY, ref maxExtentX, ref maxExtentY);

            Vector3d worldBoundsCenter = localToWorldMatrix.MultiplyPoint3x4(cX, cY, cZ);
            occlusionData.centroidDot = Vector3d.Dot(worldBoundsCenter, velocity);
            occlusionData.projectedCenter = worldBoundsCenter - occlusionData.centroidDot * velocity;
            occlusionData.boundsCenter = new Vector3((float)cX, (float)cY, (float)cZ);
            occlusionData.minimumDot = minDot;
            occlusionData.maximumDot = maxDot;
            occlusionData.minExtents = new Vector2((float)minExtentX, (float)minExtentY); // minExtents / maxExtents : ideally flatten into double fields
            occlusionData.maxExtents = new Vector2((float)maxExtentX, (float)maxExtentY);

            occlusionData.extents = (occlusionData.maxExtents - occlusionData.minExtents) * 0.5f; // extents, center : ideally flatten into double fields
            occlusionData.center = occlusionData.minExtents + occlusionData.extents;

            occlusionData.projectedArea = part.DragCubes.CrossSectionalArea;
            occlusionData.invFineness = part.DragCubes.TaperDot;
            occlusionData.maxWidthDepth = part.DragCubes.Depth;

            occlusionData.projectedRadius = Math.Sqrt(occlusionData.projectedArea / Math.PI);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FindExtentsMinMax(double x, double z, ref double minExtentX, ref double minExtentY, ref double maxExtentX, ref double maxExtentY)
        {
            maxExtentX = Math.Max(maxExtentX, x);
            maxExtentY = Math.Max(maxExtentY, z);
            minExtentX = Math.Min(minExtentX, x);
            minExtentY = Math.Min(minExtentY, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FindDotMinMax(double dot, ref double minDot, ref double maxDot)
        {
            if (dot < minDot)
                minDot = dot;

            if (dot > maxDot)
                maxDot = dot;
        }

        private struct QuaternionDPointRotation
        {
            private double qx2x;
            private double qy2y;
            private double qz2z;
            private double qy2x;
            private double qz2x;
            private double qz2y;
            private double qx2w;
            private double qy2w;
            private double qz2w;

            public QuaternionDPointRotation(QuaternionD rotation)
            {
                double qx2 = rotation.x * 2.0;
                double qy2 = rotation.y * 2.0;
                double qz2 = rotation.z * 2.0;
                qx2x = rotation.x * qx2;
                qy2y = rotation.y * qy2;
                qz2z = rotation.z * qz2;
                qy2x = rotation.x * qy2;
                qz2x = rotation.x * qz2;
                qz2y = rotation.y * qz2;
                qx2w = rotation.w * qx2;
                qy2w = rotation.w * qy2;
                qz2w = rotation.w * qz2;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RotatePointGetXZ(Vector3d point, out double x, out double z)
            {
                x = (1.0 - (qy2y + qz2z)) * point.x + (qy2x - qz2w) * point.y + (qz2x + qy2w) * point.z;
                z = (qz2x - qy2w) * point.x + (qz2y + qx2w) * point.y + (1.0 - (qx2x + qy2y)) * point.z;
            }
        }

        #endregion
    }
}
