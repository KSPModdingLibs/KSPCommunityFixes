// - Add support for IConfigNode serialization
// - Add support for Guid serialization
// - Rewrite Object reading and writing for perf, clarity, and reuse
//#define DEBUG_PERSISTENTICONFIGNODE
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static ConfigNode;
using Random = System.Random;

namespace KSPCommunityFixes.Modding
{
    class PersistentIConfigNode : BasePatch
    {
        private struct LoadState
        {
            public bool wasRemove;
            public ReadLinkList links;
            public LoadState(bool r, ReadLinkList l)
            {
                wasRemove = r;
                links = l;
            }
        }

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            MethodInfo ConfigNode_ReadObject = AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.ReadObject), new Type[] { typeof(object), typeof(ConfigNode) });
            MethodInfo ConfigNode_WriteObject = AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.WriteObject), new Type[] { typeof(object), typeof(ConfigNode), typeof(int) });

            if (ConfigNode_ReadObject == null)
                throw new Exception("ConfigNode.ReadObject() : method not found");

            if (ConfigNode_WriteObject == null)
                throw new Exception("ConfigNode.WriteObject() : method not found");

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                ConfigNode_ReadObject,
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                ConfigNode_WriteObject,
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ConfigNode), nameof(CreateConfigFromObject), new Type[] { typeof(object), typeof(int), typeof(ConfigNode) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ConfigNode), nameof(CreateConfigFromObject), new Type[] { typeof(object), typeof(int), typeof(ConfigNode) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ConfigNode), nameof(LoadObjectFromConfig), new Type[] { typeof(object), typeof(ConfigNode), typeof(int), typeof(bool) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ConfigNode), nameof(LoadObjectFromConfig), new Type[] { typeof(object), typeof(ConfigNode), typeof(int), typeof(bool) }),
                this));
        }

        // This will fail if nested, so we cache off the old writeLinks.
        private static void ConfigNode_CreateConfigFromObject_Prefix(out WriteLinkList __state)
        {
            __state = writeLinks;
        }

        // and restore it
        private static void ConfigNode_CreateConfigFromObject_Postfix(WriteLinkList __state)
        {
            writeLinks = __state;
        }

        // This will fail if nested, so we cache off the old readlinks and remove.
        private static void ConfigNode_LoadObjectFromConfig_Prefix(out LoadState __state)
        {
            __state = new LoadState(removeAfterUse, readLinks);
        }

        // and restore them
        private static void ConfigNode_LoadObjectFromConfig_Postfix(LoadState __state)
        {
            removeAfterUse = __state.wasRemove;
            readLinks = __state.links;
        }

        // used by TypeCache since we can't make the below method return a value.
        public static bool WriteSuccess;

        public static bool ConfigNode_WriteObject_Prefix(object obj, ConfigNode node, int pass)
        {
            if (obj is IPersistenceSave persistenceSave)
                persistenceSave.PersistenceSave();

            Type type = obj.GetType();
            var typeCache = TypeCache.GetOrCreate(type);
            if (typeCache == null)
            {
                WriteSuccess = false;
                return false;
            }

            WriteSuccess = typeCache.Write(obj, node, pass);

            return false;
        }


        public static bool ConfigNode_ReadObject_Prefix(object obj, ConfigNode node, out bool __result)
        {
            Type type = obj.GetType();
            var typeCache = TypeCache.GetOrCreate(type);
            if (typeCache == null)
            {
                __result = false;
                return false;
            }

            bool readResult = typeCache.Read(obj, node);

            if (obj is IPersistenceLoad persistenceLoad)
            {
                iPersistentLoaders.Add(persistenceLoad);
            }

            __result = readResult;
            return false;
        }
    }

    // This is an expanded version of System.TypeCode
    public enum DataType : uint
    {
        INVALID = 0,
        IConfigNode,
        ValueString,
        ValueGuid,
        ValueBool,
        ValueByte,
        ValueSByte,
        ValueChar,
        ValueDecimal,
        ValueDouble,
        ValueFloat,
        ValueInt,
        ValueUInt,
        ValueLong,
        ValueULong,
        ValueShort,
        ValueUShort,
        ValueVector2,
        ValueVector3,
        ValueVector3d,
        ValueVector4,
        ValueQuaternion,
        ValueQuaternionD,
        ValueMatrix4x4,
        ValueColor,
        ValueColor32,
        ValueEnum,
        Array,
        IList,
        Component,
        Object,

        FirstValueType = ValueString,
        LastValueType = ValueEnum,
    }

    public class FieldData
    {
        private static readonly System.Globalization.CultureInfo _Invariant = System.Globalization.CultureInfo.InvariantCulture;

        public string[] persistentNames = null;
        public Type fieldType = null;
        public FieldInfo fieldInfo = null;
        public Persistent[] attribs = null;
        public DataType dataType = DataType.INVALID;
        public bool isLinkable = false;

        public FieldData arrayType;

        public FieldData(Type t, Persistent[] attribs)
        {
            fieldType = t;
            this.attribs = attribs;

            int len = attribs.Length;
            persistentNames = new string[len];
            for (int i = len; i-- > 0;)
            {
                string itemName = attribs[i].collectionIndex;
                if (itemName != null && itemName.Length > 0)
                    persistentNames[i] = itemName;
                // else leave it null
            }

            SetDataType(false);
        }

        public FieldData(MemberInfo memberInfo, Persistent[] attribs)
        {
            this.attribs = attribs;
            int len = attribs.Length;
            persistentNames = new string[len];
            for (int i = len; i-- > 0;)
            {
                string pName = attribs[i].name;
                persistentNames[i] = pName != null && pName.Length > 0 ? pName : memberInfo.Name;
            }

            fieldInfo = memberInfo.DeclaringType.GetField(memberInfo.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            fieldType = fieldInfo.FieldType;
            arrayType = null;

            isLinkable = HasAttribute(memberInfo.DeclaringType, typeof(PersistentLinkable));

            SetDataType(true);
        }

        public bool Read(Value value, object host, int attribIndex)
        {
            if (attribIndex >= attribs.Length)
            {
                Debug.LogError($"[KSPCommunityFixes] Tried to read value `{value.name} = {value.value}` for field {fieldInfo.Name}, index was {attribIndex} but only {attribs.Length} [Persistent] attributes on that field on host object of type {host.GetType().Name}."
                    + "\nThis is probably because the value was specified twice in the config. Making that assumption and clamping.");
                attribIndex = 0;
            }
            if (attribs[attribIndex].link)
            {
                if (!isLinkable || !int.TryParse(value.value, out int linkID))
                    return false;

                readLinks.AssignLink(linkID, new ReadFieldList.FieldItem(fieldInfo, attribs[attribIndex], host), -1);
                if (removeAfterUse)
                    value.name = string.Empty;

                return true;
            }

            object val = ReadValue(value.value, dataType, fieldType);
            if (removeAfterUse)
                value.name = string.Empty;

            if (val == null)
                return false;

            fieldInfo.SetValue(host, val);
            return true;
        }

        public bool Read(ConfigNode node, object host, int attribIndex)
        {
            if (attribIndex >= attribs.Length)
            {
                Debug.LogError($"[KSPCommunityFixes] Tried to read node `{node.name}` for field {fieldInfo.Name}, index was {attribIndex} but only {attribs.Length} [Persistent] attributes on that field on host object of type {host.GetType().Name}."
                    + "\nThis is probably because the node was specified twice in the config. Making that assumption and clamping.");
                attribIndex = 0;
            }
            int vCount = node._values.values.Count;
            int nCount = node._nodes.nodes.Count;

            object existing = fieldInfo.GetValue(host);
            bool wasNull = existing == null;
            if (dataType == DataType.Object || dataType == DataType.IConfigNode)
            {
                existing = ReadObject(node, existing, dataType, fieldType, out bool success);
                if (existing == null)
                    return false;
                if (wasNull)
                    fieldInfo.SetValue(host, existing);
                if (removeAfterUse)
                    node.name = string.Empty;
                return success;
            }

            if (dataType == DataType.Component)
            {
                if (wasNull)
                {
                    MonoBehaviour behv = CreateComponent(host, fieldType, attribs[attribIndex], node, out bool success);
                    if (behv == null)
                        return false;
                    fieldInfo.SetValue(host, behv);
                    if (removeAfterUse)
                        node.name = string.Empty;
                    return success;
                }
                else
                {
                    PersistentIConfigNode.ConfigNode_ReadObject_Prefix(existing, node, out bool success);
                    if (removeAfterUse)
                        node.name = string.Empty;
                    return success;
                }
            }

            Array arr = null;
            IList list = null;
            if (attribs[attribIndex].link)
            {
                int fieldIndex = 0;

                Value value;
                for (int i = 0; i < vCount; ++i)
                {
                    value = node._values.values[i];

                    _readLinks.AssignLink(int.Parse(value.value), new ReadFieldList.FieldItem(fieldInfo, attribs[attribIndex], host), fieldIndex++);
                }

                if (dataType == DataType.Array)
                {
                    arr = Array.CreateInstance(arrayType.fieldType, vCount);
                    for (int i = 0; i < vCount; ++i)
                    {
                        arr.SetValue(null, i);
                    }
                    fieldInfo.SetValue(host, arr);
                }
                else
                {
                    list = (IList)Activator.CreateInstance(fieldType);
                    for (int i = 0; i < vCount; ++i)
                    {
                        list.Add(null);
                    }
                    fieldInfo.SetValue(host, list);
                }
                return true;
            }
            if (arrayType.dataType == DataType.INVALID)
                return false;

            if (arrayType.dataType == DataType.Component)
                return ReadComponentArray(node, host, attribIndex);

            bool isArr = dataType == DataType.Array;
            bool arrReadSuccess = true;
            if (arrayType.dataType >= DataType.FirstValueType && arrayType.dataType <= DataType.LastValueType)
            {

                if (isArr)
                    arr = Array.CreateInstance(arrayType.fieldType, vCount);
                else
                    list = (IList)Activator.CreateInstance(fieldType);

                for (int i = 0; i < vCount; ++i)
                {
                    object val = ReadValue(node._values.values[i].value, arrayType.dataType, arrayType.fieldType);
                    if (isArr)
                        arr.SetValue(val, i);
                    else
                        list.Add(val);
                }
            }
            else
            {
                if (isArr)
                    arr = Array.CreateInstance(arrayType.fieldType, nCount);
                else
                    list = (IList)Activator.CreateInstance(fieldType);

                for (int i = 0; i < nCount; ++i)
                {
                    object val = ReadObject(node._nodes.nodes[i], null, arrayType.dataType, arrayType.fieldType, out bool readSuccess);
                    arrReadSuccess &= readSuccess;
                    if (isArr)
                        arr.SetValue(val, i);
                    else
                        list.Add(val);
                }
            }
            if (isArr)
                fieldInfo.SetValue(host, arr);
            else
                fieldInfo.SetValue(host, list);
            if (removeAfterUse)
                node.name = string.Empty;
            return arrReadSuccess;
        }

        public bool Write(object value, ConfigNode node, int attribIndex, int pass)
        {
            Persistent persistent = attribs[attribIndex];
            if (!persistent.isPersistant || (pass != 0 && persistent.pass != 0 && (persistent.pass & pass) == 0))
                return true;

            string fieldName = persistentNames[attribIndex];
            if (persistent.link)
            {
                if (!isLinkable)
                {
                    Debug.LogWarning("Field: '" + fieldInfo.Name + "' does not reference a PersistentLinkable type");
                    return false;
                }

                return WriteValue(fieldName, writeLinks.AssignLink(value), DataType.ValueInt, node);
            }

            if (dataType >= DataType.FirstValueType && dataType <= DataType.LastValueType)
            {
                return WriteValue(fieldName, value, dataType, node);
            }

            var subNode = new ConfigNode(fieldName);

            switch (dataType)
            {
                // Node types
                case DataType.IConfigNode:
                    (value as IConfigNode).Save(subNode);
                    if (subNode.HasData)
                        node._nodes.nodes.Add(subNode);
                    return true;
                case DataType.IList:
                case DataType.Array:
                    var list = value as IList;
                    if (list == null || arrayType == null)
                        return false;
                    string itemName = arrayType.persistentNames[attribIndex] ?? "item";
                    foreach (var item in list)
                    {
                        arrayType.Write(item, subNode, attribIndex, pass);
                    }
                    if (subNode.HasData)
                        node._nodes.nodes.Add(subNode);
                    return true;

                case DataType.Object:
                case DataType.Component:
                    PersistentIConfigNode.ConfigNode_WriteObject_Prefix(value, subNode, pass);
                    if (subNode.HasData)
                        node._nodes.nodes.Add(subNode);
                    return PersistentIConfigNode.WriteSuccess;
            }
            return false;
        }

        public static object ReadObject(ConfigNode node, object existing, DataType dataType, Type fieldType, out bool success)
        {
            success = false;
            switch (dataType)
            {
                case DataType.IConfigNode:
                    IConfigNode icn;
                    if (existing == null)
                    {
                        icn = TypeCache.GetNewInstance(fieldType) as IConfigNode;
                        if (icn == null)
                            return null;
                    }
                    else
                    {
                        icn = existing as IConfigNode;
                    }
                    success = true;
                    icn.Load(node);
                    return icn;
                case DataType.Object:
                    if (existing == null)
                        existing = TypeCache.GetNewInstance(fieldType);
                    if (existing == null)
                        return null;
                    PersistentIConfigNode.ConfigNode_ReadObject_Prefix(existing, node, out success);
                    return existing;
            }
            return null;
        }

        public static MonoBehaviour CreateComponent(object host, Type fieldType, Persistent attrib, ConfigNode node, out bool success)
        {
            success = false;
            MonoBehaviour hostComponent = host as MonoBehaviour;
            if (hostComponent == null)
            {
                Debug.LogWarning("Cannot instantiate a MonoBehaviour type inside a non-MonoBehaviour class (or a destroyed MonoBehaviour)");
                return null;
            }

            GameObject hostObject = null;
            switch (attrib.relationship)
            {
                case PersistentRelation.NoRelation:
                    hostObject = new GameObject();
                    break;
                case PersistentRelation.SameObject:
                    hostObject = hostComponent.gameObject;
                    break;
                case PersistentRelation.ChildObject:
                    hostObject = new GameObject();
                    hostObject.transform.parent = hostComponent.transform;
                    hostObject.transform.localPosition = Vector3.zero;
                    hostObject.transform.localRotation = Quaternion.identity;
                    break;
            }
            MonoBehaviour child = (MonoBehaviour)hostObject.AddComponent(fieldType);
            if (child == null)
            {
                Debug.LogError("Cannot create component of type '" + fieldType.Name + "'");
                if (hostObject != hostComponent.gameObject)
                {
                    GameObject.DestroyImmediate(hostObject);
                    return null;
                }
            }

            // We're not reading directly here because the class might have
            // other interfaces/links/etc.
            PersistentIConfigNode.ConfigNode_ReadObject_Prefix(child, node, out success);
            return child;
        }

        private bool ReadComponentArray(ConfigNode node, object host, int attribIndex)
        {
            int nCount = node._nodes.Count;
            List<object> objects = new List<object>();

            ConfigNode subNode;
            bool allSucceeded = true;
            for (int i = 0; i < nCount; ++i)
            {
                subNode = node.nodes[i];
                MonoBehaviour child = CreateComponent(host, arrayType.fieldType, attribs[attribIndex], subNode, out bool success);
                if (child == null)
                    continue;

                allSucceeded &= success;
                objects.Add(child);
            }

            int len = objects.Count;
            if (dataType == DataType.Array)
            {
                Array arr = Array.CreateInstance(arrayType.fieldType, len);
                for (int i = 0; i < len; ++i)
                {
                    arr.SetValue(objects[i], i);
                }
                fieldInfo.SetValue(host, arr);
            }
            else
            {
                IList list = (IList)Activator.CreateInstance(fieldType);
                for (int i = 0; i < len; ++i)
                {
                    list.Add(objects[i]);
                }
                fieldInfo.SetValue(host, list);
            }
            if (removeAfterUse)
                node.name = string.Empty;
            return allSucceeded;
        }

        public static object ReadValue(string value, DataType dataType, Type fieldType)
        {
            switch (dataType)
            {
                case DataType.ValueString:
                    return value;
                case DataType.ValueGuid:
                    return new Guid(value);
                case DataType.ValueBool:
                    if (bool.TryParse(value, out var b))
                        return b;
                    return null;
                case DataType.ValueDouble:
                    if (double.TryParse(value, out var d))
                        return d;
                    return null;
                case DataType.ValueFloat:
                    if (float.TryParse(value, out var f))
                        return f;
                    return null;
                case DataType.ValueDecimal:
                    if (decimal.TryParse(value, out var dc))
                        return dc;
                    return null;
                case DataType.ValueInt:
                    if (int.TryParse(value, out var i))
                        return i;
                    return null;
                case DataType.ValueUInt:
                    if (uint.TryParse(value, out var ui))
                        return ui;
                    return null;
                case DataType.ValueChar:
                    return value.Length > 0 ? value[0] : '\0';
                case DataType.ValueShort:
                    if (short.TryParse(value, out var s))
                        return s;
                    return null;
                case DataType.ValueUShort:
                    if (ushort.TryParse(value, out var us))
                        return us;
                    return null;
                case DataType.ValueLong:
                    if (long.TryParse(value, out var l))
                        return l;
                    return null;
                case DataType.ValueULong:
                    if (ulong.TryParse(value, out var ul))
                        return ul;
                    return null;
                case DataType.ValueByte:
                    if (byte.TryParse(value, out var by))
                        return by;
                    return null;
                case DataType.ValueSByte:
                    if (sbyte.TryParse(value, out var sb))
                        return sb;
                    return null;
                case DataType.ValueEnum:
                    try
                    {
                        return Enum.Parse(fieldType, value);
                    }
                    catch
                    {
                        return null;
                    }
                case DataType.ValueVector2:
                    return ParseVector2(value);
                case DataType.ValueVector3:
                    return ParseVector3(value);
                case DataType.ValueVector3d:
                    return ParseVector3D(value);
                case DataType.ValueVector4:
                    return ParseVector4(value);
                case DataType.ValueQuaternion:
                    return ParseQuaternion(value);
                case DataType.ValueQuaternionD:
                    return ParseQuaternionD(value);
                case DataType.ValueMatrix4x4:
                    return ParseMatrix4x4(value);
                case DataType.ValueColor:
                    return ParseColor(value);
                case DataType.ValueColor32:
                    return ParseColor32(value);
            }
            return null;
        }

        public static bool WriteValue(string fieldName, object value, DataType dataType, ConfigNode node)
        {
            string v = WriteValue(value, dataType);
            if (v == null)
                return false;

            node._values.values.Add(new Value(fieldName, v));
            return true;
        }

        public static string WriteValue(object value, DataType dataType)
        {
            switch (dataType)
            {
                case DataType.ValueString:
                    return (string)value;
                case DataType.ValueGuid:
                    return ((Guid)value).ToString();
                case DataType.ValueBool:
                    return ((bool)value).ToString(_Invariant);
                case DataType.ValueDouble:
                    return ((double)value).ToString("G17");
                case DataType.ValueFloat:
                    return ((float)value).ToString("G9");
                case DataType.ValueDecimal:
                    return ((decimal)value).ToString(_Invariant);
                case DataType.ValueInt:
                    return ((int)value).ToString(_Invariant);
                case DataType.ValueUInt:
                    return ((uint)value).ToString(_Invariant);
                case DataType.ValueChar:
                    return ((char)value).ToString(_Invariant);
                case DataType.ValueShort:
                    return ((short)value).ToString(_Invariant);
                case DataType.ValueUShort:
                    return ((ushort)value).ToString(_Invariant);
                case DataType.ValueLong:
                    return ((long)value).ToString(_Invariant);
                case DataType.ValueULong:
                    return ((ulong)value).ToString(_Invariant);
                case DataType.ValueByte:
                    return ((byte)value).ToString(_Invariant);
                case DataType.ValueSByte:
                    return ((sbyte)value).ToString(_Invariant);
                case DataType.ValueEnum:
                    return ((System.Enum)value).ToString();
                case DataType.ValueVector2:
                    return WriteVector((Vector2)value);
                case DataType.ValueVector3:
                    return WriteVector((Vector3)value);
                case DataType.ValueVector3d:
                    return WriteVector((Vector3)value);
                case DataType.ValueVector4:
                    return WriteVector((Vector4)value);
                case DataType.ValueQuaternion:
                    return WriteQuaternion((Quaternion)value);
                case DataType.ValueQuaternionD:
                    return WriteQuaternion((QuaternionD)value);
                case DataType.ValueMatrix4x4:
                    return WriteMatrix4x4((Matrix4x4)value);
                case DataType.ValueColor:
                    return WriteColor((Color)value);
                case DataType.ValueColor32:
                    return WriteColor((Color32)value);
            }
            return null;
        }

        public static DataType ValueDataType(Type fieldType)
        {
            if (!fieldType.IsValueType)
            {
                if (fieldType == typeof(string))
                    return DataType.ValueString;

                return DataType.INVALID;
            }
            if (fieldType == typeof(Guid))
                return DataType.ValueGuid;
            if (fieldType == typeof(bool))
                return DataType.ValueBool;
            if (fieldType == typeof(byte))
                return DataType.ValueByte;
            if (fieldType == typeof(sbyte))
                return DataType.ValueSByte;
            if (fieldType == typeof(char))
                return DataType.ValueChar;
            if (fieldType == typeof(decimal))
                return DataType.ValueDecimal;
            if (fieldType == typeof(double))
                return DataType.ValueDouble;
            if (fieldType == typeof(float))
                return DataType.ValueFloat;
            if (fieldType == typeof(int))
                return DataType.ValueInt;
            if (fieldType == typeof(uint))
                return DataType.ValueUInt;
            if (fieldType == typeof(long))
                return DataType.ValueLong;
            if (fieldType == typeof(ulong))
                return DataType.ValueULong;
            if (fieldType == typeof(short))
                return DataType.ValueShort;
            if (fieldType == typeof(ushort))
                return DataType.ValueUShort;
            if (fieldType == typeof(Vector2))
                return DataType.ValueVector2;
            if (fieldType == typeof(Vector3))
                return DataType.ValueVector3;
            if (fieldType == typeof(Vector3d))
                return DataType.ValueVector3d;
            if (fieldType == typeof(Vector4))
                return DataType.ValueVector4;
            if (fieldType == typeof(Quaternion))
                return DataType.ValueQuaternion;
            if (fieldType == typeof(QuaternionD))
                return DataType.ValueQuaternionD;
            if (fieldType == typeof(Matrix4x4))
                return DataType.ValueMatrix4x4;
            if (fieldType == typeof(Color))
                return DataType.ValueColor;
            if (fieldType == typeof(Color32))
                return DataType.ValueColor32;
            if (fieldType.IsEnum)
                return DataType.ValueEnum;

            return DataType.INVALID;
        }

        private void SetDataType(bool allowArrays)
        {
            if (typeof(IConfigNode).IsAssignableFrom(fieldType))
                dataType = DataType.IConfigNode;
            else if (ValueDataType(fieldType) is var dt && dt != DataType.INVALID)
                dataType = dt;
            else if (!allowArrays || !FindSetArrayType(fieldType))
            {
                if (fieldType.IsAssignableFrom(typeof(MonoBehaviour)) || fieldType.IsSubclassOf(typeof(MonoBehaviour)))
                    dataType = DataType.Component;
                else
                    dataType = DataType.Object;
            }
        }

        private bool FindSetArrayType(Type seqType)
        {
            if (seqType.IsArray)
            {
                arrayType = new FieldData(seqType.GetElementType(), attribs);
                dataType = DataType.Array;
                return true;
            }

            if (seqType.IsGenericType)
            {
                Type[] args = seqType.GetGenericArguments();
                if (args != null && args.Length > 0)
                {
                    int iC = args.Length;
                    for (int i = 0; i < iC; ++i)
                    {
                        Type ienum = typeof(IEnumerable<>).MakeGenericType(args[i]);
                        if (ienum.IsAssignableFrom(seqType))
                        {
                            arrayType = new FieldData(args[i], attribs);
                            dataType = DataType.IList;
                            return true;
                        }
                    }
                }
            }

            Type[] ifaces = seqType.GetInterfaces();
            if (ifaces != null && ifaces.Length > 0)
            {
                int iC = ifaces.Length;
                for (int i = 0; i < iC; ++i)
                {
                    Type ienum = FindIEnumerable(ifaces[i]);
                    if (ienum != null)
                    {
                        arrayType = new FieldData(ienum, attribs);
                        dataType = DataType.IList;
                        return true;
                    }
                }
            }
            if (seqType.BaseType != null && seqType.BaseType != typeof(object))
            {
                return FindSetArrayType(seqType.BaseType);
            }

            return false;
        }
    }

    public class TypeCache
    {
        private static readonly Dictionary<Type, TypeCache> cache = new Dictionary<Type, TypeCache>();
        //private static readonly Dictionary<Type, ConstructorInfo> _typeToConstrutor = new Dictionary<Type, ConstructorInfo>();

        private List<FieldData> _fields = new List<FieldData>();
        private Dictionary<string, FieldData> _nameToField = new Dictionary<string, FieldData>();
        private Dictionary<string, int> _seenFieldCounts = new Dictionary<string, int>();

        private bool _isPersistentLinkable;

        public TypeCache(Type t)
        {
            _isPersistentLinkable = HasAttribute(t, typeof(PersistentLinkable));

            MemberInfo[] members = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            for (int i = 0, iC = members.Length; i < iC; ++i)
            {
                var attribs = (Persistent[])members[i].GetCustomAttributes(typeof(Persistent), inherit: true);
                if (attribs.Length == 0)
                    continue;

                var data = new FieldData(members[i], attribs);
                if (data.dataType != DataType.INVALID)
                {
                    _fields.Add(data);
                    foreach (var s in data.persistentNames)
                        _nameToField[s] = data;
                }
            }
        }

        private int SeenFieldIndex(string fieldName)
        {
            // We need to track how many times we've seen this field name
            // because fields can have >1 Persistent attribute and we
            // need to index.
            if (!_seenFieldCounts.TryGetValue(fieldName, out int numSeen))
                numSeen = 0;
            _seenFieldCounts[fieldName] = numSeen + 1;
            return numSeen;
        }

        public bool Read(object host, ConfigNode node)
        {
            if (_isPersistentLinkable)
            {
                string linkUID = node.GetValue("link_uid");
                int linkID = -1;
                if (linkUID != null)
                    linkID = int.Parse(linkUID);

                readLinks.AssignLinkable(linkID, host);
            }

            bool allSucceeded = true;
            int count = node.values.Count;
            for (int i = 0; i < count; ++i)
            {
                Value value = node.values[i];
                if (!_nameToField.TryGetValue(value.name, out FieldData fieldItem))
                    continue;

                int index = SeenFieldIndex(value.name);
                // Leaving this check commented since the stock code
                // handles this dumbly too.
                //if (value.name != fieldItem.persistentNames[index])
                //    Debug.LogError($"[KSPCommunityFixes] ConfigNode.ReadObject: seeing value {value.name} for time {index} but expected name for field {fieldItem.field.Name} is {fieldItem.persistentNames[index]");

                allSucceeded &= fieldItem.Read(value, host, index);
            }
            _seenFieldCounts.Clear();

            count = node.nodes.Count;
            for (int j = 0; j < count; j++)
            {
                ConfigNode subNode = node.nodes[j];
                if (!_nameToField.TryGetValue(subNode.name, out FieldData fieldItem))
                    continue;

                allSucceeded &= fieldItem.Read(subNode, host, SeenFieldIndex(subNode.name));
            }
            _seenFieldCounts.Clear();
            return allSucceeded;
        }

        public bool Write(object obj, ConfigNode node, int pass)
        {
            if (_isPersistentLinkable)
            {
                node.AddValue("link_uid", writeLinks.AssignTarget(obj));
            }

            int num = _fields.Count;
            bool allSucceeded = true;
            for (int i = 0; i < num; i++)
            {
                FieldData fieldData = _fields[i];

                object value = fieldData.fieldInfo.GetValue(obj);
                if (value == null)
                {
                    continue;
                }

                int persistentCount = fieldData.attribs.Length;
                for (int j = 0; j < persistentCount; j++)
                {
                    allSucceeded &= fieldData.Write(value, node, j, pass);
                }
            }
            return allSucceeded;
        }

        public static TypeCache GetOrCreate(Type t)
        {
            if (cache.TryGetValue(t, out var tc))
                return tc;

            return CreateAndAdd(t);
        }

        public static TypeCache CreateAndAdd(Type t)
        {
            var tc = new TypeCache(t);
            if (tc._fields.Count == 0)
            {
                Debug.LogError($"[KSPCommunityFixes]: No Persistent fields on object of type {t.Name} that is referenced in persistent field, adding as null to TypeCache.");
                tc = null;
            }

            cache[t] = tc;
            return tc;
        }

        public static object GetNewInstance(Type t)
        {
            //if (!_typeToConstrutor.TryGetValue(t, out var cons))
            //{
            //    cons = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            //    _typeToConstrutor[t] = cons; // yes this might be null, but we're caching that it's null
            //}
            //if (cons == null)
            //    return null;

            //return cons.Invoke(null);
            return Activator.CreateInstance(t, true);
        }
    }

#if DEBUG_PERSISTENTICONFIGNODE

    public class PersistentIConfigNodeTestModule : PartModule
    {
        [Persistent] 
        private FloatCurve foo;

        [KSPEvent(active = true, guiActive = true, guiActiveEditor = true, guiName = "IConfigNode test")]
        public void Test()
        {
            System.Random rnd = new System.Random();

            foo = new FloatCurve();
            float time = rnd.Next(100);
            float value = rnd.Next(100);
            foo.Add(time, value);

            Debug.Log($"[IConfigNode] before serialization, time={time} value={value}");
            ConfigNode configNode = CreateConfigFromObject(this);
            foo = new FloatCurve(new[]{new Keyframe(999f, 999f)});
            LoadObjectFromConfig(this, configNode);
            if (foo.Curve.length == 0)
            {
                Debug.LogError($"[IConfigNode] key not found !");
                return;
            }

            if (time != foo.Curve[0].time || value != foo.Curve[0].value)
            {
                Debug.LogError($"[IConfigNode] time/value don't match, time={foo.Curve[0].time} value={foo.Curve[0].value}");
                return;
            }

            Debug.Log($"[IConfigNode] after serialization, time={foo.Curve[0].time} value={foo.Curve[0].value}");
        }
    }

#endif

}
