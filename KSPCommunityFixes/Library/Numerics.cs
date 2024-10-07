using System;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Random = System.Random;

namespace KSPCommunityFixes.Library
{
    internal static class Numerics
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Zero(ref this Vector3 v)
        {
            v.x = 0f; 
            v.y = 0f; 
            v.z = 0f;
        }

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
        /// mutating add, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add(ref this Vector3d v, double x, double y, double z)
        {
            v.x += x;
            v.y += y;
            v.z += z;
        }

        /// <summary>
        /// mutating add, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add(ref this Vector3 v, Vector3 other)
        {
            v.x += other.x;
            v.y += other.y;
            v.z += other.z;
        }

        /// <summary>
        /// mutating add, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add(ref this Vector3 v, float x, float y, float z)
        {
            v.x += x;
            v.y += y;
            v.z += z;
        }

        /// <summary>
        /// mutating substract, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Substract(ref this Vector3d v, Vector3d other)
        {
            v.x -= other.x;
            v.y -= other.y;
            v.z -= other.z;
        }

        /// <summary>
        /// mutating substract, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Substract(ref this Vector3d v, double x, double y, double z)
        {
            v.x -= x;
            v.y -= y;
            v.z -= z;
        }

        /// <summary>
        /// mutating substract, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Substract(ref this Vector3 v, Vector3 other)
        {
            v.x -= other.x;
            v.y -= other.y;
            v.z -= other.z;
        }

        /// <summary>
        /// mutating substract, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Substract(ref this Vector3 v, float x, float y, float z)
        {
            v.x -= x;
            v.y -= y;
            v.z -= z;
        }

        /// <summary>
        /// mutating multiply, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Multiply(ref this Vector3d v, double scalar)
        {
            v.x *= scalar;
            v.y *= scalar;
            v.z *= scalar;
        }

        /// <summary>
        /// mutating multiply, ~10 times faster than using the + operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Multiply(ref this Vector3 v, float scalar)
        {
            v.x *= scalar;
            v.y *= scalar;
            v.z *= scalar;
        }

        /// <summary>
        /// Clamp a value between 0.0 and 1.0
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp01(double value)
        {
            if (value < 0.0)
                return 0.0;

            if (value > 1.0)
                return 1.0;

            return value;
        }

        /// <summary>
        /// Double backed clamped lerp
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Lerp(double a, double b, double t)
        {
            if (t <= 0.0)
                return a;

            if (t >= 1.0)
                return a + (b - a);

            return a + (b - a) * t;
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

        /// <summary>
        /// Faster approximate alternative to Math.Pow(), by a factor 5-6. Max error around 5%, average error around 1.7%
        /// </summary>
        public static unsafe double FastPow(double value, double exponent)
        {
            // exponentiation by squaring
            double r = 1.0;
            int exp = (int)exponent;
            double _base = value;
            while (exp != 0)
            {
                if ((exp & 1) != 0)
                {
                    r *= _base;
                }
                _base *= _base;
                exp >>= 1;
            }

            // use the IEEE 754 trick for the fraction of the exponent
            double b_faction = exponent - (int)exponent;
            long tmp = *(long*)&value;
            long tmp2 = (long)(b_faction * (tmp - 4606921280493453312L)) + 4606921280493453312L;
            return r * *(double*)&tmp2;
        }
    }

