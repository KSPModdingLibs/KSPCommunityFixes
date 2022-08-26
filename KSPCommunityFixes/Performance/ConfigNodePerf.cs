//#define DEBUG_CONFIGNODE_PERF
//#define COMPARE_LOAD_RESULTS
//#define CONFIGNODE_PERF_TEST
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
using KSP.Localization;
using System.Text;
using System.Collections;

namespace KSPCommunityFixes.Performance
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class ConfigNodePerf : MonoBehaviour
    {
        private enum ParseMode
        {
            SkipToKey = 0,
            ReadKey,
            SkipToValue,
            ReadValue,
            EatComment,
        }

        const int _SaveBufferSize = 64 * 1024;
        const int _ReadBufferSize = 1024 * 1024;
        private static readonly char[] _charBuf = new char[_ReadBufferSize];
        static readonly UTF8Encoding _UTF8NoBOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        static readonly string _Newline = Environment.NewLine;
        static readonly Stack<ConfigNode> _nodeStack = new Stack<ConfigNode>(128);
        public static bool _doClean = true; // so it is accessible from ModUpgradePipeline
        public static bool _AllowSkipIndent = false; // so it is accessible from other things if needed
        // This large-size stringbuilder is used for writing ConfigNodes to string.
        // Large nodes may cause new stringbuilders to be created, but since we reset
        // the length after each use, they will get GC'd and only this first 1KB
        // block will remain. Annoyingly we have to use some reflection to avoid the
        // 1kb buffer being reallocated when this happens.
        const int StringBuilderBuffer = 1024;
        static StringBuilder _stringBuilder = new StringBuilder(StringBuilderBuffer);
        static readonly System.Reflection.MethodInfo FindChunkForIndex = typeof(StringBuilder).GetMethod("FindChunkForIndex", AccessTools.all);
        static readonly object[] _StringBuilderIndex0Param = new object[1] { 0 };

#if DEBUG_CONFIGNODE_PERF
        static long _ourTime = 0, _readTime = 0;
        static bool _skipOtherPatches = false;
#endif
        static string[] _skipPrefixes;
        static bool _valid = false;
        const int _MinLinesForParallel = 100000;
        static readonly System.Text.UTF8Encoding _UTF8NoBOM = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        static readonly string _Newline = Environment.NewLine;

        public void Awake()
        {
            GameObject.DontDestroyOnLoad(this);

            Harmony harmony = new Harmony("ConfigNodePerf");

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(Load), new Type[] { typeof(string), typeof(bool) }),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_Load_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(Parse)),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_Parse_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.Save), new Type[] { typeof(string), typeof(string) }),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_Save_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.CleanupInput)),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_CleanupInput_Prefix))));

            harmony.Patch(
                AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.CopyToRecursive)),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.ConfigNode_CopyToRecursive_Prefix))));

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

            harmony.Patch(
                AccessTools.Method(typeof(Game), nameof(Game.Updated), Type.EmptyTypes),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.SetNoClean))),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.SetDoClean))));

            harmony.Patch(
                AccessTools.Method(typeof(ShipConstruct), nameof(ShipConstruct.SaveShip)),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.SetNoClean))),
                new HarmonyMethod(AccessTools.Method(typeof(ConfigNodePerf), nameof(ConfigNodePerf.SetDoClean))));
        }

        public void ModuleManagerPostLoad()
        {
            StartCoroutine(LoadRoutine());
        }

        public IEnumerator LoadRoutine()
        {
            yield return null;

            KSPCommunityFixes.SettingsNode.TryGetValue("SkipIndentsOnSavesAndCraftFiles", ref _AllowSkipIndent);
        }

        private static void SetNoClean()
            {
            _doClean = false;
            }
            else
                _skipKeys = new string[0];

        private static void SetDoClean()
            {
            _doClean = true;
            }
            else
                _skipPrefixes = new string[0];

        private static unsafe bool ConfigNode_Ctor1_Prefix(ConfigNode __instance, string name)
        {
#if DEBUG_CONFIGNODE_PERF
            if (_skipOtherPatches)
                return true;
#endif
            __instance._values = new ValueList();
            __instance._nodes = new ConfigNodeList();

            int len;
            __instance.id = string.Empty;
            if (name == null || (len = name.Length) == 0)
            {
                __instance.name = string.Empty;
                return false;
        }

            if (!_doClean)
        {
                __instance.name = name;
                return false;
            }

            // Would be faster to cleanup in place along with detection
            // but I don't think it's worth it in this case.
            ConfigNode_CleanupInput_Prefix(name, ref name);

            // Split
            fixed (char* pszName = name)
            {
                int nameLen = 0;
                for (; nameLen < len; ++nameLen)
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

        private static bool ConfigNode_Ctor2_Prefix(ConfigNode __instance, string name, string vcomment)
        {
#if DEBUG_CONFIGNODE_PERF
            if (_skipOtherPatches)
                return true;
#endif
            ConfigNode_Ctor1_Prefix(__instance, name);
            __instance.comment = vcomment;
            return false;
        }

        private static unsafe bool ConfigNode_Parse_Prefix(string s, ref ConfigNode __result)
        {
            int len = s.Length;
            if (len == 0)
            {
                __result = new ConfigNode("root");
                return false;
            }

            try
            {
                fixed (char* pszInput = s)
                {
                    __result = ParseConfigNode(pszInput, len);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[KSPCommunityFixes] ConfigNodePerf: Exception parsing string, using fallback ConfigNode reader. Exception: " + e);
                __result = RecurseFormat(PreFormatConfig(s.Split('\n', '\r')));
            }
            return false;
        }

        private static bool ConfigNode_Load_Prefix(string fileFullName, bool bypassLocalization, ref ConfigNode __result)
        {
            __result = null;
            if (!File.Exists(fileFullName))
            {
                Debug.LogWarning("File '" + fileFullName + "' does not exist");
                return false;
            }
            string ext = Path.GetExtension(fileFullName);
#if DEBUG_CONFIGNODE_PERF
            Debug.Log($"Loaded from: file `{fileFullName}` with ext {ext}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif
            var oldClean = _doClean;
            switch (ext)
            {
                case ".ConfigCache":
                    _doClean = false;
                    break;
                case ".craft":
                    _doClean = false;
                    break;
                case ".sfs":
                    _doClean = false;
                    break;
            }
            try
            {
                __result = ReadFile(fileFullName);
#if DEBUG_CONFIGNODE_PERF
                _ourTime = sw.ElapsedMilliseconds;
#endif
                bool hasData = __result._nodes.nodes.Count > 0 || __result._values.values.Count > 0;
                if (hasData && !bypassLocalization && Localizer.Instance != null)
                {
                    Localizer.TranslateBranch(__result);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[KSPCommunityFixes] ConfigNodePerf: Exception reading file {fileFullName}, using fallback ConfigNode reader. Exception: " + e);
                __result = LoadFromStringArray(File.ReadAllLines(fileFullName), true);
            }
#if DEBUG_CONFIGNODE_PERF
            sw.Restart();
            string[] file = File.ReadAllLines(fileFullName);
            var oldRead = sw.ElapsedMilliseconds;
            sw.Restart();
            _skipOtherPatches = true;
            var old = LoadFromStringArray(file, bypassLocalization);
            _skipOtherPatches = false;
            var oldTime = sw.ElapsedMilliseconds;
            Debug.Log($"Ours: {_ourTime} (read {_readTime}), old: {oldTime + oldRead} (read {oldRead})");
#if COMPARE_LOAD_RESULTS
            if (!AreNodesEqual(__result, old, true))
            {
                Debug.LogError("@@@@ Mismatch in data!");
                // to step through again
                //AreNodesEqual(__result, old);
                __result = old;
            }
#endif
#endif
            _doClean = oldClean;
            return false;
        }

        private static bool ConfigNode_Save_Prefix(ConfigNode __instance, string fileFullName, string header, ref bool __result)
            {
            bool indent = true;
            if (_AllowSkipIndent)
                {
                string ext = Path.GetExtension(fileFullName);
                switch (ext)
                    {
                    //case ".ConfigCache":
                    // We can't control this with a setting because CC loads/saves
                    // before we have access to settings.

                    case ".craft":
                    case ".sfs":
                        indent = false;
                        break;
                    }
            }
            StreamWriter sw = new StreamWriter(File.Open(fileFullName, FileMode.Create), _UTF8NoBOM, _SaveBufferSize);
            if (indent && !string.IsNullOrEmpty(header))
                    {
                sw.Write("// ");
                sw.Write(header);
                sw.Write(_Newline);
                sw.Write(_Newline);
                    }
            _WriteRootNode(__instance, sw, indent, indent);
            sw.Close();

            __result = true;
            return false;
        }

        private static bool ConfigNode_CopyToRecursive_Prefix(ConfigNode __instance, ConfigNode node, bool overwrite)
                    {
            if (node.name == null || node.name.Length == 0)
            {
                node.name = __instance.name;
                    }
            if (node.id == null || node.id.Length == 0)
                    {
                node.id = __instance.id;
                    }
            if (__instance.comment != null && __instance.comment.Length > 0)
            {
                node.comment = __instance.comment;
                }
            for (int i = 0, iC = __instance.values.Count; i < iC; ++i)
            {
                Value value = __instance.values[i];
                Value v = null;

                if (overwrite)
                {
                    for (int j = 0, jC = node._values.values.Count; j < jC; ++j)
                    {
                        if (node._values.values[j].name == value.name)
                        {
                            v = node._values.values[j];
                            v.value = value.value;
                            v.comment = value.comment;
                            break;
            }
                    }
                }
                if (v == null)
            {
                    node._values.values.Add(new Value(value.name, value.value, value.comment));
                }
            }
            for (int i = 0, iC = __instance._nodes.nodes.Count; i < iC; ++i)
                {
                ConfigNode sub = __instance.nodes[i];
                if (overwrite)
                    {
                    node._nodes.RemoveNode(sub.name);
                }
                ConfigNode newNode = new ConfigNode(string.Empty); // will be set above when we recurse.
                node._nodes.nodes.Add(newNode);
                ConfigNode_CopyToRecursive_Prefix(sub, newNode, overwrite);
            }
            return false;
        }

        private static bool ConfigNode_ToString_Prefix(ConfigNode __instance, ref string __result)
                        {
            var sw = new StringWriter(_stringBuilder);
            _WriteNodeString(__instance, sw, string.Empty, false);
            __result = _stringBuilder.ToString();
            // Reset for next use
            if (_stringBuilder.Length > StringBuilderBuffer)
            {
                // If we're here, that means there's previous chunks.
                // So we need to get to the first chunk again. Setting the
                // field to point to it will detach the other chunks and GC
                // will collect them.
                _stringBuilder = FindChunkForIndex.Invoke(_stringBuilder, _StringBuilderIndex0Param) as StringBuilder;
                        }
            _stringBuilder.Length = 0;
            
            return false;
        }

        private static unsafe bool ConfigNodeValue_Sanitize_Prefix(ConfigNode.Value __instance, bool sanitizeName)
        {
#if DEBUG_CONFIGNODE_PERF
            if (_skipOtherPatches)
                return true;
#endif
            if (sanitizeName)
            {
                // We could skip if _doClean is false
                // but this should be quite fast, and we don't want to chance
                // a mod author writing a broken name by mistake and breaking things.

                if (__instance.name == null)
                    return false;

                int len = __instance.name.Length;
                if (len == 0)
                    return false;
                
                string result = null;
                bool sanitize = false;
                fixed (char* pszSanitize = __instance.name)
                {
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
                        // We could maybe fix in place, but this might be a const string
                        // and we don't want to risk it.
                        result = new string(' ', len);
                        fixed (char* pszNewStr = result)
                        {
                            for (int i = 0; i < len; ++i)
                            {
                                char c = pszSanitize[i];
                                switch (c)
                                {
                                    case '{': pszNewStr[i] = '['; break;
                                    case '}': pszNewStr[i] = ']'; break;
                                    case '=': pszNewStr[i] = '-'; break;
                                    default: pszNewStr[i] = c; break;
                        }
                    }
                }
            }
                }
                if (sanitize)
                    __instance.name = result;
            }
            else if (_doClean)
            {
                if (__instance.value == null)
                    return false;

                int len = __instance.value.Length;
                if (len == 0)
                    return false;

                string result = null;
                bool sanitize = false;
                fixed (char* pszSanitize = __instance.value)
                {    
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
                        // We could maybe fix in place, but this might be a const string
                        // and we don't want to risk it.
                        result = new string(' ', len);
                        fixed (char* pszNewStr = result)
                        {
                            for (int i = 0; i < len; ++i)
                            {
                                char c = pszSanitize[i];
                                switch (c)
                                {
                                    case '{': pszNewStr[i] = '['; break;
                                    case '}': pszNewStr[i] = ']'; break;
                                    default: pszNewStr[i] = c; break;
                                }
                            }
                        }
                    }
                }
                if (sanitize)
                    __instance.value = result;
            }
            return false;
        }

        private static unsafe bool ConfigNode_AddValue_Prefix(ConfigNode __instance, string name, string value, string vcomment)
        {
#if DEBUG_CONFIGNODE_PERF
            if (_skipOtherPatches)
                return true;
#endif
            if (value == null)
            {
                Debug.LogError(StringBuilderCache.Format("Input is null for field '{0}' in config node '{1}'\n{2}", name, __instance.name, Environment.StackTrace));
                value = string.Empty;
            }
            if (_doClean)
                ConfigNode_CleanupInput_Prefix(value, ref value);

            var v = new Value(name, value, vcomment);
            if (_doClean)
                ConfigNodeValue_Sanitize_Prefix(v, true);
            __instance._values.values.Add(v);
            return false;
        }

        private static unsafe bool ConfigNode_AddValue2_Prefix(ConfigNode __instance, string name, string value)
        {
#if DEBUG_CONFIGNODE_PERF
            if (_skipOtherPatches)
                return true;
#endif
            if (value == null)
            {
                Debug.LogError(StringBuilderCache.Format("Input is null for field '{0}' in config node '{1}'\n{2}", name, __instance.name, Environment.StackTrace));
                value = string.Empty;
            }
            if (_doClean)
                ConfigNode_CleanupInput_Prefix(value, ref value);

            var v = new Value(name, value);
            if (_doClean)
                ConfigNodeValue_Sanitize_Prefix(v, true);
            __instance._values.values.Add(v);
            return false;
        }

        private static unsafe bool ConfigNode_CleanupInput_Prefix(string value, ref string __result)
        {
#if DEBUG_CONFIGNODE_PERF
            if (_skipOtherPatches)
                return true;
#endif
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
#if DEBUG_CONFIGNODE_PERF
            if (_skipOtherPatches)
            return true;
#endif
            ConfigNodeValue_Sanitize_Prefix(v, true);
            __instance.values.Add(v);
            // skip the weird capacity stuff
            return false;
        }

        private static void _WriteRootNode(ConfigNode __instance, TextWriter sw, bool indent, bool includeComments = true)
        {
            for (int i = 0, count = __instance.values.Count; i < count; ++i)
            {
                Value value = __instance.values[i];
                sw.Write(value.name);
                sw.Write(" = ");
                sw.Write(value.value);
                if (includeComments && value.comment != null && value.comment.Length > 0)
                {
                    sw.Write(" // ");
                    sw.Write(value.comment);
                }
                sw.Write(_Newline);
            }
            if (indent)
            {
            for (int i = 0, count = __instance.nodes.Count; i < count; ++i)
            {
                    _WriteNodeString(__instance.nodes[i], sw, string.Empty, includeComments);
            }
        }
            else
            {
                for (int i = 0, count = __instance.nodes.Count; i < count; ++i)
                {
                    _WriteNodeStringNoIndent(__instance.nodes[i], sw);
                }
            }
        }

        private static void _WriteNodeString(ConfigNode __instance, TextWriter sw, string indent, bool includeComments)
        {
            sw.Write(indent);
            sw.Write(__instance.name);
            if (includeComments && __instance.comment != null && __instance.comment.Length > 0)
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
                if (includeComments && value.comment != null && value.comment.Length > 0)
                {
                    sw.Write(" // ");
                    sw.Write(value.comment);
                }
                sw.Write(_Newline);
            }
            for (int i = 0, count = __instance.nodes.Count; i < count; ++i)
            {
                _WriteNodeString(__instance.nodes[i], sw, newIndent, includeComments);
            }
            sw.Write(indent);
            sw.Write("}");
            sw.Write(_Newline);
        }

        private static void _WriteNodeStringNoIndent(ConfigNode __instance, TextWriter sw)
        {
            sw.Write(__instance.name);
            sw.Write(_Newline);

            sw.Write("{");
            sw.Write(_Newline);

            for (int i = 0, count = __instance.values.Count; i < count; ++i)
            {
                Value value = __instance.values[i];
                sw.Write(value.name);
                sw.Write(" = ");
                sw.Write(value.value);
                sw.Write(_Newline);
            }
            for (int i = 0, count = __instance.nodes.Count; i < count; ++i)
            {
                _WriteNodeStringNoIndent(__instance.nodes[i], sw);
            }
            sw.Write("}");
            sw.Write(_Newline);
        }

        private static unsafe ConfigNode ReadFile(string path)
        {
#if DEBUG_CONFIGNODE_PERF
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif
            char[] chars = null;
            int numChars = 0;
            using (var reader = new StreamReader(path, Encoding.UTF8, true, 1024 * 1024))
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Length > int.MaxValue)
                    throw new FileLoadException("file size too large for int length");
                int fLength = (int)fi.Length;
                if (fLength > _ReadBufferSize)
                    chars = new char[fLength];
                else
                    chars = _charBuf;

                numChars = reader.Read(chars, 0, chars.Length);
            }
#if DEBUG_CONFIGNODE_PERF
            _readTime = sw.ElapsedMilliseconds;
#endif

            if (numChars == 0)
                return new ConfigNode("root");

            fixed (char* pBase = chars)
                {
                return ParseConfigNode(pBase, numChars);
                }
            }

        public static unsafe ConfigNode ParseConfigNode(char* pBase, int numChars)
        {
            ConfigNode node = new ConfigNode("root");
            int pos = 0;
            string savedName = string.Empty;
            ParseMode mode = ParseMode.SkipToKey;
            _nodeStack.Push(node);


            int start = pos;
            int lastNonWS = pos - 1;
            int lastNonWSNonSlash = lastNonWS;

            for (; pos < numChars; ++pos)
        {
                char c = pBase[pos];

                // first eat the rest of the line, if it's a comment
                if (mode == ParseMode.EatComment)
            {
                    if (c == '\n')
                {
                        mode = ParseMode.SkipToKey;
                        start = pos + 1;
                        lastNonWS = pos;
                        lastNonWSNonSlash = lastNonWS;
                    }
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    // handle EOL
                    if (c == '\n')
                    {
                        //ProcessString(pBase, start, lastNonWS);
                        if (mode == ParseMode.ReadKey)
                        {
                            int len = lastNonWS - start + 1;
                            savedName = len > 0 ? new string(pBase, start, len) : string.Empty;
                        }
                        else if (mode == ParseMode.ReadValue || mode == ParseMode.SkipToValue)
                {
                            int len = lastNonWS - start + 1;
                            string val = len > 0 ? new string(pBase, start, len) : string.Empty;
                            _nodeStack.Peek()._values.values.Add(new Value(savedName, val));
                            savedName = string.Empty;
                }
                        mode = ParseMode.SkipToKey;
                        start = pos + 1;
                        lastNonWS = pos;
                        lastNonWSNonSlash = lastNonWS;
                        continue;
            }

            int idxValueLast;
            if (idxValueStart == lineLen)
            {
                // Empty value
                idxValueLast = -1;
            }
            else
            {
                    // Detect if we're starting a comment
                    if (c == '/')
                    {
                        if (pos < numChars - 1 && pBase[pos + 1] == '/')
                {
                            // will do nothing if in linestart
                            //ProcessString(pBase, start, lastNonWSNonSlash);
                            if (mode == ParseMode.ReadKey)
                    {
                                int len = lastNonWSNonSlash - start + 1;
                                savedName = len > 0 ? new string(pBase, start, len) : string.Empty;
                            }
                            else if (mode == ParseMode.ReadValue || mode == ParseMode.SkipToValue)
                        {
                                int len = lastNonWSNonSlash - start + 1;
                                _nodeStack.Peek()._values.values.Add(new Value(savedName, new string(pBase, start, len)));
                                savedName = string.Empty;
                        }

                            mode = ParseMode.EatComment;
                            ++pos; // we know the next char
                            continue;
                    }

                        // are we done eating ws at start of line?
                        if (mode == ParseMode.SkipToKey)
                    {
                            mode = ParseMode.ReadKey;
                            start = pos;
                    }
                        else if (mode == ParseMode.SkipToValue)
                    {
                            mode = ParseMode.ReadValue;
                            start = pos;
                    }
                        // update last non-ws but NOT
                        // the last non-ws before a slash.
                        lastNonWS = pos;
                }
                    else
                    {
                        // are we done eating ws at start of line?
                        if (mode == ParseMode.SkipToKey)
                {
                            mode = ParseMode.ReadKey;
                            start = pos;
                            lastNonWS = pos - 1; // zero-length string
        }
                        else if (mode == ParseMode.SkipToValue)
            {
                            mode = ParseMode.ReadValue;
                            start = pos;
                            lastNonWS = pos - 1; // zero-length string
            }

                        if (c == '{')
            {
                            //ProcessString(pBase, start, lastNonWS);
                            if (mode == ParseMode.ReadKey)
                {
                                // was there any string to grab, or is this a { on an empty line?
                                if (lastNonWS >= start)
                {
                                    int len = lastNonWS - start + 1;
                                    savedName = len > 0 ? new string(pBase, start, len) : string.Empty;
                }
                if (!char.IsWhiteSpace(c))
                    idxKeyLast = idxEquals;
            }
                            else if (mode == ParseMode.ReadValue || mode == ParseMode.SkipToValue)
            {
                                // we *do* want to store an empty value in this case.
                                int len = lastNonWS - start + 1;
                                string val = len > 0 ? new string(pBase, start, len) : string.Empty;
                                _nodeStack.Peek()._values.values.Add(new Value(savedName, val));
                                savedName = string.Empty;
            }
                            mode = ParseMode.SkipToKey;
                            var sub = new ConfigNode(savedName);
                            _nodeStack.Peek()._nodes.nodes.Add(sub);
                            _nodeStack.Push(sub);
                            start = pos + 1;
                            lastNonWS = pos;
                            lastNonWSNonSlash = lastNonWS;
                            continue;
                        }
                        else if (c == '}')
                        {
                            // 'name' without = is discarded when finishing a node
                            if (mode == ParseMode.ReadValue && lastNonWS >= start)
                            {
                                //ProcessString(pBase, start, lastNonWS);
                                if (mode == ParseMode.ReadKey)
                                {
                                    int len = lastNonWS - start + 1;
                                    savedName = len > 0 ? new string(pBase, start, len) : string.Empty;
                        }
                                else if (mode == ParseMode.ReadValue || mode == ParseMode.SkipToValue)
                        {
                                    int len = lastNonWS - start + 1;
                                    string val = len > 0 ? new string(pBase, start, len) : string.Empty;
                                    _nodeStack.Peek()._values.values.Add(new Value(savedName, val));
                                    savedName = string.Empty;
                        }
                    }
                            mode = ParseMode.SkipToKey;
                            // If we are in a subnode, close it and go to parent
                            if (_nodeStack.Count > 1)
                            {
                                _nodeStack.Pop();
                            }
                            else
                                {
                                // Otherwise do what stock does when encountering this typo: stop processing.
                                    break;
                                }
                            start = pos + 1;
                            lastNonWS = pos;
                            lastNonWSNonSlash = lastNonWS;
                            continue;
                        }
                        else if (mode == ParseMode.ReadKey && c == '=')
                        {
                            //ProcessString(pBase, start, lastNonWS);
                            int len = lastNonWS - start + 1;
                            savedName = len > 0 ? new string(pBase, start, len) : string.Empty;

                            // This saves a bit of time but isn't really worth it.
                            // Because in some rare cases things still need trimming
                            // and we don't want to sacrifice 100% compatibility
                            //if (_overrideSkip || IsSkip(pBase + start, len))
                            //{
                            //    int eqPos = pos;
                            //    pos += 2; // skip the = and the space
                            //    int valueStart = pos;
                            //    for(;;) // eat until EOL
                            //    {
                            //        if (pos == numChars)
                            //            break;
                            //        char p = pBase[pos];
                            //        if (p == '\n' || p == '\r')
                            //            break;
                            //        ++pos; 
                            //    }
                            //    len = pos - 1 - valueStart + 1;
                            //    string val = len > 0 ? new string(pBase, valueStart, len) : string.Empty;
                            //    _nodeStack.Peek()._values.values.Add(new Value(_lastStr, val));
                            //    _lastStr = string.Empty;

                            //    // start next parse
                            //    _mode = ParseMode.SkipToKey;
                            //    start = pos + 1;
                            //    lastNonWS = pos;
                            //    lastNonWSNonSlash = lastNonWS;
                            //    continue;
                            //}
                            mode = ParseMode.SkipToValue;
                            start = pos + 1;
                            lastNonWS = pos;
                            lastNonWSNonSlash = lastNonWS;
                            continue;
                        }

                        lastNonWS = pos;
                        lastNonWSNonSlash = pos;
                    }
                }
                ProcessValue(output, line, pszLine, idxEquals + 1, lineLen, idxKeyStart, idxKeyLast);
            }
            _nodeStack.Clear();
            return node;
        }

#if DEBUG_CONFIGNODE_PERF || CONFIGNODE_PERF_TEST
        private static string OldCleanupInput(string value)
                        {
            value = value.Replace("\n", "");
            value = value.Replace("\r", "");
            value = value.Replace("\t", " ");
            return value;
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

        private static bool ValueCountMismatch(ConfigNode a, ConfigNode b)
        {
            int aLen = a._values.values.Count;
            int bLen = b._values.values.Count;
            int minLen = Math.Min(aLen, bLen);
            for (int i = 0; i < minLen; ++i)
                    {
                var aV = a._values.values[i];
                var bV = b._values.values[i];
                if (aV.name != bV.name)
                        {
                    Debug.Log($"Value name mismatch at index {i}: {aV.name} | {bV.name}");
                    return false;
                    }
                if (aV.value != bV.value)
                {
                    Debug.Log($"Value value mismatch at index {i}: {aV.value} | {bV.value}");
                    return false;
                }
                }
            var extra = (aLen < bLen ? a : b)._values.values;
            for (int i = minLen; i < extra.Count; ++i)
                Debug.Log($"Extra value: {extra[i].name} | {extra[i].value}");

            return false;
        }

        private static bool NodeCountMismatch(ConfigNode a, ConfigNode b)
                {
            int aLen = a._nodes.nodes.Count;
            int bLen = b._nodes.nodes.Count;
            int minLen = Math.Min(aLen, bLen);
            for (int i = 0; i < minLen; ++i)
                    {
                var aN = a._nodes.nodes[i];
                var bN = b._nodes.nodes[i];
                if (aN.name != bN.name)
                        {
                    Debug.Log($"Node name mismatch at index {i}: {aN.name} | {bN.name}");
                    return false;
                        }
                        list[num] = list[num].Remove(num2);
                    }
            var extra = (aLen < bLen ? a : b)._nodes.nodes;
            for (int i = minLen; i < extra.Count; ++i)
                Debug.Log($"Extra node: {extra[i].name}");

            return false;
                    }

        private static bool AreNodesEqual(ConfigNode a, ConfigNode b, bool dump)
                    {
            if ((a == null) != (b == null))
                        {
                if (dump)
                    Debug.Log($"Null mismatch: {(a?.ToString() ?? "<>")} | {(b?.ToString() ?? "<>")}");
                return false;
                        }
            if (a == null)
                return true;

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
            {
                if (dump)
                    return ValueCountMismatch(a, b);
                return false;
                    }
            while (c-- > 0)
                    {
                if (a._values.values[c].name != b._values.values[c].name)
                        {
                    if (dump)
                        Debug.Log($"Value mismatch, name: {a._values.values[c].name ?? "<>"} | {b._values.values[c].name ?? "<>"}");
                    return false;
                        }
                if (a._values.values[c].value != b._values.values[c].value)
                        {
                    if (dump)
                        Debug.Log($"Value mismatch, name: {a._values.values[c].value?? "<>"} | {b._values.values[c].value ?? "<>"}");
                    return false;
                }
                if (a._values.values[c].comment != b._values.values[c].comment)
                    {
                    if (dump)
                        Debug.Log($"Value mismatch, name: {a._values.values[c].comment ?? "<>"} | {b._values.values[c].comment ?? "<>"}");
                    return false;
        }
    }

            c = a._nodes.nodes.Count;
            if (c != b._nodes.nodes.Count)
    {
                if (dump)
                    return NodeCountMismatch(a, b);
                return false;
            }
            while (c-- > 0)
                if (!AreNodesEqual(a._nodes.nodes[c], b._nodes.nodes[c], dump))
                    return false;

            return true;
        }
#endif
#if CONFIGNODE_PERF_TEST
        public static void Test()
        {
            // Insert your own tests here.
            Debug.Log("(((((");

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

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var craft = ConfigNode.Load(@"C:\Games\R112\saves\Hard\Ships\VAB\Dolphin Lunar Orbital.craft");
            Debug.Log($"Loaded craft, 30k lines: {sw.ElapsedMilliseconds}");
            sw.Restart();
            var bigSave = ConfigNode.Load(@"C:\Games\R112\saves\Hard\persistent.sfs");
            Debug.Log($"Loaded save, 1.2m lines: {sw.ElapsedMilliseconds}");
            sw.Restart();
            var scorpu = ConfigNode.Load(@"C:\temp\scorpu.sfs");
            Debug.Log($"Loaded save, 2.0m lines: {sw.ElapsedMilliseconds}");
            sw.Restart();
            var princSave = ConfigNode.Load(@"C:\Games\R112\saves\lcdev\persistent.sfs");
            Debug.Log($"Loaded save, 364k lines w/ Principia: {sw.ElapsedMilliseconds}");
            sw.Restart();
            var meganoodle = ConfigNode.Load(@"C:\temp\meganoodle.sfs");
            Debug.Log($"Loaded save, 660k lines w/ Principia: {sw.ElapsedMilliseconds}");
#if DEBUG_CONFIGNODE_PERF
            Debug.Log($"Loaded tree-parts");
            ConfigNode.Load(@"C:\Games\R112\GameData\RP-0\Tree\TREE-Parts.cfg");

            Debug.Log($"Loaded Downrange");
            ConfigNode.Load(@"C:\Games\R112\GameData\RP-0\Contracts\Sounding Rockets\DistanceSoundingDifficult.cfg");

            Debug.Log($"Loaded dictionary");
            ConfigNode.Load(@"C:\Games\R112\GameData\Squad\Localization\dictionary.cfg");

            Debug.Log($"Loaded gravity-model");
            ConfigNode.Load(@"C:\Games\R112\GameData\Principia\real_solar_system\gravity_model.cfg");

            Debug.Log($"Loaded mj loc fr");
            ConfigNode.Load(@"C:\Games\R112\GameData\MechJeb2\Localization\fr-fr.cfg");
#endif
            bool oldAllow = _AllowSkipIndent;
            _AllowSkipIndent = false;
            sw.Restart();
            scorpu.Save(@"C:\temp\t1.cfg");
            _AllowSkipIndent = true;
            var ours = sw.ElapsedMilliseconds;
            sw.Restart();
            scorpu.Save(@"c:\temp\t1.cfg");
            var oursSkip = sw.ElapsedMilliseconds;
            sw.Restart();
            StreamWriter streamWriter = new StreamWriter(File.Open(@"C:\temp\t2.cfg", FileMode.Create));
            scorpu.WriteRootNode(streamWriter);
            streamWriter.Close();
            var old = sw.ElapsedMilliseconds;
            sw.Restart();
            ConfigNode.Load(@"c:\temp\t1.cfg");
            var reload = sw.ElapsedMilliseconds;
            Debug.Log($"Scorpu: Read noindent: {reload}, write ours: {ours} ({oursSkip} noindent), old: {old}.");

            _AllowSkipIndent = false;
            sw.Restart();
            meganoodle.Save(@"C:\temp\t1.cfg");
            ours = sw.ElapsedMilliseconds;
            _AllowSkipIndent = true;
            sw.Restart();
            meganoodle.Save(@"C:\temp\t1.cfg");
            oursSkip = sw.ElapsedMilliseconds;
            sw.Restart();
            streamWriter = new StreamWriter(File.Open(@"C:\temp\t2.cfg", FileMode.Create));
            meganoodle.WriteRootNode(streamWriter);
            streamWriter.Close();
            old = sw.ElapsedMilliseconds;
            sw.Restart();
            ConfigNode.Load(@"C:\temp\t1.cfg");
            reload = sw.ElapsedMilliseconds;
            Debug.Log($"Meganoodle: Read noindent: {reload}, write ours: {ours} ({oursSkip} noindent), old: {old}.");

            _AllowSkipIndent = oldAllow;
            Debug.Log("end");
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class ConfigNodePerfTestser : MonoBehaviour
    {
        public void Awake()
        {
            ConfigNodePerf.Test();
    }
#endif
}
}
