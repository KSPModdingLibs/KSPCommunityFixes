// - Add support for IConfigNode serialization
// - Add support for Guid serialization

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using static ConfigNode;
using Random = System.Random;

namespace KSPCommunityFixes.Modding
{
    class ConfigNodePerf : BasePatch
    {
        static string[] _skipKeys;
        static string[] _skipPrefixes;
        static bool _valid = false;

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            ConfigNode settingsNodeKeys = KSPCommunityFixes.SettingsNode.GetNode("CONFIGNODE_PERF_SKIP_PROCESSING_KEYS");
            ConfigNode settingsNodePrefixes = KSPCommunityFixes.SettingsNode.GetNode("CONFIGNODE_PERF_SKIP_PROCESSING_SUBSTRINGS");

            if (settingsNodeKeys != null)
            {
                _skipKeys = new string[settingsNodeKeys.values.Count];
                int i = 0;
                foreach (ConfigNode.Value v in settingsNodeKeys.values)
                    _skipKeys[i++] = v.value;
            }
            else
                _skipKeys = new string[0];

            if (settingsNodePrefixes != null)
            {
                _skipPrefixes = new string[settingsNodePrefixes.values.Count];
                int i = 0;
                foreach (ConfigNode.Value v in settingsNodePrefixes.values)
                    _skipPrefixes[i++] = v.value;
            }
            else
                _skipPrefixes = new string[0];