    /// <summary>
    /// A readonly 4x3 double-backed matrix for transform operations
    /// </summary>
    public readonly struct TransformMatrix
    {
        public readonly double m00, m01, m02, m03;
        public readonly double m10, m11, m12, m13;
        public readonly double m20, m21, m22, m23;

        public TransformMatrix(double m00, double m01, double m02, double m03, double m10, double m11, double m12, double m13, double m20, double m21, double m22, double m23)
        {
            this.m00 = m00;
            this.m01 = m01;
            this.m02 = m02;
            this.m03 = m03;
            this.m10 = m10;
            this.m11 = m11;
            this.m12 = m12;
            this.m13 = m13;
            this.m20 = m20;
            this.m21 = m21;
            this.m22 = m22;
            this.m23 = m23;
        }

        public TransformMatrix(ref Matrix4x4 transformMatrix)
        {
            m00 = transformMatrix.m00;
            m01 = transformMatrix.m01;
            m02 = transformMatrix.m02;
            m03 = transformMatrix.m03;

            m10 = transformMatrix.m10;
            m11 = transformMatrix.m11;
            m12 = transformMatrix.m12;
            m13 = transformMatrix.m13;

            m20 = transformMatrix.m20;
            m21 = transformMatrix.m21;
            m22 = transformMatrix.m22;
            m23 = transformMatrix.m23;
        }

        public TransformMatrix(ref Matrix4x4D transformMatrix)
        {
            m00 = transformMatrix.m00;
            m01 = transformMatrix.m01;
            m02 = transformMatrix.m02;
            m03 = transformMatrix.m03;

            m10 = transformMatrix.m10;
            m11 = transformMatrix.m11;
            m12 = transformMatrix.m12;
            m13 = transformMatrix.m13;

            m20 = transformMatrix.m20;
            m21 = transformMatrix.m21;
            m22 = transformMatrix.m22;
            m23 = transformMatrix.m23;
        }

        /// <summary>
        /// Local to world space transform matrix
        /// </summary>
        public static TransformMatrix LocalToWorld(Transform transform)
        {
            Matrix4x4 matrix = transform.localToWorldMatrix;
            return new TransformMatrix(
                matrix.m00, matrix.m01, matrix.m02, matrix.m03,
                matrix.m10, matrix.m11, matrix.m12, matrix.m13,
                matrix.m20, matrix.m21, matrix.m22, matrix.m23);
        }

        /// <summary>
        /// World to local space (inverse) transform matrix
        /// </summary>
        public static TransformMatrix WorldToLocal(Transform transform)
        {
            Matrix4x4 matrix = transform.worldToLocalMatrix;
            return new TransformMatrix(
                matrix.m00, matrix.m01, matrix.m02, matrix.m03,
                matrix.m10, matrix.m11, matrix.m12, matrix.m13,
                matrix.m20, matrix.m21, matrix.m22, matrix.m23);
        }

        /// <summary>
        /// Transform point
        /// </summary>
        public void MultiplyPoint3x4(ref Vector3d point)
        {
            double x = point.x;
            double y = point.y;
            double z = point.z;
            point.x = m00 * x + m01 * y + m02 * z + m03;
            point.y = m10 * x + m11 * y + m12 * z + m13;
            point.z = m20 * x + m21 * y + m22 * z + m23;
        }

        /// <summary>
        /// Transform point
        /// </summary>
        public Vector3d MultiplyPoint3x4(Vector3d point)
        {
            return new Vector3d(
                m00 * point.x + m01 * point.y + m02 * point.z + m03,
                m10 * point.x + m11 * point.y + m12 * point.z + m13,
                m20 * point.x + m21 * point.y + m22 * point.z + m23);
        }

        /// <summary>
        /// Transform point
        /// </summary>
        public Vector3d MultiplyPoint3x4(double x, double y, double z)
        {
            return new Vector3d(
                m00 * x + m01 * y + m02 * z + m03,
                m10 * x + m11 * y + m12 * z + m13,
                m20 * x + m21 * y + m22 * z + m23);
        }

        /// <summary>
        /// Transform vector
        /// </summary>
        public void MultiplyVector(ref Vector3d point)
        {
            double x = point.x;
            double y = point.y;
            double z = point.z;
            point.x = m00 * x + m01 * y + m02 * z;
            point.y = m10 * x + m11 * y + m12 * z;
            point.z = m20 * x + m21 * y + m22 * z;
        }

        /// <summary>
        /// Transform vector
        /// </summary>
        public Vector3d MultiplyVector(Vector3d point)
        {
            return new Vector3d(
                m00 * point.x + m01 * point.y + m02 * point.z,
                m10 * point.x + m11 * point.y + m12 * point.z,
                m20 * point.x + m21 * point.y + m22 * point.z);
        }

        /// <summary>
        /// Transform vector
        /// </summary>
        public Vector3d MultiplyVector(double x, double y, double z)
        {
            return new Vector3d(
                m00 * x + m01 * y + m02 * z,
                m10 * x + m11 * y + m12 * z,
                m20 * x + m21 * y + m22 * z);
        }
    }
}
