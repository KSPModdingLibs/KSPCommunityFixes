using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.UI.TooltipTypes;
using UnityEngine;

namespace KSPCommunityFixes
{
    // see https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide
    internal enum DDSFourCC : uint
    {
        DXT1 = 0x31545844, // "DXT1"
        DXT2 = 0x32545844, // "DXT2"
        DXT3 = 0x33545844, // "DXT3"
        DXT4 = 0x34545844, // "DXT4"
        DXT5 = 0x35545844, // "DXT5"
        BC4U_ATI = 0x31495441, // "ATI1" (actually BC4U)
        BC4U = 0x55344342, // "BC4U"
        BC4S = 0x53344342, // "BC4S"
        BC5U_ATI = 0x32495441, // "ATI2" (actually BC5U)
        BC5U = 0x55354342, // "BC5U"
        BC5S = 0x53354342, // "BC5S"
        RGBG = 0x47424752, // "RGBG"
        GRGB = 0x42475247, // "GRGB"
        UYVY = 0x59565955, // "UYVY"
        YUY2 = 0x32595559, // "YUY2"
        DX10 = 0x30315844, // "DX10", actual DXGI format specified in DX10 header
        R16G16B16A16_UNORM = 36,
        R16G16B16A16_SNORM = 110,
        R16_FLOAT = 111,
        R16G16_FLOAT = 112,
        R16G16B16A16_FLOAT = 113,
        R32_FLOAT = 114,
        R32G32_FLOAT = 115,
        R32G32B32A32_FLOAT = 116,
        CxV8U8 = 117,
    }

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
    }
}
