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

            // Glyphs requested from now on are rendered from that font, for every skin, style and uGUI text
            // using the built-in font. Glyphs missing from it are still taken from Unity's list of substitutes.
            arial.fontNames = new[] { fontName };

            // Force a rebuild if any glyphs were already generated from the wrong font.
            bool rebuilt = arial.characterInfo.Length > 0 && ForceTextureRebuild(arial);

            Debug.Log($"[KSPCF:LinuxArialFallback] Using \"{fontName}\" for the built-in Arial font{(rebuilt ? ", rebuilt its texture" : "")}");
        }

        // Request a glyph of increasing size until it doesn't fit in the font texture. Unity then rebuilds the
        // texture with only the glyphs of that request, and every other glyph is rendered again when next requested.
        private static bool ForceTextureRebuild(Font font)
        {
            bool rebuilt = false;
            void OnTextureRebuilt(Font rebuiltFont)
            {
                if (rebuiltFont.RefEquals(font))
                    rebuilt = true;
            }

            Font.textureRebuilt += OnTextureRebuilt;
            try
            {
                int maxSize = 2 * font.material.mainTexture.height;
                for (int size = 16; !rebuilt && size <= maxSize; size *= 2)
                    font.RequestCharactersInTexture("W", size);
            }
            finally
            {
                Font.textureRebuilt -= OnTextureRebuilt;
            }

            if (!rebuilt)
                Debug.LogWarning("[KSPCF:LinuxArialFallback] Couldn't rebuild the built-in Arial font texture, text already drawn with it may keep the substitute font");

            return rebuilt;
        }
    }
}
