using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using KSP.Localization;
using UnityEngine;

namespace KSPCommunityFixes
{
#pragma warning disable IDE0041 // Use 'is null' check
    static class UnityExtensions
    {
        /// <summary>
        /// Perform a true reference equality comparison between two UnityEngine.Object references.<br/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RefEquals(this UnityEngine.Object unityObject, UnityEngine.Object otherUnityObject)
        {
            return ReferenceEquals(unityObject, otherUnityObject);
        }

        /// <summary>
        /// Perform a true reference inequality comparison between two UnityEngine.Object references.<br/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RefNotEquals(this UnityEngine.Object unityObject, UnityEngine.Object otherUnityObject)
        {
            return !ReferenceEquals(unityObject, otherUnityObject);
        }

        /// <summary>
        /// Equivalent as the Unity <c>==</c> operator, but 6-8 times faster.<br/>
        /// Use this if you want to perform an equality check where :<br/>
        /// - a destroyed <c>UnityEngine.Object</c> instance is considered equal to <c>null</c><br/>
        /// - two different destroyed <c>UnityEngine.Object</c> instances are not considered equal
        /// </summary>
        public static bool NotDestroyedRefEquals(this UnityEngine.Object unityObject, UnityEngine.Object otherUnityObject)
        {
            if (ReferenceEquals(unityObject, otherUnityObject))
                return true;

            if (ReferenceEquals(otherUnityObject, null) && unityObject.m_CachedPtr == IntPtr.Zero)
                return true;

            if (ReferenceEquals(unityObject, null) && otherUnityObject.m_CachedPtr == IntPtr.Zero)
                return true;

            return false;
        }