            _valid = _skipKeys.Length > 0 || _skipPrefixes.Length > 0;

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ConfigNode), nameof(PreFormatConfig)),
                this));
        }

        // This will fail if nested, so we cache off the old writeLinks.
        private static bool ConfigNode_PreFormatConfig_Prefix(string[] cfgData, ref List<string[]> __result)
        {
            __result = _PreFormatConfig(cfgData);
#if DEBUG_CONFIGNODE_PERF
            var old = OldPreFormat(cfgData);
            string str = string.Empty;
            if (__result.Count != old.Count)
            {
                str = $"\nMismatch in length! Ours {__result.Count}, old {old.Count}";
                for (int i = 0; i < old.Count; ++i)
                {
                    int rL = __result[i].Length;
                    int oL = old[i].Length;
                    str += $"\nLine {i}:";
                    for (int j = 0; j < Math.Max(rL, oL); ++j)
                    {
                        str += "\n";
                        str += j < rL ? __result[i][j] : "<>";
                        str += " |||| ";
                        str += j < oL ? old[i][j] : "<>";
                    }
                }
            }
            else
            {
                for (int i = 0; i < old.Count; ++i)
                {
                    int rL = __result[i].Length;
                    int oL = old[i].Length;
                    if (rL != oL)
                    {
                        str += $"\nLength mismatch on line {i}. Dumping:";
                        for (int j = 0; j < Math.Max(rL, oL); ++j)
                        {
                            str += "\n";
                            str += j < rL ? __result[i][j] : "<>";
                            str += " |||| ";
                            str += j < oL ? old[i][j] : "<>";
                        }
                        break;
                    }
                    for (int j = 0; j < rL; ++j)
                    {
                        if (__result[i][j] != old[i][j])
                        {
                            str += $"\nValue mismatch on line {i}, value {j}:\n{__result[i][j]} |||| {old[i][j]}";
                        }
                    }
                }
            }
            if (str != string.Empty)
            {
                Debug.Log("$$$$ mismatch:" + str);
                __result = old;
            }
#endif
            return false;
        }

        private static void AddKVP(List<string[]> output, string line, int keyStart, int keyLast, int valueStart, int valueLast)
        {
            var pair = new string[2];
            if (keyLast < keyStart)
                pair[0] = string.Empty;
            else
                pair[0] = line.Substring(keyStart, keyLast - keyStart + 1);

            if (valueLast < valueStart)
                pair[1] = string.Empty;
            else
                pair[1] = line.Substring(valueStart, valueLast - valueStart + 1);

            output.Add(pair);
        }

        private static unsafe void ProcessValueRaw(List<string[]> output, string line, char* pszLine, int start, int lineLen, int idxKeyStart, int idxKeyLast)
        {
            int idxValueStart = start;
            for (; idxValueStart < lineLen; ++idxValueStart)
            {
                if (!char.IsWhiteSpace(pszLine[idxValueStart]))
                    break;
            }

            int idxValueLast;
            if (idxValueStart == lineLen)
            {
                // Empty value
                idxValueLast = -1;
            }
            else
            {
                idxValueLast = lineLen - 1;
                for (; idxValueLast > idxValueStart; --idxValueLast)
                {
                    if (!char.IsWhiteSpace(pszLine[idxValueLast]))
                        break;
                }
            }

            AddKVP(output, line, idxKeyStart, idxKeyLast, idxValueStart, idxValueLast);
        }

        private static unsafe void ProcessValue(List<string[]> output, string line, char* pszLine, int start, int lineLen, int idxKeyStart, int idxKeyLast)
        {
            int idxValueStart = start;

            for (; idxValueStart < lineLen; ++idxValueStart)
            {
                char c = pszLine[idxValueStart];
                if (c == '{' || c == '}')
                {
                    // There is nothing left of the brace, so add an empty value.
                    AddKVP(output, line, idxKeyStart, idxKeyLast, 0, -1);
                    // next, add the brace.
                    output.Add(new string[1] { c.ToString() }); // hopefully this is faster than substring
                    // finally, process the rest of the line.
                    ProcessLine(output, line, pszLine, idxValueStart + 1, lineLen);
                    return;
                }

                if (idxValueStart < lineLen - 1 && c == '/' && pszLine[idxValueStart + 1] == '/')
                {
                    lineLen = idxValueStart; // ignore whatever follows
                    break;
                }

                if (!char.IsWhiteSpace(c))
                    break;
            }

            int idxValueLast;
            if (idxValueStart == lineLen)
            {
                // Empty value
                idxValueLast = -1;
            }
            else
            {
                idxValueLast = idxValueStart;
                for (; idxValueLast < lineLen - 1; ++idxValueLast)
                {
                    char c = pszLine[idxValueLast];
                    if (c == '{' || c == '}')
                    {
                        // Welp, we found the end of the value.
                        AddKVP(output, line, idxKeyStart, idxKeyLast, idxValueStart, idxValueLast - 1);
                        // next, add the brace.
                        output.Add(new string[1] { c.ToString() }); // hopefully this is faster than substring
                        // finally, process the rest of the line.
                        ProcessLine(output, line, pszLine, idxValueStart + 1, lineLen);
                        return;
                    }

                    if (c == '/' && pszLine[idxValueLast + 1] == '/')
                    {
                        --idxValueLast;
                        break;
                    }
                }

                for (; idxValueLast > idxValueStart; --idxValueLast)
                {
                    if (!char.IsWhiteSpace(pszLine[idxValueLast]))
                        break;
                }
            }

            AddKVP(output, line, idxKeyStart, idxKeyLast, idxValueStart, idxValueLast);
        }

        private static unsafe void ProcessLine(List<string[]> output, string line, char* pszLine, int start, int lineLen)
        {
            if (start == lineLen)
                return;

            // Find first nonwhitespace char
            int idxKeyStart = start;
            for (; idxKeyStart < lineLen; ++idxKeyStart)
            {
                char c = pszLine[idxKeyStart];
                if (!char.IsWhiteSpace(c))
                    break;
            }

            // If we didn't find any non-whitespace, or it's only a comment, bail
            if (idxKeyStart == lineLen
                || (idxKeyStart < lineLen - 1 && pszLine[idxKeyStart] == '/' && pszLine[idxKeyStart + 1] == '/'))
                return;

            // Find equals, if it exists. We'll divert if we find a brace, and truncate if we find a comment.
            int idxEquals = idxKeyStart;
            int idxKeyLast = idxEquals;
            for (; idxEquals < lineLen; ++idxEquals)
            {
                char c = pszLine[idxEquals];
                if (c == '=')
                    break;

                if (c == '{' || c == '}')
                {
                    // First, process what's left of the brace, using our known first-non-whitespace char
                    ProcessLine(output, line, pszLine, idxKeyStart, idxEquals);
                    // next, add the brace.
                    output.Add(new string[1] { pszLine[idxEquals].ToString() }); // hopefully this is faster than substring
                    // finally, process the rest of the line.
                    ProcessLine(output, line, pszLine, idxEquals + 1, lineLen);
                    return;
                }

                if (idxEquals < lineLen - 1 && c == '/' && pszLine[idxEquals + 1] == '/')
                {
                    lineLen = idxEquals; // ignore whatever follows
                    break;
                }
                if (!char.IsWhiteSpace(c))
                    idxKeyLast = idxEquals;
            }
            if (idxEquals == lineLen)
            {
                // this is a nonempty line with no equals, and we've already stripped the comment and any braces.
                // We know the last non-WS character index, too, so let's just add it and return.
                output.Add(new string[1] { line.Substring(idxKeyStart, idxKeyLast - idxKeyStart + 1) });
                return;
            }
            else
            {
                // See if we should skip further processing.
                int keyLen = idxKeyLast - idxKeyStart + 1;
                if (keyLen > 0)
                {
                    foreach (string s in _skipKeys)
                    {
                        if (keyLen != s.Length)
                            continue;

                        bool match = true;
                        fixed (char* pszComp = s)
                        {
                            for (int j = 0; j < keyLen; ++j)
                            {
                                if (pszLine[idxKeyStart + j] != pszComp[j])
                                {
                                    match = false;
                                    break;
                                }
                            }
                        }

                        if (match)
                        {
#if DEBUG
                            Debug.Log("$$$ Skipped processing key " + s);
#endif
                            ProcessValueRaw(output, line, pszLine, idxEquals + 1, lineLen, idxKeyStart, idxKeyLast);
                            return;
                        }
                    }
                    foreach (string s in _skipPrefixes)
                    {
                        if (keyLen < s.Length)
                            continue;

                        bool match = true;
                        int sLen = s.Length;
                        fixed (char* pszComp = s)
                        {
                            for (int j = 0; j < sLen; ++j)
                            {
                                if (pszLine[idxKeyStart + j] != pszComp[j])
                                {
                                    match = false;
                                    break;
                                }
                            }
                        }

                        if (match)
                        {
#if DEBUG
                            Debug.Log($"$$$ Skipped processing key matching {s} (full key is {line.Substring(idxKeyStart, idxKeyLast - idxKeyStart + 1)})");
#endif
                            ProcessValueRaw(output, line, pszLine, idxEquals + 1, lineLen, idxKeyStart, idxKeyLast);
                            return;
                        }
                    }
                }
                ProcessValue(output, line, pszLine, idxEquals + 1, lineLen, idxKeyStart, idxKeyLast);
            }
        }

        private static unsafe List<string[]> _PreFormatConfig(string[] cfgData)
        {
            if (cfgData != null && cfgData.Length >= 1)
            {
                int lineCount = cfgData.Length;
                List<string[]> output = new List<string[]>(lineCount);

                for (int i = 0; i < lineCount; ++i)
                {
                    fixed (char* pszLine = cfgData[i])
                    {
                        ProcessLine(output, cfgData[i], pszLine, 0, cfgData[i].Length);
                    }
                }

                return output;
            }
            Debug.LogError("Error: Empty part config file");
            return null;
        }

        private static List<string[]> OldPreFormat(string[] cfgData)
        {
            if (cfgData != null && cfgData.Length >= 1)
            {
                List<string> list = new List<string>(cfgData);
                int num = list.Count;
                while (--num >= 0)
                {
                    list[num] = list[num];
                    int num2;
                    if ((num2 = list[num].IndexOf("//")) != -1)
                    {
                        if (num2 == 0)
                        {
                            list.RemoveAt(num);
                            continue;
                        }
                        list[num] = list[num].Remove(num2);
                    }
                    list[num] = list[num].Trim();
                    if (list[num].Length == 0)
                    {
                        list.RemoveAt(num);
                    }
                    else if ((num2 = list[num].IndexOf("}", 0)) != -1 && (num2 != 0 || list[num].Length != 1))
                    {
                        if (num2 > 0)
                        {
                            list.Insert(num, list[num].Substring(0, num2));
                            num++;
                            list[num] = list[num].Substring(num2);
                            num2 = 0;
                        }
                        if (num2 < list[num].Length - 1)
                        {
                            list.Insert(num + 1, list[num].Substring(num2 + 1));
                            list[num] = "}";
                            num += 2;
                        }
                    }
                    else if ((num2 = list[num].IndexOf("{", 0)) != -1 && (num2 != 0 || list[num].Length != 1))
                    {
                        if (num2 > 0)
                        {
                            list.Insert(num, list[num].Substring(0, num2));
                            num++;
                            list[num] = list[num].Substring(num2);
                            num2 = 0;
                        }
                        if (num2 < list[num].Length - 1)
                        {
                            list.Insert(num + 1, list[num].Substring(num2 + 1));
                            list[num] = "{";
                            num += 2;
                        }
                    }
                }
                List<string[]> list2 = new List<string[]>(list.Count);
                int i = 0;
                for (int count = list.Count; i < count; i++)
                {
                    string[] array = CustomEqualSplit(list[i]);
                    if (array != null && array.Length != 0)
                    {
                        list2.Add(array);
                    }
                }
                return list2;
            }
            Debug.LogError("Error: Empty part config file");
            return null;
        }
    }
}
