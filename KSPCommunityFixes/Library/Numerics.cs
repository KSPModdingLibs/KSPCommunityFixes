using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace KSPCommunityFixes.Library
{
    internal static class Numerics
    {
        /// <summary>
        /// mutating add, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add(ref this Vector3d v, Vector3d other)
        {
            v.x += other.x;
            v.y += other.y;
            v.z += other.z;
        }

        /// <summary>
        /// Return a QuaternionD representing a rotation from a Vector3d to another
        /// </summary>
        public static QuaternionD FromToRotation(Vector3d from, Vector3d to)
        {
            double d = Vector3d.Dot(from, to);
            double qw = Math.Sqrt(from.sqrMagnitude * to.sqrMagnitude) + d;
            double x, y, z, sqrMag;
            if (qw < 1e-12)
            {
                // vectors are 180 degrees apart
                x = from.x;
                y = from.y;
                z = -from.z;
                sqrMag = x * x + y * y + z * z;
                if (sqrMag != 1.0)
                {
                    double invNorm = 1.0 / Math.Sqrt(sqrMag);
                    x *= invNorm;
                    y *= invNorm;
                    z *= invNorm;
                }
                return new QuaternionD(x, y, z, 0.0);
            }

            Vector3d axis = Vector3d.Cross(from, to);
            x = axis.x;
            y = axis.y;
            z = axis.z;
            sqrMag = x * x + y * y + z * z + qw * qw;
            if (sqrMag != 1.0)
            {
                double invNorm = 1.0 / Math.Sqrt(sqrMag);
                x *= invNorm;
                y *= invNorm;
                z *= invNorm;
                qw *= invNorm;
            }

            return new QuaternionD(x, y, z, qw);
        }

        /// <summary>
        /// Cast a Matrix4x4 to a Matrix4x4D
        /// </summary>
        public static Matrix4x4D ToMatrix4x4D(ref this Matrix4x4 m)
        {
            return new Matrix4x4D(
                m.m00, m.m01, m.m02, m.m03,
                m.m10, m.m11, m.m12, m.m13,
                m.m20, m.m21, m.m22, m.m23,
                m.m30, m.m31, m.m32, m.m33);
        }

        /// <summary>
        /// Transforms a direction by this matrix.
        /// </summary>
        public static Vector3 MultiplyVector(this Matrix4x4 m, float x, float y, float z)
        {
            return new Vector3(
                m.m00 * x + m.m01 * y + m.m02 * z,
                m.m10 * x + m.m11 * y + m.m12 * z,
                m.m20 * x + m.m21 * y + m.m22 * z);
        }

        /// <summary>
        /// Transforms a direction by this matrix.
        /// </summary>
        public static Vector3d MultiplyVector(ref this Matrix4x4D m, Vector3d vector)
        {
            return new Vector3d(
                m.m00 * vector.x + m.m01 * vector.y + m.m02 * vector.z,
                m.m10 * vector.x + m.m11 * vector.y + m.m12 * vector.z,
                m.m20 * vector.x + m.m21 * vector.y + m.m22 * vector.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3d MultiplyPoint3x4(ref this Matrix4x4D m, double x, double y, double z)
        {
            return new Vector3d(
                m.m00 * x + m.m01 * y + m.m02 * z + m.m03,
                m.m10 * x + m.m11 * y + m.m12 * z + m.m13,
                m.m20 * x + m.m21 * y + m.m22 * z + m.m23);
        }

        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        public static bool IsPowerOfTwo(int value) => (value & (value - 1)) == 0 && value > 0;

        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        public static bool IsPowerOfTwo(uint value) => (value & (value - 1)) == 0 && value != 0;
    }
}
