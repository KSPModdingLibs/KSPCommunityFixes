using KSPCommunityFixes.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace KSPCommunityFixes.Performance
{
    internal class PartParsingPerf : BasePatch
    {
        internal static Stopwatch iconCompilationWatch = new Stopwatch();

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(PartLoader), nameof(PartLoader.CreatePartIcon));

            AddPatch(PatchType.Prefix, typeof(PartLoader), nameof(PartLoader.ApplyPartValue));
        }

        static List<Component> componentBuffer = new List<Component>();
        static List<Component> iconHiddenComponentBuffer = new List<Component>();
        static List<GameObject> colliderObjectsToDestroy = new List<GameObject>();
        static List<Renderer> iconRenderers = new List<Renderer>();
        static List<Material> materialBuffer = new List<Material>();

        private static bool shaderRefsAcquired;
        private static Shader shader_ScreenSpaceMaskSpecular;
        private static Shader shader_ScreenSpaceMaskBumpedSpecularTransparent;
        private static Shader shader_ScreenSpaceMaskBumped;
        private static Shader shader_ScreenSpaceMaskAlphaCutoffBackground;
        private static Shader shader_ScreenSpaceMaskUnlit;
        private static Shader shader_ScreenSpaceMask;

        private static Shader GetIconShader(string materialShaderName)
        {
            if (!shaderRefsAcquired)
            {
                shader_ScreenSpaceMaskSpecular = Shader.Find("KSP/ScreenSpaceMaskSpecular");
                shader_ScreenSpaceMaskBumpedSpecularTransparent = Shader.Find("KSP/ScreenSpaceMaskBumpedSpecular(Transparent)");
                shader_ScreenSpaceMaskBumped = Shader.Find("KSP/ScreenSpaceMaskBumped");
                shader_ScreenSpaceMaskAlphaCutoffBackground = Shader.Find("KSP/ScreenSpaceMaskAlphaCutoffBackground");
                shader_ScreenSpaceMaskUnlit = Shader.Find("KSP/ScreenSpaceMaskUnlit");
                shader_ScreenSpaceMask = Shader.Find("KSP/ScreenSpaceMask");
                shaderRefsAcquired = true;
            }

            if (materialShaderName == "KSP/Bumped Specular (Mapped)")
                return shader_ScreenSpaceMaskSpecular;
            if (materialShaderName == "KSP/Bumped Specular (Transparent)")
                return shader_ScreenSpaceMaskBumpedSpecularTransparent;
            if (materialShaderName.Contains("Bumped"))
                return shader_ScreenSpaceMaskBumped;
            if (materialShaderName.Contains("KSP/Alpha/CutoffBackground"))
                return shader_ScreenSpaceMaskAlphaCutoffBackground;
            if (materialShaderName == "KSP/Unlit")
                return shader_ScreenSpaceMaskUnlit;

            return shader_ScreenSpaceMask;
        }

        private static bool PartLoader_CreatePartIcon_Prefix(GameObject newPart, out float iconScale, out GameObject __result)
        {
            iconCompilationWatch.Start();
            GameObject iconPartObject = UObject.Instantiate(newPart);
            iconPartObject.SetActive(true);
            Part iconPart = iconPartObject.GetComponent<Part>();

            if (iconPart.IsNotNullOrDestroyed())
            {
                int i = 0;
                for (int count = iconPart.Modules.Count; i < count; i++)
                    iconPart.Modules[i].OnIconCreate();
            }

            Bounds partBounds = default;
            iconPartObject.GetComponentsInChildren(false, componentBuffer);
            try
            {
                for (int i = componentBuffer.Count; i-- > 0;)
                {
                    Component c = componentBuffer[i];
                    if (c is Part
                        || c is PartModule
                        || c is EffectBehaviour
                        || c is WheelCollider
                        || c is SmokeTrailControl
                        || c is FXPrefab
                        || c is ParticleSystem
                        || c is Light
                        || c is Animation
                        || c is DAE)
                    {
                        UObject.DestroyImmediate(c, false);
                    }
                    else if (!c.IsDestroyed() && (c is Renderer || c is MeshFilter))
                    {
                        // we are adding renderers that will potentially be destroyed latter
                        if (!(c is MeshFilter))
                            iconRenderers.Add((Renderer)c);

                        if ((c is MeshRenderer || c is SkinnedMeshRenderer || c is MeshFilter) && c.gameObject.CompareTag("Icon_Hidden"))
                        {
                            c.gameObject.GetComponentsInChildren(false, iconHiddenComponentBuffer);

                            for (int j = iconHiddenComponentBuffer.Count; j-- > 0;)
                            {
                                Component child = iconHiddenComponentBuffer[j];
                                if (!child.IsDestroyed() && (child is MeshRenderer || child is SkinnedMeshRenderer || child is MeshFilter))
                                    UObject.DestroyImmediate(child, false);
                            }

                            iconHiddenComponentBuffer.Clear();
                            UObject.DestroyImmediate(c, false);
                        }
                    }
                    else if (c is Collider)
                    {
                        if (c.gameObject.name == "collider")
                            colliderObjectsToDestroy.Add(c.gameObject);
                        else
                            UObject.DestroyImmediate(c, false);
                    }
                }

                for (int i = colliderObjectsToDestroy.Count; i-- > 0;)
                    UObject.DestroyImmediate(colliderObjectsToDestroy[i]);

                iconPartObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                bool first = true;
                for (int i = iconRenderers.Count; i-- > 0;)
                {
                    Renderer renderer = iconRenderers[i];
                    if (renderer.IsDestroyed())
                        continue;

                    if (!(renderer is ParticleSystemRenderer))
                    {
                        if (first)
                        {
                            first = false;
                            partBounds = renderer.bounds;
                        }
                        else
                        {
                            partBounds.Encapsulate(renderer.bounds);
                        }
                    }

                    renderer.GetSharedMaterials(materialBuffer);
                    for (int j = materialBuffer.Count; j-- > 0;)
                    {
                        Material partMaterial = materialBuffer[j];
                        if (partMaterial.IsNullOrDestroyed())
                            continue;

                        Material iconMaterial = new Material(GetIconShader(partMaterial.shader.name));
                        iconMaterial.name = partMaterial.name;
                        iconMaterial.CopyPropertiesFromMaterial(partMaterial);
                        if (iconMaterial.HasProperty(ShaderHelpers.ColorPropId))
                        {
                            Color originalColor = iconMaterial.color;
                            iconMaterial.SetColor(PropertyIDs._Color, new Color(
                                originalColor.r < 0.5f ? 0.5f : originalColor.r,
                                originalColor.g < 0.5f ? 0.5f : originalColor.g,
                                originalColor.b < 0.5f ? 0.5f : originalColor.b));
                        }
                        else
                        {
                            iconMaterial.SetColor(PropertyIDs._Color, Color.white);
                        }

                        iconMaterial.SetFloat(PropertyIDs._Multiplier, PartLoader.Instance.shaderMultiplier);
                        iconMaterial.SetFloat(PropertyIDs._MinX, 0f);
                        iconMaterial.SetFloat(PropertyIDs._MaxX, 1f);
                        iconMaterial.SetFloat(PropertyIDs._MinY, 0f);
                        iconMaterial.SetFloat(PropertyIDs._MaxY, 1f);
                        materialBuffer[j] = iconMaterial;
                    }

                    if (materialBuffer.Count == 1)
                        renderer.sharedMaterial = materialBuffer[0];
                    else
                        renderer.sharedMaterials = materialBuffer.ToArray();

                    materialBuffer.Clear();
                }

            }
            finally
            {
                componentBuffer.Clear();
                iconHiddenComponentBuffer.Clear();
                colliderObjectsToDestroy.Clear();
                iconRenderers.Clear();
                materialBuffer.Clear();
            }

            Vector3 size = partBounds.size;
            float x = Math.Abs(size.x);
            float y = Math.Abs(size.y);
            float z = Math.Abs(size.z);
            iconScale = x > y ? x : y;
            iconScale = iconScale > z ? iconScale : z;
            iconScale = 1f / iconScale;

            GameObject iconObject = new GameObject(iconPartObject.name + " icon");
            iconPartObject.transform.parent = iconObject.transform;
            iconPartObject.transform.localScale = Vector3.one * iconScale;
            iconPartObject.transform.localPosition = partBounds.center * (0f - iconScale);
            iconObject.transform.parent = PartLoader.Instance.transform;
            iconObject.SetActive(false);
            iconPartObject.SetActive(true);
            __result = iconObject;
            iconCompilationWatch.Stop();
            return false;
        }



        private static Dictionary<string, PartFieldSetter> partFieldsSetters = new Dictionary<string, PartFieldSetter>(300);
        private static Dictionary<string, PartFieldSetter> compoundPartFieldsSetters = new Dictionary<string, PartFieldSetter>(300);
        private static Dictionary<Type, Dictionary<string, PartFieldSetter>> unknownPartFieldsSetters = new Dictionary<Type, Dictionary<string, PartFieldSetter>>();

        private abstract class PartFieldSetter
        {
            public abstract bool SetField(object instance, string value);
        }

        private sealed class FloatSetter : PartFieldSetter
        {
            private Action<object, float> setter;

            public FloatSetter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceSetter<float>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                float value = float.Parse(strValue);
                setter(instance, value);
                return true;
            }
        }

        private sealed class DoubleSetter : PartFieldSetter
        {
            private Action<object, double> setter;

            public DoubleSetter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceSetter<double>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                double value = double.Parse(strValue);
                setter(instance, value);
                return true;
            }
        }

        private sealed class IntSetter : PartFieldSetter
        {
            private Action<object, int> setter;

            public IntSetter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceSetter<int>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                int value = int.Parse(strValue);
                setter(instance, value);
                return true;
            }
        }

        private sealed class BoolSetter : PartFieldSetter
        {
            private Action<object, bool> setter;

            public BoolSetter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceSetter<bool>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                bool value = bool.Parse(strValue);
                setter(instance, value);
                return true;
            }
        }

        private sealed class StringSetter : PartFieldSetter
        {
            private Action<object, string> setter;

            public StringSetter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceSetter<string>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                setter(instance, strValue);
                return true;
            }
        }

        private sealed class EnumSetter : PartFieldSetter
        {
            private Action<object, object> setter;
            private Type enumType;

            public EnumSetter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceBoxedValueSetter(fieldInfo);
                enumType = fieldInfo.FieldType;
            }

            public override bool SetField(object instance, string strValue)
            {
                object value = Enum.Parse(enumType, strValue);
                setter(instance, value);
                return true;
            }
        }

        private sealed class Vector2Setter : PartFieldSetter
        {
            private Action<object, Vector2> setter;

            public Vector2Setter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceSetter<Vector2>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                string[] array = strValue.Split(',');
                if (array.Length < 2)
                {
                    PDebug.Log($"WARNING: {strValue} is nor formatted properly! proper format for Vector2 is x,y");
                    return false;
                }
                setter(instance, new Vector2(float.Parse(array[0]), float.Parse(array[1])));
                return true;
            }
        }

        private sealed class Vector3Setter : PartFieldSetter
        {
            private Action<object, Vector3> setter;

            public Vector3Setter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceSetter<Vector3>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                string[] array = strValue.Split(',');
                if (array.Length < 3)
                {
                    PDebug.Log($"WARNING: {strValue} is nor formatted properly! proper format for Vector3 is x,y,z");
                    return false;
                }
                setter(instance, new Vector3(float.Parse(array[0]), float.Parse(array[1]), float.Parse(array[2])));
                return true;
            }
        }

        private sealed class Vector4Setter : PartFieldSetter
        {
            private Action<object, Vector4> setter;

            public Vector4Setter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceSetter<Vector4>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                string[] array = strValue.Split(',');
                if (array.Length < 4)
                {
                    PDebug.Log($"WARNING: {strValue} is nor formatted properly! proper format for Vector4 is x,y,z,w");
                    return false;
                }
                setter(instance, new Vector4(float.Parse(array[0]), float.Parse(array[1]), float.Parse(array[2]), float.Parse(array[3])));
                return true;
            }
        }

        private sealed class QuaternionSetter : PartFieldSetter
        {
            private Action<object, Quaternion> setter;

            public QuaternionSetter(FieldInfo fieldInfo)
            {
                setter = CreateInstanceSetter<Quaternion>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                string[] array = strValue.Split(',');
                if (array.Length < 4)
                {
                    PDebug.Log($"WARNING: {strValue} is nor formatted properly! proper format for Quaternion is angle(deg),x,y,z");
                    return false;
                }
                setter(instance, Quaternion.AngleAxis(float.Parse(array[0]), new Vector3(float.Parse(array[1]), float.Parse(array[2]), float.Parse(array[3]))));
                return true;
            }
        }

        private sealed class DragModelTypeSetter : PartFieldSetter
        {
            public override bool SetField(object instance, string value)
            {
                Part part = (Part)instance;
                switch (value.ToUpper())
                {
                    case "SPHERICAL":
                        part.dragModel = Part.DragModel.SPHERICAL;
                        break;
                    case "NONE":
                    case "OVERRIDE":
                        part.dragModel = Part.DragModel.NONE;
                        break;
                    case "CYLINDRICAL":
                        part.dragModel = Part.DragModel.CYLINDRICAL;
                        break;
                    case "CONIC":
                        part.dragModel = Part.DragModel.CONIC;
                        break;
                    default:
                        part.dragModel = Part.DragModel.CUBE;
                        break;
                }
                return true;
            }
        }

        private sealed class PartRendererBoundsIgnoreTypeSetter : PartFieldSetter
        {
            public override bool SetField(object instance, string value)
            {
                Part part = (Part)instance;
                string[] splitted = value.Split(',');
                part.partRendererBoundsIgnore = new List<string>();
                for (int i = 0; i < splitted.Length; i++)
                {
                    part.partRendererBoundsIgnore.Add(splitted[i].Trim());
                }
                return true;
            }
        }

        private sealed class AnonymousSetter : PartFieldSetter
        {
            private Action<object, object> setter;
            //private Func<string, object> parser;
            private MethodInfo parserMethod;
            private string[] valueBuffer = new string[1];

            public AnonymousSetter(FieldInfo fieldInfo, MethodInfo parserMethod)
            {
                //parser = (Func<string, object>)Delegate.CreateDelegate(typeof(Func<string, object>), parserMethod);
                this.parserMethod = parserMethod;
                if (fieldInfo.FieldType.IsValueType)
                    setter = CreateInstanceBoxedValueSetter(fieldInfo);
                else
                    setter = CreateInstanceSetter<object>(fieldInfo);
            }

            public override bool SetField(object instance, string strValue)
            {
                //object value = parser(strValue);
                valueBuffer[0] = strValue;
                object value = parserMethod.Invoke(null, valueBuffer);
                setter(instance, value);
                return true;
            }
        }

        private static Action<object, T> CreateInstanceSetter<T>(FieldInfo field)
        {
            string methodName = field.ReflectedType.FullName + ".set_" + field.Name;
            DynamicMethod setterMethod = new DynamicMethod(methodName, null, new Type[2] { typeof(object), typeof(T) }, true);
            ILGenerator gen = setterMethod.GetILGenerator();
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldarg_1);
            gen.Emit(OpCodes.Stfld, field);
            gen.Emit(OpCodes.Ret);
            return (Action<object, T>)setterMethod.CreateDelegate(typeof(Action<object, T>));
        }

        private static Action<object, object> CreateInstanceBoxedValueSetter(FieldInfo field)
        {
            string methodName = field.ReflectedType.FullName + ".set_" + field.Name;
            DynamicMethod setterMethod = new DynamicMethod(methodName, null, new Type[2] { typeof(object), typeof(object) }, true);
            ILGenerator gen = setterMethod.GetILGenerator();
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldarg_1);
            gen.Emit(OpCodes.Unbox_Any, field.FieldType);
            gen.Emit(OpCodes.Stfld, field);
            gen.Emit(OpCodes.Ret);
            return (Action<object, object>)setterMethod.CreateDelegate(typeof(Action<object, object>));
        }

        private static bool partFieldDictionariesAreBuilt;

        private static void BuildPartFieldDictionaries()
        {
            PopulatePartFieldDictionary(typeof(Part), partFieldsSetters);
            PopulatePartFieldDictionary(typeof(CompoundPart), compoundPartFieldsSetters);

            foreach (AssemblyLoader.LoadedAssembly assembly in AssemblyLoader.loadedAssemblies.assemblies)
            {
                if (assembly.typesDictionary.TryGetValue(typeof(Part), out Dictionary<string, Type> partTypes))
                {
                    foreach (Type unknownPartType in partTypes.Values)
                    {
                        if (unknownPartType != typeof(Part) && unknownPartType != typeof(CompoundPart))
                        {
                            Dictionary<string, PartFieldSetter> unknownTypeDict = new Dictionary<string, PartFieldSetter>(300);
                            unknownPartFieldsSetters.Add(unknownPartType, unknownTypeDict);
                            PopulatePartFieldDictionary(unknownPartType, unknownTypeDict);
                        }
                    }
                }
            }
        }
            
        private static void PopulatePartFieldDictionary(Type partType, Dictionary<string, PartFieldSetter> typeSetterDict)
        {
            foreach (FieldInfo fieldInfo in partType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                PartFieldSetter setter;

                string fieldName = fieldInfo.Name;

                if (fieldName == "dragModelType")
                {
                    setter = new DragModelTypeSetter();
                }
                else if (fieldName == "partRendererBoundsIgnore")
                {
                    setter = new PartRendererBoundsIgnoreTypeSetter();
                }
                else if (fieldName == "iconCenter"
                         || fieldName == "alphaCutoff"
                         || fieldName == "scale"
                         || fieldName == "texture"
                         || fieldName == "normalmap"
                         || fieldName == "name"
                         || fieldName == "specPower"
                         || fieldName == "rimFalloff"
                         || fieldName == "mesh"
                         || fieldName == "subcategory"
                         || fieldName == "module"
                         || fieldName == "exportScale"
                         || fieldName.StartsWith("node")
                         || fieldName.StartsWith("fx")
                         || fieldName.StartsWith("sound"))
                {
                    // ignored field, just have a null entry
                    setter = null;
                }
                else
                {
                    Type fieldType = fieldInfo.FieldType;
                    if (fieldType == typeof(float))
                    {
                        setter = new FloatSetter(fieldInfo);
                    }
                    else if (fieldType == typeof(double))
                    {
                        setter = new DoubleSetter(fieldInfo);
                    }
                    else if (fieldType == typeof(int))
                    {
                        setter = new IntSetter(fieldInfo);
                    }
                    else if (fieldType == typeof(bool))
                    {
                        setter = new BoolSetter(fieldInfo);
                    }
                    else if (fieldType == typeof(string))
                    {
                        setter = new StringSetter(fieldInfo);
                    }
                    else if (fieldType == typeof(Vector2))
                    {
                        setter = new Vector2Setter(fieldInfo);
                    }
                    else if (fieldType == typeof(Vector3))
                    {
                        setter = new Vector3Setter(fieldInfo);
                    }
                    else if (fieldType == typeof(Vector4))
                    {
                        setter = new Vector4Setter(fieldInfo);
                    }
                    else if (fieldType == typeof(Quaternion))
                    {
                        setter = new QuaternionSetter(fieldInfo);
                    }
                    else if (fieldType.IsEnum)
                    {
                        setter = new EnumSetter(fieldInfo);
                    }
                    else
                    {
                        MethodInfo parseMethod = fieldType.GetMethod("Parse", new[] { typeof(string) });
                        if (parseMethod == null)
                        {
                            setter = null;
                        }
                        else
                        {
                            setter = new AnonymousSetter(fieldInfo, parseMethod);
                        }
                    }
                }

                typeSetterDict[fieldInfo.Name] = setter;
            }
        }

        static bool PartLoader_ApplyPartValue_Prefix(Part part, ConfigNode.Value nodeValue, out bool __result)
        {
            if (!partFieldDictionariesAreBuilt)
            {
                partFieldDictionariesAreBuilt = true;
                BuildPartFieldDictionaries();
            }

            Type partType = part.GetType();
            Dictionary<string, PartFieldSetter> partFieldSetterDict;
            if (partType == typeof(Part))
                partFieldSetterDict = partFieldsSetters;
            else if (partType == typeof(CompoundPart))
                partFieldSetterDict = compoundPartFieldsSetters;
            else if (!unknownPartFieldsSetters.TryGetValue(partType, out partFieldSetterDict))
                throw new Exception($"Unknown part type: '{partType}'");

            string valueName = nodeValue.name;
            if (partFieldSetterDict.TryGetValue(valueName, out PartFieldSetter setter))
            {
                if (setter == null)
                {
                    __result = false;
                    return false;
                }

                __result = setter.SetField(part, nodeValue.value);
                return false;
            }

            __result = false;
            return false;
        }
    }
}
