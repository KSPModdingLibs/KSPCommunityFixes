/*
Reimplementation of the stock Localizer.Format() methods which :
- Don't allocate an useless array for the parameterless overload
- Do a much faster unescape '\n', '"', and '\t' operation
- Don't allocate an immediately thrown away list for the overloads with parameters
 */

using HarmonyLib;
using KSP.Localization;
using Lingoona;
using System.Collections.Generic;
using System.Threading;
using Debug = UnityEngine.Debug;
using NativeMethods = Lingoona.NativeMethods;

namespace KSPCommunityFixes.Performance
{
    internal class LocalizerPerf : BasePatch
    {
        private static Thread mainThread;

        private static readonly Dictionary<(string, string),string> Cache = new Dictionary<(string, string), string>();
        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            mainThread = Thread.CurrentThread;

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Localizer), nameof(Localizer.Format), new[] { typeof(string) }),
                this, nameof(Localizer_FormatNoParams_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Localizer), nameof(Localizer.Format), new[] { typeof(string), typeof(string[]) }),
                this, nameof(Localizer_FormatStringParams_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Localizer), nameof(Localizer.Format), new[] { typeof(string), typeof(object[]) }),
                this, nameof(Localizer_FormatObjectParams_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Localizer), nameof(Localizer.TranslateBranch)),
                this));
        }

        private static bool Localizer_FormatNoParams_Prefix(string template, out string __result)
        {
            if (Localizer.ShowKeysOnScreen)
            {
                __result = template;
                return false;
            }
            Localizer localizer = Localizer.Instance;
            if (localizer == null || string.IsNullOrEmpty(template))
            {
                __result = string.Empty;
                return false;
            }

            if (!localizer.tagValues.TryGetValue(template, out __result))
                __result = template;

            __result = UnescapeFormattedString(__result);

            if (Localizer.debugWriteMissingKeysToLog)
                DebugWriteMissingKeysToLog(__result);

            return false;
        }

        private static bool Localizer_FormatStringParams_Prefix(string template, string[] list, out string __result)
        {
            if (Localizer.ShowKeysOnScreen)
            {
                __result = template;
                return false;
            }

            Localizer localizer = Localizer.Instance;
            if (localizer == null || string.IsNullOrEmpty(template))
            {
                __result = string.Empty;
                return false;
            }

            (string, string) cacheKey = (template, string.Join(".",list));
            
            if(Cache.TryGetValue(cacheKey, out __result))
            {
                return false;
            }

            if (localizer.tagValues.TryGetValue(template, out string localizedTemplate))
                template = localizedTemplate;

            int argCount = list.Length;

            for (int i = 0; i < argCount; i++)
            {
                string arg = list[i];
                if (arg == null)
                    list[i] = string.Empty;
                else if (arg.Length > 0 && localizer.tagValues.TryGetValue(arg, out string localizedArg))
                    list[i] = localizedArg;
            }

            if (Grammar.available)
                __result = NativeMethods.useGrammar(template, 4096, list, argCount);
            else
                __result = Grammar.useGrammar(template, new List<string>(list)); // should never happen ?

            __result = UnescapeFormattedString(__result);

            if (Localizer.debugWriteMissingKeysToLog)
                DebugWriteMissingKeysToLog(__result);

            Cache.Add(cacheKey, __result);
            return false;
        }

        private static readonly string[] strArray1 = new string[1];
        private static readonly string[] strArray2 = new string[2];
        private static readonly string[] strArray3 = new string[3];
        private static readonly string[] strArray4 = new string[4];

        private static bool Localizer_FormatObjectParams_Prefix(string template, object[] list, out string __result)
        {
            if (Localizer.ShowKeysOnScreen)
            {
                __result = template;
                return false;
            }

            Localizer localizer = Localizer.Instance;
            if (localizer == null || string.IsNullOrEmpty(template))
            {
                __result = string.Empty;
                return false;
            }

            if (localizer.tagValues.TryGetValue(template, out string localizedTemplate))
                template = localizedTemplate;

            int argCount = list.Length;
            string[] argBuffer;

            if (Thread.CurrentThread != mainThread)
            {
                argBuffer = new string[argCount];
            }
            else
            {
                switch (argCount)
                {
                    case 1: argBuffer = strArray1; break;
                    case 2: argBuffer = strArray2; break;
                    case 3: argBuffer = strArray3; break;
                    case 4: argBuffer = strArray4; break;
                    default: argBuffer = new string[argCount]; break;
                }
            }

            for (int i = 0; i < argCount; i++)
            {
                string arg = list[i].ToString();
                if (string.IsNullOrEmpty(arg))
                    argBuffer[i] = string.Empty;
                else if (localizer.tagValues.TryGetValue(arg, out string localizedArg))
                    argBuffer[i] = localizedArg;
                else
                    argBuffer[i] = arg;
            }

            if (Grammar.available)
                __result = NativeMethods.useGrammar(template, 4096, argBuffer, argCount);
            else
                __result = Grammar.useGrammar(template, new List<string>(argBuffer));

            __result = UnescapeFormattedString(__result);

            if (Localizer.debugWriteMissingKeysToLog)
                DebugWriteMissingKeysToLog(__result);

            return false;
        }

        private static bool Localizer_TranslateBranch_Prefix(ConfigNode branchRoot)
        {
            List<ConfigNode> nodes = branchRoot._nodes.nodes;
            for (int i = nodes.Count; i-- > 0;)
            {
                Localizer_TranslateBranch_Prefix(nodes[i]);
            }

            List<ConfigNode.Value> values = branchRoot._values.values;
            for (int j = values.Count; j-- > 0 ;)
            {
                ConfigNode.Value value = values[j];
                Localizer_FormatNoParams_Prefix(value.value, out value.value);
            }

            return false;
        }

        /// <summary>
        /// Faster / minimal allocation alternative to the stock Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\t", "\t") code
        /// </summary>
        internal static unsafe string UnescapeFormattedString(string formattedString)
        {
            int length = formattedString.Length;
            int removed = 0;

            fixed (char* charPtr = &formattedString.m_firstChar)
            {
                for (int i = 0; i < length; i++)
                {
                    if (charPtr[i] == '\\')
                    {
                        i++;
                        if (i < length)
                        {
                            char next = charPtr[i];
                            if (next == 'n' || next == '\"' || next == 't')
                            {
                                removed++;
                            }
                        }
                    }
                }
            }

            if (removed == 0)
                return formattedString;

            int newLength = length - removed;
            string result = new string(' ', newLength);

            fixed (char* charPtr = result)
            {
                for (int i = 0, j = 0; i < newLength; i++, j++)
                {
                    if (formattedString[j] == '\\')
                    {
                        int k = j + 1;
                        if (k < length)
                        {
                            switch (formattedString[k])
                            {
                                case 'n':  charPtr[i] = '\n'; j = k; continue;
                                case '\"': charPtr[i] = '\"'; j = k; continue;
                                case 't':  charPtr[i] = '\t'; j = k; continue;
                            }
                        }
                    }

                    charPtr[i] = formattedString[j];
                }
            }

            return result;
        }

        private static void DebugWriteMissingKeysToLog(string key)
        {
            if (!key.Contains(Localizer.Instance.missingKeyPrefix) && key.ToLower().Contains("#autoloc") && !Localizer.missingKeysList.Contains(key))
            {
                Localizer.missingKeysList.Add(key);
                Debug.LogWarning(Localizer.Instance.missingKeyPrefix + " " + key);
            }
        }
    }
}
