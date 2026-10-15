using System;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    // The default IMGUI skin, used by most mod windows, draws with Unity's default "Arial" font.
    // Arial usually isn't installed on Linux. The default backup list starts with DejaVu Sans which
    // is wider than Arial, so text wraps or clips where it doesn't on Windows. Implement a new list
    // of fall back fonts with Arial-clone metrics, and use that list where possible.
    internal class LinuxArialFallback : BasePatch
    {
        // Fonts metric-compatible with Arial, by order of preference.
        private static readonly string[] metricCompatibleFonts = { "Liberation Sans", "Arimo", "Nimbus Sans" };

        private string fontName;

        protected override bool CanApplyPatch(out string reason)
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer)
            {
                reason = "not running on Linux";
                return false;
            }

            string[] installedFonts = Font.GetOSInstalledFontNames();

            if (Array.IndexOf(installedFonts, "Arial") >= 0)
            {
                reason = "Arial is installed";
                return false;
            }

            foreach (string candidate in metricCompatibleFonts)
            {
                if (Array.IndexOf(installedFonts, candidate) >= 0)
                {
                    fontName = candidate;
                    reason = null;
                    return true;
                }
            }

            reason = "no font metric-compatible with Arial is installed";
            return false;
        }

        protected override void ApplyPatches() { }

        protected override void OnPatchApplied()
        {
            Font arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (arial.IsNullOrDestroyed())
                return;

            // Glyphs missing from that font are still taken from Unity's list of substitutes.
            string[] fontNames = { fontName };

            // Effective if nothing has used the font yet, including uGUI texts using it.
            arial.fontNames = fontNames;

            // A copy keeps the line spacing of the built-in font, a font created from the OS font doesn't.
            Font replacement = UnityEngine.Object.Instantiate(arial);
            replacement.name = arial.name;
            replacement.fontNames = fontNames;
            replacement.hideFlags = HideFlags.DontUnloadUnusedAsset;

            // The default skin, and any copy of it made by a mod
            int replaced = 0;
            foreach (GUISkin skin in Resources.FindObjectsOfTypeAll<GUISkin>())
            {
                if (skin.font.RefEquals(arial))
                {
                    skin.font = replacement;
                    replaced++;
                }

                foreach (GUIStyle style in skin)
                {
                    if (style.font.RefEquals(arial))
                    {
                        style.font = replacement;
                        replaced++;
                    }
                }
            }

            Debug.Log($"[KSPCF:LinuxArialFallback] Using \"{fontName}\" for the built-in Arial font, replaced it in {replaced} skins and styles");
        }
    }
}
