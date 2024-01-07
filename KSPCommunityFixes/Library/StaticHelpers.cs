using System;
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
    }
}
