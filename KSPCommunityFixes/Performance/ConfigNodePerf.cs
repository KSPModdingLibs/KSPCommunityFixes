//#define DEBUG_CONFIGNODE_PERF
//#define COMPARE_LOAD_RESULTS
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using static ConfigNode;
using UniLinq;
using System.Runtime.CompilerServices;
using System.IO;
using System.Collections;
using KSP.Localization;

namespace KSPCommunityFixes.Performance
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class ConfigNodePerf : MonoBehaviour
    {
        static string[] _skipKeys = new string[0];
        static string[] _skipPrefixes = new string[0];
        static string[] _skipBlacklist = new string[0];
        static readonly string[] _CraftKeys = new string[] { "ship", "description" }; // stock craft have comments in these.
        static bool _valid = false;
        static bool _overrideSkip = false;
        static bool _hasBlacklist = false;
        static readonly System.Text.UTF8Encoding _UTF8NoBOM = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        static readonly string _Newline = Environment.NewLine;
        

        public void Awake()
        {
            GameObject.DontDestroyOnLoad(this);

            Harmony harmony = new Harmony("ConfigNodePerf");

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(LoadFromStringArray), new Type[] { typeof(string[]), typeof(bool) } ),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_LoadFromStringArray_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(Load), new Type[] { typeof(string[]), typeof(bool) }),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_Load_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.Save), new Type[] { typeof(string), typeof(string) }),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_Save_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.CleanupInput)),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_CleanupInput_Prefix))));

            harmony.Patch(
                AccessTools.Constructor(typeof(ConfigNode), new Type[] { typeof(string) }),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_Ctor1_Prefix))));

            harmony.Patch(
                AccessTools.Constructor(typeof(ConfigNode), new Type[] { typeof(string), typeof(string) }),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_Ctor2_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.AddValue), new Type[] { typeof(string), typeof(string), typeof(string) }),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_AddValue_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.AddValue), new Type[] { typeof(string), typeof(string) }),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_AddValue2_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode.Value), nameof(ConfigNode.Value.Sanitize)),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNodeValue_Sanitize_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode.ValueList), nameof(ConfigNode.ValueList.Add)),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNodeValueList_Add_Prefix))));
        }

        public void ModuleManagerPostLoad()
        {
            StartCoroutine(LoadRoutine());
        }

        public IEnumerator LoadRoutine()
        {
            yield return null;

            ConfigNode settingsNodeKeys = KSPCommunityFixes.SettingsNode.GetNode("CONFIGNODE_PERF_SKIP_PROCESSING_KEYS");
            ConfigNode settingsNodePrefixes = KSPCommunityFixes.SettingsNode.GetNode("CONFIGNODE_PERF_SKIP_PROCESSING_SUBSTRINGS");
            ConfigNode settingsNodeBlacklist = KSPCommunityFixes.SettingsNode.GetNode("CONFIGNODE_PERF_SKIP_PROCESSING_BLACKLIST");

            if (settingsNodeKeys != null)
            {
                _skipKeys = new string[settingsNodeKeys.values.Count];
                int i = 0;
                foreach (ConfigNode.Value v in settingsNodeKeys.values)
                    _skipKeys[i++] = v.name;
            }

            if (settingsNodePrefixes != null)
            {
                _skipPrefixes = new string[settingsNodePrefixes.values.Count];
                int i = 0;
                foreach (ConfigNode.Value v in settingsNodePrefixes.values)
                    _skipPrefixes[i++] = v.name;
            }

            if (settingsNodeBlacklist != null)
            {
                _skipBlacklist = new string[settingsNodeBlacklist.values.Count];
                int i = 0;
                foreach (ConfigNode.Value v in settingsNodeBlacklist.values)
                    _skipBlacklist[i++] = v.name;
            }

            _valid = _skipKeys.Length > 0 || _skipPrefixes.Length > 0;
            if (settingsNodeBlacklist != null)
                _hasBlacklist = true;
        }

        private static unsafe bool ConfigNode_Ctor1_Prefix(ConfigNode __instance, string name)
        {
            __instance._values = new ValueList();
            __instance._nodes = new ConfigNodeList();

            int len;
            if (name == null || (len = name.Length) == 0)
            {
                __instance.name = string.Empty;
                return false;
            }

            // Would be faster to cleanup in place along with detection
            // but I don't think it's worth it in this case.
            ConfigNode_CleanupInput_Prefix(name, ref name);

            // Split
            fixed (char* pszName = name)
            {
                int nameLen = 0;
                for (;  nameLen < len; ++nameLen)
                {
                    char c = pszName[nameLen];
                    if (c == '(' || c == ')' || c == ' ')
                        break;
                }
                if (nameLen == len)
                {
                    __instance.name = name;
                    return false;
                }
                __instance.name = new string(pszName, 0, nameLen);

                int idStart = nameLen;
                for (; idStart < len; ++idStart)
                {
                    char c = pszName[idStart];
                    if (c == '(' || c == ')' || c == ' ')
                        break;
                }
                if (idStart == nameLen)
                {
                    return false;
                }

                // We have at least 1 valid character, let's work backwards
                int idEnd = len - 1;
                for (; idEnd > idStart; --idEnd)
                {
                    char c = pszName[idEnd];
                    if (c != '(' && c != ')' && c != ' ')
                        break;
                }

                __instance.id = new string(pszName, idStart, idEnd - idStart + 1);
            }
            return false;
        }

        private static unsafe bool ConfigNode_Ctor2_Prefix(ConfigNode __instance, string name, string vcomment)
        {
            ConfigNode_Ctor1_Prefix(__instance, name);
            __instance.comment = vcomment;
            return false;
        }

        private static bool ConfigNode_LoadFromStringArray_Prefix(string[] cfgData, bool bypassLocalization, ref ConfigNode __result)
        {
            __result = _LoadFromStringArray(cfgData, bypassLocalization);
#if DEBUG_CONFIGNODE_PERF
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var old = OldLoadFromStringArray(cfgData, bypassLocalization);
            var oldTime = sw.ElapsedMilliseconds;
            Debug.Log($"%%% Ours: {_ourTime}, old: {oldTime}");
#if COMPARE_LOAD_RESULTS
            if (!AreNodesEqual(__result, old))
            {
                Debug.LogError("@@@@ Mismatch in data!");
                AreNodesEqual(__result, old);
            }
#endif
#endif
            return false;
        }

        private static unsafe bool ConfigNode_Load_Prefix(string fileFullName, bool bypassLocalization, ref ConfigNode __result)
        {
            __result = null;
            if (!File.Exists(fileFullName))
            {
                Debug.LogWarning("File '" + fileFullName + "' does not exist");
                return false;
            }
#if DEBUG_CONFIGNODE_PERF
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif
            string ext = Path.GetExtension(fileFullName);
            var oldBlacklist = _skipBlacklist;
            var oldHasBlacklist = _hasBlacklist;
            switch (ext)
            {
                case "ConfigCache":
                    _overrideSkip = true;
                    break;
                case "craft":
                    var list = new List<string>(_skipBlacklist);
                    list.AddRange(_CraftKeys);
                    _skipBlacklist = list.ToArray();
                    break;
                case "sfs":
                    // sfs uses regular blacklist, if user has enabled that optimization
                    break;
                default:
                    // can't risk a blacklist with arbitrary mod-loaded cfg files
                    _hasBlacklist = false;
                    break;
            }
            StreamReader sr = new StreamReader(fileFullName);
            var configNode = new ConfigNode("root");
            _nodeStack.Push(configNode);
            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                fixed (char* pszLine = line)
                {
                    ProcessLine(pszLine, 0, line.Length);
                }
            }
            sr.Close();
            _nodeStack.Clear();
            _lastStr = string.Empty;
            bool hasData = configNode._nodes.nodes.Count > 0 || configNode._values.values.Count > 0;

            if (hasData && !bypassLocalization && Localizer.Instance != null)
            {
                Localizer.TranslateBranch(configNode);
            }
            if (hasData)
                __result = configNode;
