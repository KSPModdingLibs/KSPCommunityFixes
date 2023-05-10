using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes
{
    static class Extensions
    {
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
        /// Get an assembly qualified type name in the "assemblyName:typeName" format
        /// </summary>
        public static string AssemblyQualifiedName(this object obj)
        {
            Type type = obj.GetType();
            return $"{type.Assembly.GetName().Name}:{type.Name}";
        }
    }
}
