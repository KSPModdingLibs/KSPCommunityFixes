using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KSP.Localization;

namespace KSPCommunityFixes
{
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
                UnityEngine.Debug.LogError($"Failed to generate localization file : {e}");
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
            UnityEngine.Debug.Log($"[KSPCF] Localization file generated: \"{path}\"");
            ScreenMessages.PostScreenMessage($"KSP Community Fixes\nLocalization file generated\n\"{path}\"", 60f, ScreenMessageStyle.UPPER_LEFT);
        }
    }
}