#if DEBUG_CONFIGNODE_PERF
            _ourTime = sw.ElapsedMilliseconds;
            sw.Restart();
            var old = OldLoadFromStringArray(File.ReadAllLines(fileFullName), bypassLocalization);
            var oldTime = sw.ElapsedMilliseconds;
            Debug.Log($"%%% Ours: {_ourTime}, old: {oldTime}");
#if COMPARE_LOAD_RESULTS
            if (!AreNodesEqual(__result, old))
            {
                Debug.LogError("@@@@ Mismatch in data!");
                AreNodesEqual(__result, old);
            }
#endif
#endif
            _overrideSkip = false;
            _skipBlacklist = oldBlacklist;
            return false;
        }

        private static bool ConfigNode_Save_Prefix(ConfigNode __instance, string fileFullName, string header, ref bool __result)
        {
            __result = Save(__instance, fileFullName, header);
            return false;
        }

        private static unsafe bool ConfigNodeValue_Sanitize_Prefix(ConfigNode.Value __instance, bool sanitizeName)
        {
            if (sanitizeName)
            {
                fixed (char* pszSanitize = __instance.name)
                {
                    // It's cheaper to just do this than to do a skipcheck.
                    int len = __instance.name.Length;
                    bool sanitize = false;
                    for (int i = 0; i < len; ++i)
                    {
                        switch (pszSanitize[i])
                        {
                            case '{':
                            case '}':
                            case '=':
                                sanitize = true;
                                break;
                        }
                    }
                    if (sanitize)
                    {
                        char[] newStr = new char[len];
                        for (int i = 0; i < len; ++i)
                        {
                            char c = pszSanitize[i];
                            switch (c)
                            {
                                case '{': newStr[i] = '['; break;
                                case '}': newStr[i] = ']'; break;
                                case '=': newStr[i] = '-'; break;
                                default: newStr[i] = c; break;
                            }
                        }
                        __instance.name = new string(newStr);
                    }
                }
            }
            else
            {
                if (_overrideSkip)
                    return false;

                fixed (char* pszName = __instance.name)
                {
                    if (_valid && IsSkip(pszName, __instance.name.Length))
                        return false;
                }

                fixed (char* pszSanitize = __instance.value)
                {
                    int len = __instance.name.Length;
                    bool sanitize = false;
                    for (int i = 0; i < len; ++i)
                    {
                        switch (pszSanitize[i])
                        {
                            case '{':
                            case '}':
                                sanitize = true;
                                break;
                        }
                    }
                    if (sanitize)
                    {
                        char[] newStr = new char[len];
                        for (int i = 0; i < len; ++i)
                        {
                            char c = pszSanitize[i];
                            switch (c)
                            {
                                case '{': newStr[i] = '['; break;
                                case '}': newStr[i] = ']'; break;
                                default: newStr[i] = c; break;
                            }
                        }
                        __instance.value = new string(newStr);
                    }
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsSkip(char* pszKeyStart, int keyLen)
        {
            if (_hasBlacklist)
            {
                for (int k = _skipBlacklist.Length; k-- > 0;)
                {
                    string s = _skipBlacklist[k];
                    if (keyLen != s.Length)
                        continue;

                    fixed (char* pszComp = s)
                    {
                        int i = 0;
                        for (; i < keyLen; ++i)
                        {
                            if (pszKeyStart[i] != pszComp[i])
                                break;
                        }
                        if (i == keyLen)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }

            for (int k = _skipKeys.Length; k-- > 0;)
            {
                string s = _skipKeys[k];
                if (keyLen != s.Length)
                    continue;

                fixed (char* pszComp = s)
                {
                    int i = 0;
                    for (; i < keyLen; ++i)
                    {
                        if (pszKeyStart[i] != pszComp[i])
                            break;
                    }
                    if (i == keyLen)
                    {
                        return true;
                    }
                }
            }
            for (int k = _skipPrefixes.Length; k-- > 0;)
            {
                string s = _skipPrefixes[k];
                int sLen = s.Length;
                if (keyLen < sLen)
                    continue;

                fixed (char* pszComp = s)
                {
                    int i = 0;
                    for (; i < sLen; ++i)
                    {
                        if (pszKeyStart[i] != pszComp[i])
                            break;
                    }
                    if (i == sLen)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static unsafe bool ConfigNode_AddValue_Prefix(ConfigNode __instance, string name, string value, string vcomment)
        {
            if (value == null)
            {
                Debug.LogError(StringBuilderCache.Format("Input is null for field '{0}' in config node '{1}'\n{2}", name, __instance.name, Environment.StackTrace));
                value = string.Empty;
            }
            bool sanitize = true;
            if (name != null && name.Length > 0)
            {
                if (_overrideSkip)
                {
                    sanitize = false;
                }
                else
                {
                    fixed (char* pszName = name)
                    {
                        if (_valid && IsSkip(pszName, name.Length))
                            sanitize = false;
                        else
                            ConfigNode_CleanupInput_Prefix(value, ref value);
                    }
                }
            }
            else
                ConfigNode_CleanupInput_Prefix(value, ref value);

            var v = new Value(name, value, vcomment);
            if (sanitize)
                ConfigNodeValue_Sanitize_Prefix(v, true);
            __instance._values.values.Add(v);
            return false;
        }

        private static unsafe bool ConfigNode_AddValue2_Prefix(ConfigNode __instance, string name, string value)
        {
            if (value == null)
            {
                Debug.LogError(StringBuilderCache.Format("Input is null for field '{0}' in config node '{1}'\n{2}", name, __instance.name, Environment.StackTrace));
                value = string.Empty;
            }
            bool sanitize = true;
            if (name != null && name.Length > 0)
            {
                if (_overrideSkip)
                {
                    sanitize = false;
                }
                else
                {
                    fixed (char* pszName = name)
                    {
                        if (_valid && IsSkip(pszName, name.Length))
                            sanitize = false;
                        else
                            ConfigNode_CleanupInput_Prefix(value, ref value);
                    }
                }
            }
            else
                ConfigNode_CleanupInput_Prefix(value, ref value);

            var v = new Value(name, value);
            if (sanitize)
                ConfigNodeValue_Sanitize_Prefix(v, true);
            __instance._values.values.Add(v);
            return false;
        }

        private static unsafe bool ConfigNode_CleanupInput_Prefix(string value, ref string __result)
        {
            fixed (char* pszSanitize = value)
            {
                int len = value.Length;
                bool ok = true;
                int removed = 0;
                for (int i = 0; i < len; ++i)
                {
                    char c = pszSanitize[i];
                    if (c == '\t')
                    {
                        ok = false;
                    }
                    else
                    {
                        if (c == '\n' || c == '\r')
                        {
                            ++removed;
                            ok = false;
                        }
                    }
                }
                if (ok)
                {
                    __result = value;
                    return false;
                }

                if (len == removed)
                {
                    __result = string.Empty;
                    return false;
                }

                __result = new string(' ', len - removed);
                fixed (char* pszResult = __result)
                {
                    for (int i = 0, j = 0; i < len; ++i)
                    {
                        char c = pszSanitize[i];
                        switch (c)
                        {
                            case '\n':
                            case '\r':
                                break;
                            case '\t': pszResult[j++] = ' '; break;
                            default: pszResult[j++] = c; break;
                        }
                    }
                }
                return false;
            }
        }

        private static bool ConfigNodeValueList_Add_Prefix(ValueList __instance, Value v)
        {
            ConfigNodeValue_Sanitize_Prefix(v, true);
            __instance.values.Add(v);
            // skip the weird capacity stuff
            return false;
        }

        private static bool Save(ConfigNode __instance, string fileFullName, string header)
        {
            StreamWriter sw = new StreamWriter(File.Open(fileFullName, FileMode.Create), _UTF8NoBOM, 65536);
            if (!string.IsNullOrEmpty(header))
            {
                sw.Write("// ");
                sw.Write(header);
                sw.Write(_Newline);
                sw.Write(_Newline);
            }
            _WriteRootNode(__instance, sw);
            sw.Close();
            return true;
        }

        private static void _WriteRootNode(ConfigNode __instance, StreamWriter sw)
        {
            for (int i = 0, count = __instance.values.Count; i < count; ++i)
            {
                Value value = __instance.values[i];
                sw.Write(value.name);
                sw.Write(" = ");
                sw.Write(value.value);
                if (value.comment != null && value.comment.Length > 0)
                {
                    sw.Write(" // ");
                    sw.Write(value.comment);
                }
                sw.Write(_Newline);
            }
            for (int i = 0, count = __instance.nodes.Count; i < count; ++i)
            {
                _WriteNodeString(__instance.nodes[i], sw, string.Empty);
            }
        }

        private static void _WriteNodeString(ConfigNode __instance, StreamWriter sw, string indent)
        {
            sw.Write(indent);
            sw.Write(__instance.name);
            if (__instance.comment != null && __instance.comment.Length > 0)
            {
                sw.Write(" // ");
                sw.Write(__instance.comment);
            }
            sw.Write(_Newline);

            sw.Write(indent);
            sw.Write("{");
            sw.Write(_Newline);

            string newIndent = indent + "\t";

            for (int i = 0, count = __instance.values.Count; i < count; ++i)
            {
                Value value = __instance.values[i];
                sw.Write(newIndent);
                sw.Write(value.name);
                sw.Write(" = ");
                sw.Write(value.value);
                if (value.comment != null && value.comment.Length > 0)
                {
                    sw.Write(" // ");
                    sw.Write(value.comment);
                }
                sw.Write(_Newline);
            }
            for (int i = 0, count = __instance.nodes.Count; i < count; ++i)
            {
                _WriteNodeString(__instance.nodes[i], sw, newIndent);
            }
            sw.Write(indent);
            sw.Write("}");
            sw.Write(_Newline);
        }

        // Helper statics
        static Stack<ConfigNode> _nodeStack = new Stack<ConfigNode>(128);
        static string _lastStr = string.Empty;
#if DEBUG_CONFIGNODE_PERF
        static long _ourTime = 0;
#endif
        private static unsafe ConfigNode _LoadFromStringArray(string[] cfgData, bool bypassLocalization)
        {
            if (cfgData == null || cfgData.Length == 0)
            {
                Debug.LogError("Error: Empty part config file");
                return null;
            }
#if DEBUG_CONFIGNODE_PERF
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif
            var configNode = new ConfigNode("root");
            int lineCount = cfgData.Length;
            _nodeStack.Push(configNode);
            for (int i = 0; i < lineCount; ++i)
            {
                fixed (char* pszLine = cfgData[i])
                {
                    ProcessLine(pszLine, 0, cfgData[i].Length);
                }
            }
            _nodeStack.Clear();
            _lastStr = string.Empty;
            if (!bypassLocalization && Localizer.Instance != null)
            {
                Localizer.TranslateBranch(configNode);
            }
#if DEBUG_CONFIGNODE_PERF
            _ourTime = sw.ElapsedMilliseconds;
#endif
            return configNode;
        }

        private static unsafe void ProcessLine(char* pszLine, int idxKeyStart, int lineLen)
        {
            bool doChecks = true;
        ProcessStart:
            if (doChecks) // we might not need to do this stuff if we found a { midline.
            {
                if (idxKeyStart == lineLen)
                    return;
                
                // Find first nonwhitespace char
                for (; idxKeyStart < lineLen; ++idxKeyStart)
                {
                    char c = pszLine[idxKeyStart];
                    if (!char.IsWhiteSpace(c))
                        break;
                }

                // If we didn't find any non-whitespace, or it's only a comment,
                // bail: it's a blank line.
                if (idxKeyStart == lineLen
                    || (idxKeyStart < lineLen - 1 && pszLine[idxKeyStart] == '/' && pszLine[idxKeyStart + 1] == '/'))
                    return;
            }
            doChecks = true; // always reset

            // Find equals, if it exists. We'll divert if we find a brace, and truncate if we find a comment.
            int idxEquals = idxKeyStart;
            int idxKeyLast = idxEquals - 1; // this sets up for a zero-length string
            for (; idxEquals < lineLen; ++idxEquals)
            {
                char c = pszLine[idxEquals];
                if (c == '=')
                    break;

                if (c == '{')
                {
                    // add a new node and restart processing
                    int nameLen = idxKeyLast - idxKeyStart + 1;
                    string name = nameLen > 0 ? new string(pszLine + idxKeyStart, 0, nameLen) : _lastStr;
                    _nodeStack.Push(_nodeStack.Peek().AddNode(name));
                    idxKeyStart = idxEquals + 1;
                    _lastStr = string.Empty;
                    goto ProcessStart;
                }

                if (c == '}')
                {
                    _nodeStack.Pop();
                    idxKeyStart = idxEquals + 1;
                    goto ProcessStart;
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
                // this is a nonempty line with no equals, and we've already stripped the comment (and there are no braces)
                // We know the last non-WS character index, too.
                // This is our candidate string for a node name, so let's just return it.
                _lastStr = new string(pszLine + idxKeyStart, 0, idxKeyLast - idxKeyStart + 1);
                return;
            }

            int keyLen = idxKeyLast - idxKeyStart + 1;

            int idxValueStart = idxEquals + 1;
            int valueLen = 0;
            // See if we should skip further processing.
            if (_overrideSkip)
            {
                Value v = new Value(keyLen == 0 ? string.Empty : new string(pszLine, idxKeyStart, keyLen), valueLen == 0 ? string.Empty : new string(pszLine, idxValueStart, valueLen));
                _nodeStack.Peek()._values.values.Add(v);
                _lastStr = string.Empty;
                return;
            }
            if (_valid && keyLen > 0)
            {
                if (IsSkip(pszLine + idxKeyStart, keyLen))
                {
                    for (; idxValueStart < lineLen; ++idxValueStart)
                    {
                        if (!char.IsWhiteSpace(pszLine[idxValueStart]))
                            break;
                    }

                    if (idxValueStart != lineLen)
                    {
                        int idxValueLast = lineLen - 1;
                        for (; idxValueLast > idxValueStart; --idxValueLast)
                        {
                            if (!char.IsWhiteSpace(pszLine[idxValueLast]))
                                break;
                        }
                        valueLen = idxValueLast - idxValueStart + 1;
                    }

                    Value v = new Value(new string(pszLine, idxKeyStart, keyLen), valueLen == 0 ? string.Empty : new string(pszLine, idxValueStart, valueLen));
                    _nodeStack.Peek()._values.values.Add(v);
                    _lastStr = string.Empty;
                    return;
                }
            }

            // Normal processing of the rest of the line
            for (; idxValueStart < lineLen; ++idxValueStart)
            {
                char c = pszLine[idxValueStart];
                if (c == '{' || c == '}')
                {
                    Value v = new Value(new string(pszLine, idxKeyStart, keyLen), string.Empty);
                    _nodeStack.Peek()._values.values.Add(v);
                    _lastStr = string.Empty;
                    idxKeyStart = idxValueStart;
                    doChecks = false; // we already have a valid char
                    goto ProcessStart;
                }

                if (idxValueStart < lineLen - 1 && c == '/' && pszLine[idxValueStart + 1] == '/')
                {
                    lineLen = idxValueStart; // ignore whatever follows
                    break;
                }

                if (!char.IsWhiteSpace(c))
                    break;
            }

            if (idxValueStart != lineLen)
            {
                int idxValueLast = idxValueStart - 1; // this sets up for a zero-length string
                for (int i = idxValueStart; i < lineLen; ++i)
                {
                    char c = pszLine[i];
                    if (c == '{' || c == '}')
                    {
                        valueLen = idxValueLast - idxValueStart + 1;
                        Value v = new Value(keyLen == 0 ? string.Empty : new string(pszLine, idxKeyStart, keyLen),
                            valueLen == 0 ? string.Empty : new string(pszLine, idxValueStart, valueLen));
                        _nodeStack.Peek()._values.values.Add(v);
                        _lastStr = string.Empty;
                        idxKeyStart = i;
                        doChecks = false; // we already have a valid char
                        goto ProcessStart;
                    }

                    if (i < lineLen - 1 && c == '/' && pszLine[i + 1] == '/')
                    {
                        break;
                    }

                    if (!char.IsWhiteSpace(c))
                        idxValueLast = i;
                }
                valueLen = idxValueLast - idxValueStart + 1;
            }
            // We reached the end of the line. Add the value and we're done.
            {
                Value v = new Value(keyLen == 0 ? string.Empty : new string(pszLine, idxKeyStart, keyLen),
                    valueLen == 0 ? string.Empty : new string(pszLine, idxValueStart, valueLen));
                _nodeStack.Peek()._values.values.Add(v);
                _lastStr = string.Empty;
            }
        }

        // From Mono
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool EqualsHelper(char* pszA, char* pszB, int len)
        {
            char* pchA = pszA;
            char* pchB = pszB;
            if (Environment.Is64BitProcess)
            {
                while (len >= 12)
                {
                    if (*(long*)pchA != *(long*)pchB)
                    {
                        return false;
                    }
                    if (*(long*)(pchA + 4) != *(long*)(pchB + 4))
                    {
                        return false;
                    }
                    if (*(long*)(pchA + 8) != *(long*)(pchB + 8))
                    {
                        return false;
                    }
                    pchA += 12;
                    pchB += 12;
                    len -= 12;
                }
            }
            else
            {
                while (len >= 10)
                {
                    if (*(int*)pchA != *(int*)pchB)
                    {
                        return false;
                    }
                    if (*(int*)(pchA + 2) != *(int*)(pchB + 2))
                    {
                        return false;
                    }
                    if (*(int*)(pchA + 4) != *(int*)(pchB + 4))
                    {
                        return false;
                    }
                    if (*(int*)(pchA + 6) != *(int*)(pchB + 6))
                    {
                        return false;
                    }
                    if (*(int*)(pchA + 8) != *(int*)(pchB + 8))
                    {
                        return false;
                    }
                    pchA += 10;
                    pchB += 10;
                    len -= 10;
                }
            }
            while (len > 0 && *(int*)pchA == *(int*)pchB)
            {
                pchA += 2;
                pchB += 2;
                len -= 2;
            }
            return len <= 0;
        }

#if DEBUG_CONFIGNODE_PERF
        private static string OldCleanupInput(string value)
        {
            value = value.Replace("\n", "");
            value = value.Replace("\r", "");
            value = value.Replace("\t", " ");
            return value;
        }

        private static ConfigNode OldLoadFromStringArray(string[] cfgData, bool bypassLocalization)
        {
            if (cfgData == null)
            {
                return null;
            }
            List<string[]> list = PreFormatConfig(cfgData);
            if (list != null && list.Count != 0)
            {
                ConfigNode configNode = RecurseFormat(list);
                if (Localizer.Instance != null && !bypassLocalization)
                {
                    Localizer.TranslateBranch(configNode);
                }
                return configNode;
            }
            return null;
        }

        private static bool IsHeaderEqual(ConfigNode a, ConfigNode b)
        {
            if (a.name != b.name)
                return false;
            if (a.id != b.id)
                return false;
            if (a.comment != b.comment)
                return false;

            return true;
        }

        private static bool AreNodesEqual(ConfigNode a, ConfigNode b)
        {
            if (!IsHeaderEqual(a, b))
            {
                Debug.Log("Headers unequal!"
                    + $"\nName: {a.name?.Length ?? -1} - {b.name?.Length ?? -1}: {a?.name ?? "<>"} | {b?.name ?? "<>"}"
                    + $"\nID: {a.id?.Length ?? -1} - {b.id?.Length ?? -1}: {a?.id ?? "<>"} | {b?.id ?? "<>"}"
                    + $"\nComment: {a.comment?.Length ?? -1} - {b.comment?.Length ?? -1}: {a?.comment ?? "<>"} | {b?.comment ?? "<>"}");
                return false;
            }

            int c = a._values.values.Count;
            if (c != b._values.values.Count)
                return false;
            while (c-- > 0)
            {
                if (a._values.values[c].name != b._values.values[c].name)
                    return false;
                if (a._values.values[c].value != b._values.values[c].value)
                    return false;
                if (a._values.values[c].comment != b._values.values[c].comment)
                    return false;
            }

            c = a._nodes.nodes.Count;
            if (c != b._nodes.nodes.Count)
                return false;
            while (c-- > 0)
                if (!AreNodesEqual(a._nodes.nodes[c], b._nodes.nodes[c]))
                    return false;

            return true;
        }

        public static void Test()
        {
            const string testStr = "==sdfjiksd==lf{{}p=dsjf][]df}{{{";
            const string cleanStr = "==sdfjik\nsd=\r=lf{\t{}p=d\tsjf][]df}{{{\n";
            string ourClean = string.Empty;
            ConfigNode_CleanupInput_Prefix(cleanStr, ref ourClean);
            var oldClean = OldCleanupInput(cleanStr);
            if (ourClean.Equals(oldClean))
                Debug.Log("Cleans equal!");
            else
                Debug.Log($"Cleans not equal! Lengths {ourClean.Length} - {oldClean.Length}\nOur: {ourClean.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")}\nOld: " + oldClean);

            Value ourV = new Value(testStr, testStr, "comment");
            ourV.Sanitize(true);
            ourV.Sanitize(false);
            Value oldV = new Value(testStr, testStr, "comment");
            oldV.SanitizeString(ref oldV.name, true);
            oldV.SanitizeString(ref oldV.value, false);
            if (ourV.name == oldV.name && ourV.value == oldV.value)
                Debug.Log("Sanitize equal!");
            else
                Debug.Log($"Sanitize not equal!\n{ourV.name}\n{oldV.name}\n===\n{ourV.value}\n{oldV.value}");

            if (_skipKeys.Length > 0)
            {
                ConfigNode skip = new ConfigNode();
                skip.AddValue(_skipKeys[0], cleanStr);
                Debug.Log("Skip test: " + (skip.values[0].value == cleanStr ? "ok!" : "fail"));
            }

            if (_skipPrefixes.Length > 0)
            {
                ConfigNode skip = new ConfigNode();
                skip.AddValue(_skipPrefixes[0] + "skdjflsdkf", cleanStr);
                Debug.Log("Prefix Skip test: " + (skip.values[0].value == cleanStr ? "ok!" : "fail"));
            }

            ConfigNode unskip = new ConfigNode();
            unskip.AddValue("test", cleanStr);
            Debug.Log("Non-skip test: " + (unskip.values[0].value != cleanStr ? "ok!" : "fail"));

            string keys = "With keys:";
            foreach (var s in ConfigNodePerf._skipKeys)
                keys += " " + s;
            keys += " and prefixes";
            foreach (var s in ConfigNodePerf._skipPrefixes)
                keys += " " + s;
            Debug.Log(keys);
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class ConfigNodePerfTestser : MonoBehaviour
    {
        public void Awake()
        {
            // Insert your own tests here.
            Debug.Log("(((((");

            Debug.Log("Loading craft, 30k lines");
            var craft = ConfigNode.Load("C:\\Games\\R112\\saves\\Hard\\Ships\\VAB\\Dolphin Lunar Orbital.craft");
            Debug.Log("Loading save, 1.2m lines");
            var bigSave = ConfigNode.Load("C:\\Games\\R112\\saves\\Hard\\persistent.sfs");
            Debug.Log("Loading save, 2.0m lines");
            var scorpu = ConfigNode.Load("C:\\temp\\scorpu.sfs");
            Debug.Log("Loading save, 364k lines w/ Principia");
            var princSave = ConfigNode.Load("C:\\Games\\R112\\saves\\lcdev\\persistent.sfs");
            Debug.Log("Loading save, 660k lines w/ Principia");
            var meganoodle = ConfigNode.Load("C:\\temp\\meganoodle.sfs");

            Debug.Log("((((Loading tree-parts");
            ConfigNode.Load("C:\\Games\\R112\\GameData\\RP-0\\Tree\\TREE-Parts.cfg");

            Debug.Log("Loading Downrange");
            ConfigNode.Load("C:\\Games\\R112\\GameData\\RP-0\\Contracts\\Sounding Rockets\\DistanceSoundingDifficult.cfg");

            Debug.Log("Loading dictionary");
            ConfigNode.Load("C:\\Games\\R112\\GameData\\Squad\\Localization\\dictionary.cfg");

            Debug.Log("Loading gravity-model");
            ConfigNode.Load("C:\\Games\\R112\\GameData\\Principia\\real_solar_system\\gravity_model.cfg");

            Debug.Log("Loading mj loc fr");
            ConfigNode.Load("C:\\Games\\R112\\GameData\\MechJeb2\\Localization\\fr-fr.cfg");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            scorpu.Save("c:\\temp\\t1.cfg");
            var ours = sw.ElapsedMilliseconds;
            sw.Restart();
            StreamWriter streamWriter = new StreamWriter(File.Open("c:\\temp\\t2.cfg", FileMode.Create));
            scorpu.WriteRootNode(streamWriter);
            streamWriter.Close();
            var old = sw.ElapsedMilliseconds;
            Debug.Log($"%%% Scorpu Write perf: ours: {ours}, old: {old}");

            sw.Restart();
            meganoodle.Save("c:\\temp\\t1.cfg");
            ours = sw.ElapsedMilliseconds;
            sw.Restart();
            streamWriter = new StreamWriter(File.Open("c:\\temp\\t2.cfg", FileMode.Create));
            meganoodle.WriteRootNode(streamWriter);
            streamWriter.Close();
            old = sw.ElapsedMilliseconds;
            Debug.Log($"%%% Meganoodle save Write perf: ours: {ours}, old: {old}");

            ConfigNodePerf.Test();

            Debug.Log("end");
        }
#endif
    }
}
