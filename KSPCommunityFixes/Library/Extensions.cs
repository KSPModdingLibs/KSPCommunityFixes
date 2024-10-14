using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace KSPCommunityFixes
{
    static class Extensions
    {
        /// <summary>
        /// Get an assembly qualified type name in the "assemblyName:typeName" format
        /// </summary>
        public static string AssemblyQualifiedName(this object obj)
        {
            Type type = obj.GetType();
            return $"{type.Assembly.GetName().Name}:{type.Name}";
        }

        public static bool IsPAWOpen(this Part part)
        {
            return part.PartActionWindow.IsNotNullOrDestroyed() && part.PartActionWindow.isActiveAndEnabled;
        }
    }

    static class ParticleBuffer
    {
        private static NativeArray<ParticleSystem.Particle> particleBuffer = new NativeArray<ParticleSystem.Particle>(1000, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        private static long particleSize = UnsafeUtility.SizeOf<ParticleSystem.Particle>();

        /// <summary>
        /// Get a native array of active Particle in this ParticleSystem
        /// </summary>
        /// <param name="particleCount">The amount of particles in the system, usually ParticleSystem.particleCount. After returning, this will be the amount of active particles, which might be lower.</param>
        /// <returns></returns>
        public static NativeArray<ParticleSystem.Particle> GetParticlesNativeArray(this ParticleSystem particleSystem, ref int particleCount)
        {
            if (particleBuffer.Length < particleCount)
            {
                particleBuffer.Dispose();
                particleBuffer = new NativeArray<ParticleSystem.Particle>(particleCount * 2, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            particleCount = particleSystem.GetParticles(particleBuffer);
            return particleBuffer;
        }

        /// <summary>
        /// Get the position of the particle at the specified index, avoiding to have to make copies of the (huge) particle struct
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Vector3 GetParticlePosition(this NativeArray<ParticleSystem.Particle> buffer, int particleIndex)
        {
            // note : the position Vector3 is the first field of the struct
            return *(Vector3*)((byte*)buffer.m_Buffer + particleIndex * particleSize);
        }

        /// <summary>
        /// Set the position of the particle at the specified index, avoiding to have to make copies of the (huge) particle struct
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetParticlePosition(this NativeArray<ParticleSystem.Particle> buffer, int particleIndex, Vector3 position)
        {
            // note : the position Vector3 is the first field of the struct
            *(Vector3*)((byte*)buffer.m_Buffer + particleIndex * particleSize) = position;
        }
    }
}
