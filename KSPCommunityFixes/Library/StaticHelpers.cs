using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.UI.TooltipTypes;
using PartToolsLib;
using UnityEngine;

namespace KSPCommunityFixes
{
    static class StaticHelpers
    {
        public static string HumanReadableBytes(long bytes)
        {
            // Get absolute value
            long absoluteBytes = (bytes < 0 ? -bytes : bytes);
            // Determine the suffix and readable value
            string suffix;
            double readable;
            if (absoluteBytes >= 0x10000000000) // Terabyte
            {
                suffix = "TiB";
                readable = (bytes >> 30);
            }
            else if (absoluteBytes >= 0x40000000) // Gigabyte
            {
                suffix = "GiB";
                readable = (bytes >> 20);
            }
            else if (absoluteBytes >= 0x100000) // Megabyte
            {
                suffix = "MiB";
                readable = (bytes >> 10);
            }
            else if (absoluteBytes >= 0x400) // Kilobyte
            {
                suffix = "KiB";
                readable = bytes;
            }
            else
            {
                return bytes.ToString("0 B"); // Byte
            }
            // Divide by 1024 to get fractional value
            readable /= 1024.0;
            // Return formatted number with suffix
            return readable.ToString("0.### ") + suffix;
        }

        private static Tooltip_Text _tooltipTextPrefab;

        public static TooltipController_Text AddUITooltip(GameObject go)
        {
            TooltipController_Text tooltip = go.AddComponent<TooltipController_Text>();
            tooltip.prefab = _tooltipTextPrefab ??= AssetBase.GetPrefab<Tooltip_Text>("Tooltip_Text");
            return tooltip;
        }

        public static bool EditPartModuleKSPFieldAttributes(Type partModuleType, string fieldName, Action<KSPField> editAction)
        {
            BaseFieldList<BaseField, KSPField>.ReflectedData reflectedData;
            try
            {
                MethodInfo BaseFieldList_GetReflectedAttributes = AccessTools.Method(typeof(BaseFieldList), "GetReflectedAttributes");
                reflectedData = (BaseFieldList<BaseField, KSPField>.ReflectedData)BaseFieldList_GetReflectedAttributes.Invoke(null, new object[] { partModuleType, false });
            }
            catch
            {
                return false;
            }

            for (int i = 0; i < reflectedData.fields.Count; i++)
            {
                if (reflectedData.fields[i].Name == fieldName)
                {
                    editAction.Invoke(reflectedData.fieldAttributes[i]);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        public static bool IsPowerOfTwo(int value) => (value & (value - 1)) == 0 && value > 0;

        /// <summary>
        /// Evaluate whether a given integral value is a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        public static bool IsPowerOfTwo(uint value) => (value & (value - 1)) == 0 && value != 0;

        public static Matrix4x4D ToMatrix4x4D(ref this Matrix4x4 m)
        {
            return new Matrix4x4D(
                m.m00, m.m01, m.m02, m.m03,
                m.m10, m.m11, m.m12, m.m13,
                m.m20, m.m21, m.m22, m.m23,
                m.m30, m.m31, m.m32, m.m33);
        }

        public static Vector3d MultiplyVector(ref this Matrix4x4D m, Vector3d vector)
        {
            return new Vector3d(
                m.m00 * vector.x + m.m01 * vector.y + m.m02 * vector.z,
                m.m10 * vector.x + m.m11 * vector.y + m.m12 * vector.z,
                m.m20 * vector.x + m.m21 * vector.y + m.m22 * vector.z);
        }

        /// <summary>
        /// 3 times faster than using the + operator
        /// </summary>
        /// <param name="v"></param>
        /// <param name="other"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add(ref this Vector3d v, Vector3d other)
        {
            v.x += other.x;
            v.y += other.y;
            v.z += other.z;
        }

    }
}
