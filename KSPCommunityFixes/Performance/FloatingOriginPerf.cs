using KSPCommunityFixes.Library;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
    internal class FloatingOriginPerf : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(FloatingOrigin), nameof(FloatingOrigin.setOffset));
        }

        private static HashSet<int> activePS = new HashSet<int>(200);

        private static void FloatingOrigin_setOffset_Override(FloatingOrigin fo, Vector3d refPos, Vector3d nonFrame)
        {
            if (refPos.IsInvalid())
                return;

            if (double.IsInfinity(refPos.sqrMagnitude))
                return;

            fo.SetOffsetThisFrame = true;
            fo.offset = refPos;
            fo.reverseoffset = new Vector3d(0.0 - refPos.x, 0.0 - refPos.y, 0.0 - refPos.z);
            fo.offsetNonKrakensbane = fo.offset + nonFrame;

            Vector3 offsetF = fo.offset;
            Vector3 offsetNonKrakensbaneF = fo.offsetNonKrakensbane;
            float deltaTime = Time.deltaTime;
            Vector3 frameVelocity = Krakensbane.GetFrameVelocity();

            activePS.Clear();

            List<CelestialBody> bodies = FlightGlobals.Bodies;
            for (int i = bodies.Count; i-- > 0;)
                bodies[i].position -= fo.offsetNonKrakensbane;

            bool needCoMRecalc = fo.offset.sqrMagnitude > fo.CoMRecalcOffsetMaxSqr;
            List<Vessel> vessels = FlightGlobals.Vessels;

            for (int i = vessels.Count; i-- > 0;)
            {
                Vessel vessel = vessels[i];

                if (vessel.state == Vessel.State.DEAD)
                    continue;

                Vector3d vesselOffset = (!vessel.loaded || vessel.packed || vessel.LandedOrSplashed) ? fo.offsetNonKrakensbane : fo.offset;
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

                            if (particleSystem.IsNullOrDestroyed())
                                continue;

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
            for (int i = fo.particleSystems.Count; i-- > 0;)
            {
                ParticleSystem particleSystem = fo.particleSystems[i];
                if (particleSystem.IsNullOrDestroyed() || activePS.Contains(particleSystem.GetInstanceIDFast()))
                {
                    fo.particleSystems.RemoveAt(i);
                    continue;
                }

                int particleCount = particleSystem.particleCount;
                if (particleCount == 0)
                    continue;

                if (particleSystem.main.simulationSpace != ParticleSystemSimulationSpace.World)
                    continue;

                if (activePS.Contains(particleSystem.GetInstanceIDFast()))
                {
                    fo.particleSystems.RemoveAt(i);
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
            if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.radarAltitude < fo.altToStopMovingExplosions)
                FXMonger.OffsetPositions(-fo.offsetNonKrakensbane);

            for (int i = FlightGlobals.physicalObjects.Count; i-- > 0;)
            {
                physicalObject physicalObject = FlightGlobals.physicalObjects[i];
                if (physicalObject.IsNotNullOrDestroyed())
                {
                    Transform obj = physicalObject.transform;
                    obj.position -= offsetF;
                }
            }

            FloatingOrigin.TerrainShaderOffset += fo.offsetNonKrakensbane;
            GameEvents.onFloatingOriginShift.Fire(fo.offset, nonFrame);
        }
    }
}
