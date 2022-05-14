using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KSP.Localization;

namespace KSPCommunityFixes
{
    static class UnityExtensions
    {
        /// <summary>
        /// Return "null" when the UnityEngine.Object instance is either null or destroyed/not initialized.<br/>
        /// Allow using null conditional and null coalescing operators with classes deriving from UnityEngine.Object
        /// while keeping the "a destroyed object is equal to null" Unity concept.<br/>
        /// Example :<br/>
        /// <c>float x = myUnityObject.AsNull()?.myFloatField ?? 0f;</c><br/>
        /// will evaluate to <c>0f</c> when <c>myUnityObject</c> is destroyed, instead of returning the value still
        /// available on the destroyed instance.
        /// </summary>
        public static T AsNull<T>(this T unityObject) where T : UnityEngine.Object
        {
            if (ReferenceEquals(unityObject, null) || unityObject.m_CachedPtr == IntPtr.Zero)
                return null;

            return unityObject;
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
                        staticField.SetValue(null, Localizer.Format("#KSPCF_" + type.Name + "_" + staticField.Name.Substring(4)));
                    }
                }
            }
        }

        public static void GenerateBlankLocCfg(bool defaultAsComments)
        {
            string tab = "  ";

            List<string> lines = new List<string>();
            lines.Add("Localization");
            lines.Add("{");
            lines.Add(tab + "en-us");
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

                        
                        string line = tab + tab + "#KSPCF_" + type.Name + "_" + staticField.Name.Substring(4) + " = ";

                        string defaultString = (string)staticField.GetValue(null);
                        defaultString = defaultString.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
                        if (defaultAsComments)
                        {
                            lines.Add(string.Empty);
                            lines.Add(tab + tab + "// " + defaultString);
                        }
                        else
                        {
                            line += defaultString;
                        }

                        lines.Add(line);
                    }
                }
            }

            lines.Add(tab + "}");
            lines.Add("}");

            File.WriteAllLines(Path.Combine(KSPCommunityFixes.ModPath, "blankLoc.txt"), lines);
        }
    }
}
