using System;
using System.Reflection;
using HarmonyLib;
using KSP.UI.TooltipTypes;
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
    }
}
