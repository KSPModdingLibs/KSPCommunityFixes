﻿// - Add support for IConfigNode serialization
// - Add support for Guid serialization

using HarmonyLib;
using System;
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
            MethodInfo ConfigNode_ReadObject = AccessTools.Method(typeof(ConfigNode), nameof(ConfigNode.ReadObject), new Type[] {typeof(object), typeof(ConfigNode)});
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

        private static bool ConfigNode_WriteObject_Prefix(object obj, ConfigNode node, int pass)
        {
            (obj as IPersistenceSave)?.PersistenceSave();
            if (HasAttribute(obj.GetType(), typeof(PersistentLinkable)))
            {
                node.AddValue("link_uid", writeLinks.AssignTarget(obj));
            }
            Type type = obj.GetType();
            MemberInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            MemberInfo[] array = fields;
            int num = array.Length;
            for (int i = 0; i < num; i++)
            {
                MemberInfo memberInfo = array[i];
                Persistent[] array2 = (Persistent[])memberInfo.GetCustomAttributes(typeof(Persistent), inherit: true);
                int num2 = array2.Length;
                for (int j = 0; j < num2; j++)
                {
                    Persistent persistent = array2[j];
                    if (!persistent.isPersistant || (pass != 0 && persistent.pass != 0 && (persistent.pass & pass) == 0))
                    {
                        continue;
                    }
                    string fieldName = RetrieveFieldName(memberInfo, persistent);
                    FieldInfo field = type.GetField(memberInfo.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    object value = field.GetValue(obj);
                    Type fieldType = field.FieldType;
                    if (value == null)
                    {
                        continue;
                    }
                    if (persistent.link)
                    {
                        if (!HasAttribute(obj.GetType(), typeof(PersistentLinkable)))
                        {
                            Debug.LogWarning("Field: '" + memberInfo.Name + "' does not reference a PersistentLinkable type");
                        }
                        else
                        {
                            WriteValue(fieldName, typeof(int), writeLinks.AssignLink(value), node);
                        }
                    }
                    // begin edit
                    else if (IsIConfigNode(fieldType))
                    {
                        WriteIConfigNode(fieldName, value, node);
                    }
                    else if (fieldType == typeof(Guid))
                    {
                        node.AddValue(fieldName, ((Guid)value).ToString());
                    }
                    // end edit
                    else if (IsValue(fieldType))
                    {
                        WriteValue(fieldName, fieldType, value, node);
                    }
                    else if (IsArrayType(fieldType))
                    {
                        WriteArrayTypes(fieldName, fieldType, value, node, persistent);
                    }
                    else
                    {
                        WriteStruct(fieldName, field, value, node, pass);
                        
                    }
                }
            }

            return false;
        }

        private static bool IsIConfigNode(Type fieldType)
        {
            return typeof(IConfigNode).IsAssignableFrom(fieldType);
        }

        private static void WriteIConfigNode(string fieldName, object value, ConfigNode node)
        {
            IConfigNode icn = (IConfigNode) value;
            ConfigNode n = node.GetNode(fieldName);
            if (n == null)
            {
                n = new ConfigNode(fieldName);
                node.AddNode(n);
            }
            else
            {
                n.ClearData();
            }

            icn.Save(n);
        }

        private static bool ConfigNode_ReadObject_Prefix(object obj, ConfigNode node, out bool __result)
        {
            ReadFieldList readFieldList = new ReadFieldList(obj);
            if (HasAttribute(obj.GetType(), typeof(PersistentLinkable)))
            {
                if (!node.HasValue("link_uid"))
                {
                    readLinks.AssignLinkable(-1, obj);
                }
                else
                {
                    int linkID = int.Parse(node.GetValue("link_uid"));
                    readLinks.AssignLinkable(linkID, obj);
                }
            }
            int count = node.values.Count;
            for (int i = 0; i < count; i++)
            {
                Value value = node.values[i];
                ReadFieldList.FieldItem fieldItem = readFieldList[value.name];
                if (fieldItem == null)
                {
                    continue;
                }
                if (fieldItem.kspField.link)
                {
                    int linkID2 = int.Parse(value.value);
                    readLinks.AssignLink(linkID2, fieldItem, -1);
                    if (removeAfterUse)
                    {
                        value.name = "";
                    }
                }
                // begin edit 1
                else if (fieldItem.fieldType == typeof(Guid))
                {
                    fieldItem.fieldInfo.SetValue(fieldItem.host, new Guid(value.value));
                }
                // end edit
                else if (IsValue(fieldItem.fieldType))
                {
                    object obj2 = ReadValue(fieldItem.fieldType, value.value);
                    if (obj2 != null)
                    {
                        fieldItem.fieldInfo.SetValue(fieldItem.host, obj2);
                    }
                    if (removeAfterUse)
                    {
                        value.name = "";
                    }
                }
            }
            count = node.nodes.Count;
            for (int j = 0; j < count; j++)
            {
                ConfigNode configNode = node.nodes[j];
                ReadFieldList.FieldItem fieldItem2 = readFieldList[configNode.name];
                if (fieldItem2 != null)
                {
                    // begin edit 2
                    if (IsIConfigNode(fieldItem2.fieldType))
                    {
                        ReadIConfigNode(fieldItem2, configNode);
                    }
                    // end edit
                    else if (IsArrayType(fieldItem2.fieldType))
                    {
                        ReadArray(fieldItem2, configNode);
                    }
                    else
                    {
                        ReadObject(fieldItem2, configNode);
                    }
                    if (removeAfterUse)
                    {
                        configNode.name = "";
                    }
                }
            }

            if (obj is IPersistenceLoad persistenceLoad)
            {
                iPersistentLoaders.Add(persistenceLoad);
            }

            __result = true;
            return false;
        }

        private static void ReadIConfigNode(ReadFieldList.FieldItem fieldItem, ConfigNode configNode)
        {
            IConfigNode icn;
            try
            {
                icn = (IConfigNode)Activator.CreateInstance(fieldItem.fieldType);
            }
            catch
            {
                Debug.LogError($"Couldn't deserialize field {fieldItem.name} on {fieldItem.host}, field value is null and {fieldItem.fieldType} has no parameterless constructor");
                return;
            }
            icn.Load(configNode);
            fieldItem.fieldInfo.SetValue(fieldItem.host, icn);
        }
    }

#if DEBUG

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