        /// <summary>
        /// Equivalent as the Unity <c>!=</c> operator, but 6-8 times faster.<br/>
        /// Use this if you want to perform an equality check where :<br/>
        /// - a destroyed <c>UnityEngine.Object</c> instance is considered equal to <c>null</c><br/>
        /// - two different destroyed <c>UnityEngine.Object</c> instances are not considered equal
        /// </summary>
        public static bool NotDestroyedRefNotEquals(this UnityEngine.Object unityObject, UnityEngine.Object otherUnityObject)
        {
            if (ReferenceEquals(unityObject, otherUnityObject))
                return false;

            if (ReferenceEquals(otherUnityObject, null) && unityObject.m_CachedPtr == IntPtr.Zero)
                return false;

            if (ReferenceEquals(unityObject, null) && otherUnityObject.m_CachedPtr == IntPtr.Zero)
                return false;

            return true;
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> instance is destroyed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDestroyed(this UnityEngine.Object unityObject)
        {
            return unityObject.m_CachedPtr == IntPtr.Zero;
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> reference is <c>null</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullRef(this UnityEngine.Object unityObject)
        {
            return ReferenceEquals(unityObject, null);
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> reference is not <c>null</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNotNullRef(this UnityEngine.Object unityObject)
        {
            return !ReferenceEquals(unityObject, null);
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> reference is <c>null</c> or if the instance is destroyed<br/>
        /// Equivalent as testing <c><paramref name="unityObject"/> == null</c> but 4-5 times faster.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrDestroyed(this UnityEngine.Object unityObject)
        {
            return ReferenceEquals(unityObject, null) || unityObject.m_CachedPtr == IntPtr.Zero;
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> reference is not <c>null</c> and the instance is not destroyed<br/>
        /// Equivalent as testing <c><paramref name="unityObject"/> != null</c> but 4-5 times faster.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNotNullOrDestroyed(this UnityEngine.Object unityObject)
        {
            return !ReferenceEquals(unityObject, null) && unityObject.m_CachedPtr != IntPtr.Zero;
        }

        /// <summary>
        /// Return <c>null</c> when this <paramref name="unityObject"/> reference is <c>null</c> or destroyed, otherwise return the <paramref name="unityObject"/> instance<br/>
        /// Allow using null conditional and null coalescing operators with <c>UnityEngine.Object</c> derivatives while conforming to the "a destroyed object is equal to null" Unity concept.<br/>
        /// Example :<br/>
        /// <c>float x = myUnityObject.DestroyedAsNull()?.myFloatField ?? 0f;</c><br/>
        /// will evaluate to <c>0f</c> when <c>myUnityObject</c> is destroyed, instead of returning the value still available on the managed instance.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T DestroyedAsNull<T>(this T unityObject) where T : UnityEngine.Object
        {
            if (ReferenceEquals(unityObject, null) || unityObject.m_CachedPtr == IntPtr.Zero)
                return null;

            return unityObject;
        }
    }
#pragma warning restore IDE0041 // Use 'is null' check

    static class Utils
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
    }

    static class LocalizationUtils
    {
        public static void ParseLocalization()
        {
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                foreach (FieldInfo staticField in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (staticField.Name.StartsWith("LOC_", StringComparison.Ordinal))
                    {
                        string tag = "#KSPCF_" + type.Name + "_" + staticField.Name.Substring(4);
                        if (Localizer.Tags.ContainsKey(tag))
                            staticField.SetValue(null, Localizer.Format(tag));
                    }
                }
            }
        }

        public static void GenerateLocTemplateIfRequested()
        {
            UrlDir.UrlConfig[] featuresNodes = GameDatabase.Instance.GetConfigs(KSPCommunityFixes.CONFIGNODE_NAME);

            string generateLocTemplate = null;
            if (featuresNodes == null || featuresNodes.Length != 1 || !featuresNodes[0].config.TryGetValue("GenerateLocTemplate", ref generateLocTemplate))
                return;

            try
            {
                GenerateLocTemplate(generateLocTemplate);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to generate localization file : {e}");
                return;
            }
        }

        public static void GenerateLocTemplate(string langCode = "en-us")
        {
            bool isEnglishLoc = langCode == "en-us";

            Dictionary<string, string> langLoc = null;
            if (!isEnglishLoc)
            {
                ConfigNode locNode = null;
                UrlDir.UrlConfig[] locConfigs = GameDatabase.Instance.GetConfigs("Localization");
                foreach (UrlDir.UrlConfig locConfig in locConfigs)
                {
                    if (!locConfig.url.StartsWith("KSPCommunityFixes"))
                        continue;

                    if (!locConfig.config.TryGetNode(langCode, ref locNode))
                        continue;

                    langLoc = new Dictionary<string, string>(locNode.values.Count);

                    foreach (ConfigNode.Value value in locNode.values)
                    {
                        string valueString = value.value.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
                        langLoc.Add(value.name, valueString);
                    }

                    break;
                }
            }

            string tab = "  ";

            List<string> lines = new List<string>();
            lines.Add("Localization");
            lines.Add("{");
            lines.Add(tab + langCode);
            lines.Add(tab + "{");

            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                bool headerAdded = false;
                foreach (FieldInfo staticField in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (staticField.Name.StartsWith("LOC_", StringComparison.Ordinal))
                    {
                        if (!headerAdded)
                        {
                            lines.Add(string.Empty);
                            lines.Add(tab + tab + "// " + type.Name);
                            lines.Add(string.Empty);
                            headerAdded = true;
                        }


                        string configValueName = "#KSPCF_" + type.Name + "_" + staticField.Name.Substring(4);
                        string line = tab + tab + configValueName + " = ";

                        string englishValue = (string)staticField.GetValue(null);
                        englishValue = englishValue.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

                        if (isEnglishLoc)
                        {
                            line += englishValue;
                        }
                        else 
                        {
                            lines.Add(string.Empty);
                            lines.Add(tab + tab + "// " + englishValue);

                            if (langLoc != null && langLoc.Count > 0)
                            {
                                if (langLoc.TryGetValue(configValueName, out string translatedString))
                                    line += translatedString;
                                else
                                    line += "MISSINGLOC";
                            }
                        }

                        lines.Add(line);
                    }
                }
            }

            lines.Add(tab + "}");
            lines.Add("}");

            string path = Path.Combine(KSPCommunityFixes.ModPath, "Localization", $"{langCode}.cfg.generatedLoc");
            File.WriteAllLines(path, lines);
            Debug.Log($"[KSPCF] Localization file generated: \"{path}\"");
            ScreenMessages.PostScreenMessage($"KSP Community Fixes\nLocalization file generated\n\"{path}\"", 60f, ScreenMessageStyle.UPPER_LEFT);
        }
    }
}
