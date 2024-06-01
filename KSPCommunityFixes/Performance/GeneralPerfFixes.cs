using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using KSP.UI.Screens;
using KSP.UI.Screens.Settings;
using KSPCommunityFixes.Library;
using TMPro.Examples;
using UnityEngine;
using UnityEngine.UIElements;
using VehiclePhysics;
using static System.Number;
using static GameDatabase;
using static Highlighting.Highlighter.RendererCache;
using static iT;
using static ProceduralSpaceObject;
using UObject = UnityEngine.Object;

namespace KSPCommunityFixes.Performance
{
    internal class GeneralPerfFixes : BasePatch
    {
        internal static Stopwatch watch = new Stopwatch();

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.PropertyGetter(typeof(FlightGlobals), nameof(FlightGlobals.fetch)),
                this, nameof(FlightGlobals_fetch_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartLoader), nameof(PartLoader.CreatePartIcon)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartLoader), nameof(PartLoader.ApplyPartValue)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseFieldList), nameof(BaseFieldList.CreateList)),
                this));

            //patches.Add(new PatchInfo(
            //    PatchMethodType.Postfix,
            //    AccessTools.Method(typeof(PartLoader), nameof(PartLoader.CreatePartIcon)),
            //    this));

            //patches.Add(new PatchInfo(
            //    PatchMethodType.Prefix,
            //    AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.GetModelPrefab)),
            //    this));

            //// texture getting patches would also benefit to model loading, but we need to run earlier...
            //patches.Add(new PatchInfo(
            //    PatchMethodType.Prefix,
            //    AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.GetTextureInfo)),
            //    this));

            //patches.Add(new PatchInfo(
            //    PatchMethodType.Prefix,
            //    AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.GetTexture)),
            //    this));

            //patches.Add(new PatchInfo(
            //    PatchMethodType.Prefix,
            //    AccessTools.Method(typeof(PartModule), nameof(PartModule.ModularSetup)),
            //    this));

            //patches.Add(new PatchInfo(
            //    PatchMethodType.Postfix,
            //    AccessTools.Method(typeof(PartModule), nameof(PartModule.ModularSetup)),
            //    this));
        }

        static bool FlightGlobals_fetch_Prefix(out FlightGlobals __result)
        {
            if (KSPCFFastLoader.PartCompilationInProgress)
            {
                __result = null;
                return false;
            }

            if (FlightGlobals._fetch.IsNullOrDestroyed())
            {
                FlightGlobals._fetch = UObject.FindObjectOfType<FlightGlobals>();
            }

            __result = FlightGlobals._fetch;
            return false;
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

        //static void PartLoader_CreatePartIcon_Prefix()
        //{
        //    watch.Start();
        //}

        //static void PartLoader_CreatePartIcon_Postfix()
        //{
        //    watch.Stop();
        //}

        private static HashSet<Type> OnIconCreatePartModules;

        private static bool HasOnIconCreateModule(Part part)
        {
            if (OnIconCreatePartModules == null)
            {
                OnIconCreatePartModules = new HashSet<Type>();

                foreach (Type type in AccessTools.AllTypes())
                {
                    if (type.IsSubclassOf(typeof(PartModule)))
                    {
                        if (type.GetMethod(nameof(PartModule.OnIconCreate), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly) != null)
                        {
                            OnIconCreatePartModules.Add(type);
                        }
                    }
                }
            }

            if (OnIconCreatePartModules.Count == 0)
                return false;

            List<PartModule> modules = part.modules.modules;
            for (int i = modules.Count; i-- > 0;)
            {
                if (OnIconCreatePartModules.Contains(modules[i].GetType()))
                {
                    return true;
                }
            }

            return false;
        }

        static bool PartLoader_CreatePartIcon_Prefix(GameObject newPart, out float iconScale, out GameObject __result)
        {
            watch.Start();
            newPart.SetActive(false);
            GameObject partObject = UObject.Instantiate(newPart);
            newPart.SetActive(true);
            //partObject.SetActive(true);
            Part part = partObject.GetComponent<Part>();

            // only activate the part if a module requires it.
            // probably not a good idea in the end :
            // - some model setup might happen in Awake(), and we would miss those
            // - the perf gains are not as good as I hoped.
            if (HasOnIconCreateModule(part))
            {
                partObject.SetActive(true);
            }

            if (part.IsNotNullOrDestroyed())
            {
                int i = 0;
                for (int count = part.Modules.Count; i < count; i++)
                    part.Modules[i].OnIconCreate();
            }

            Bounds partBounds = default;
            partObject.GetComponentsInChildren(false, componentBuffer);
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

                partObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

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
                                Mathf.Clamp(originalColor.r, 0.5f, 1f), 
                                Mathf.Clamp(originalColor.g, 0.5f, 1f), 
                                Mathf.Clamp(originalColor.b, 0.5f, 1f)));
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

            GameObject iconObject = new GameObject();
            iconObject.name = partObject.name + " icon";
            partObject.transform.parent = iconObject.transform;
            partObject.transform.localScale = Vector3.one * iconScale;
            partObject.transform.localPosition = partBounds.center * (0f - iconScale);
            iconObject.transform.parent = PartLoader.Instance.transform;
            iconObject.SetActive(false);
            partObject.SetActive(true);

            __result = iconObject;
            watch.Stop();
            return false;
        }

        private static Dictionary<string, PartFieldSetter> partFieldsSetters = new Dictionary<string, PartFieldSetter>();
        private static Dictionary<string, PartFieldSetter> compoundPartFieldsSetters = new Dictionary<string, PartFieldSetter>();
        private static Dictionary<Type, Dictionary<string, PartFieldSetter>> derivedPartSetters;

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

        public static Action<object, T> CreateInstanceSetter<T>(FieldInfo field)
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

        public static Action<object, object> CreateInstanceBoxedValueSetter(FieldInfo field)
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

        public static void BuildPartFieldDictionaries()
        {
            Type partType = typeof(Part);
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

                partFieldsSetters[fieldInfo.Name] = setter;
            }


            //foreach (Type type in AccessTools.AllTypes())
            //{
            //    if (type.IsSubclassOf(Part))
            //    {
                    
            //    }
            //}
        }

        static bool PartLoader_ApplyPartValue_Prefix(Part part, ConfigNode.Value nodeValue, out bool __result)
        {
            if (!partFieldDictionariesAreBuilt)
            {
                partFieldDictionariesAreBuilt = true;
                BuildPartFieldDictionaries();
            }

            string valueName = nodeValue.name;
            if (partFieldsSetters.TryGetValue(valueName, out PartFieldSetter setter))
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

        static unsafe Vector2 ParseVector2(string value)
        {
            UnsafeString str = new UnsafeString(value);
            for (int i = str.length; i-- > 0;)
            {
                if (str[i] == ',')
                {
                    UnsafeString x = str.Substring(0, i);
                    UnsafeString y = str.Substring(i + 1);
                    Vector2 result = new Vector2(ParseSingle(x), ParseSingle(y));
                    str.UnPin();
                    return result;
                }
            }

            InvalidFormatException();
            return default;
        }

        private static void InvalidFormatException()
        {
            throw new FormatException();
        }

        internal static NumberFormatInfo numberFormatInfo = NumberFormatInfo.CurrentInfo;
        internal static NumberStyles numberStyleFloat = NumberStyles.Float | NumberStyles.AllowThousands;

        internal static unsafe float ParseSingle(UnsafeString str)
        {
            byte* stackBuffer = stackalloc byte[(int)(uint)NumberBuffer.NumberBufferBytes];
            NumberBuffer number = new NumberBuffer(stackBuffer);
            double value2 = 0.0;

            if (!TryStringToNumber(str, numberStyleFloat, ref number, numberFormatInfo, parseDecimal: false))
            {
                UnsafeString trimmedStr = str.Trim();
                if (trimmedStr.Equals(numberFormatInfo.PositiveInfinitySymbol))
                {
                    return float.PositiveInfinity;
                }
                if (trimmedStr.Equals(numberFormatInfo.NegativeInfinitySymbol))
                {
                    return float.NegativeInfinity;
                }
                if (trimmedStr.Equals(numberFormatInfo.NaNSymbol))
                {
                    return float.NaN;
                }
                throw new FormatException("Input string was not in a correct format.");
            }
            if (!NumberBufferToDouble(number.PackForNative(), ref value2))
            {
                throw new OverflowException("Value was either too large or too small for a Single.");
            }
            float num = (float)value2;
            if (float.IsInfinity(num))
            {
                throw new OverflowException("Value was either too large or too small for a Single.");
            }
            return num;
        }

        internal static unsafe bool TryStringToNumber(UnsafeString str, NumberStyles options, ref NumberBuffer number, NumberFormatInfo numfmt, bool parseDecimal)
        {
            char* ptr = str.ptr;
            if (!ParseNumber(ref ptr, options, ref number, null, numfmt, parseDecimal) || (ptr - str.ptr < str.length && !TrailingZeros(str, (int)(ptr - str.ptr))))
                return false;
            
            return true;
        }

        private static bool TrailingZeros(UnsafeString str, int index)
        {
            for (int i = index; i < str.length; i++)
                if (str[i] != 0)
                    return false;

            return true;
        }

        static bool BaseFieldList_CreateList_Prefix(BaseFieldList __instance, object instance)
        {
            // base code from BaseFieldList<BaseField, KSPField>
            BaseFieldList<BaseField, KSPField>.ReflectedData reflectedAttributes = BaseFieldList<BaseField, KSPField>.GetReflectedAttributes(instance.GetType(), __instance.ignoreUIControlWhenCreatingReflectedData);

            __instance._fields.Capacity = reflectedAttributes.fields.Count;
            int i = 0;
            for (int count = reflectedAttributes.fields.Count; i < count; i++)
            {
                BaseField val = new BaseField(reflectedAttributes.fieldAttributes[i], (FieldInfo)reflectedAttributes.fields[i], instance);
                val.SetOriginalValue();
                __instance._fields.Add(val);
            }

            // override from BaseFieldList : BaseFieldList<BaseField, KSPField>
            // ReflectedData reflectedAttributes = BaseFieldList<BaseField, KSPField>.GetReflectedAttributes(instance.GetType(), __instance.ignoreUIControlWhenCreatingReflectedData);
            int j = 0;
            for (int count = reflectedAttributes.controls.Count; j < count; j++)
            {
                BaseField item = new BaseField(reflectedAttributes.controlAttributes[j], (FieldInfo)reflectedAttributes.fields[j], instance);
                __instance._fields.Add(item);
            }

            return false;
        }
    }

    public struct UnsafeString
    {
        public unsafe char* ptr;
        public int length;
        private GCHandle handle;

        private unsafe UnsafeString(char* ptr, int length, GCHandle handle)
        {
            this.ptr = ptr;
            this.length = length;
            this.handle = handle;
        }

        public unsafe UnsafeString(string str)
        {
            handle = GCHandle.Alloc(str, GCHandleType.Pinned);
            ptr = (char*)handle.AddrOfPinnedObject();
            length = str.Length;
        }

        public unsafe UnsafeString(string str, int start, int length = 0)
        {
            handle = GCHandle.Alloc(str, GCHandleType.Pinned);
            ptr = (char*)handle.AddrOfPinnedObject() + start;
            this.length = length == 0 ? str.Length - start : length;
        }

        public static unsafe UnsafeString Empty => new UnsafeString((char*)0, 0, new GCHandle());

        public unsafe char this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if DEBUG
                if (index < 0 || index >= length)
                    throw new IndexOutOfRangeException();
#endif
                return ptr[index];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe UnsafeString Substring(int start, int length = 0)
        {
            if (start + length > length)
                ArgumentOutOfRange();

            return new UnsafeString(
                ptr + start,
                length == 0 ? this.length - start : length,
                handle);
        }

        private static void ArgumentOutOfRange()
        {
            throw new ArgumentOutOfRangeException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe UnsafeString Trim()
        {
            int firstNonEmptyChar = 0;
            while (firstNonEmptyChar < length && char.IsWhiteSpace(ptr[firstNonEmptyChar]))
                firstNonEmptyChar++;

            if (firstNonEmptyChar == length)
                return Empty;

            int lastNonEmptyChar = length - 1;
            while (char.IsWhiteSpace(ptr[lastNonEmptyChar]))
                lastNonEmptyChar--;

            return Substring(firstNonEmptyChar, lastNonEmptyChar - firstNonEmptyChar + 1);
        }

        public void UnPin()
        {
            if (handle.IsAllocated)
                handle.Free();
        }

        public override unsafe string ToString()
        {
            return new string(ptr, 0, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool Equals(UnsafeString value)
        {
            if (ptr == value.ptr && length == value.length)
                return true;

            if (length != value.length)
                return false;

            return Equals(this, value);
        }

        public static unsafe bool Equals(UnsafeString value1, UnsafeString value2)
        {
            int num = value1.length;
            char* ptr1 = value1.ptr;
            char* ptr2 = value2.ptr;

            if (Environment.Is64BitProcess)
            {
                while (num >= 12)
                {
                    if (*(long*)ptr1 != *(long*)ptr2)
                    {
                        return false;
                    }
                    if (*(long*)(ptr1 + 4) != *(long*)(ptr2 + 4))
                    {
                        return false;
                    }
                    if (*(long*)(ptr1 + 8) != *(long*)(ptr2 + 8))
                    {
                        return false;
                    }
                    ptr1 += 12;
                    ptr2 += 12;
                    num -= 12;
                }
            }
            else
            {
                while (num >= 10)
                {
                    if (*(int*)ptr1 != *(int*)ptr2)
                    {
                        return false;
                    }
                    if (*(int*)(ptr1 + 2) != *(int*)(ptr2 + 2))
                    {
                        return false;
                    }
                    if (*(int*)(ptr1 + 4) != *(int*)(ptr2 + 4))
                    {
                        return false;
                    }
                    if (*(int*)(ptr1 + 6) != *(int*)(ptr2 + 6))
                    {
                        return false;
                    }
                    if (*(int*)(ptr1 + 8) != *(int*)(ptr2 + 8))
                    {
                        return false;
                    }
                    ptr1 += 10;
                    ptr2 += 10;
                    num -= 10;
                }
            }
            while (num > 0 && *(int*)ptr1 == *(int*)ptr2)
            {
                ptr1 += 2;
                ptr2 += 2;
                num -= 2;
            }
            return num <= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool Equals(string value)
        {
            fixed (char* strPtr = &value.m_firstChar)
                if (ptr == strPtr && length == value.Length)
                    return true;

            if (length != value.Length)
                return false;

            return Equals(this, value);
        }

        public static unsafe bool Equals(UnsafeString value1, string value2)
        {
            int num = value1.length;
            char* ptr1 = value1.ptr;
            fixed (char* strPtr = &value2.m_firstChar)
            {
                char* ptr2 = strPtr;
                if (Environment.Is64BitProcess)
                {
                    while (num >= 12)
                    {
                        if (*(long*)ptr1 != *(long*)ptr2)
                        {
                            return false;
                        }
                        if (*(long*)(ptr1 + 4) != *(long*)(ptr2 + 4))
                        {
                            return false;
                        }
                        if (*(long*)(ptr1 + 8) != *(long*)(ptr2 + 8))
                        {
                            return false;
                        }
                        ptr1 += 12;
                        ptr2 += 12;
                        num -= 12;
                    }
                }
                else
                {
                    while (num >= 10)
                    {
                        if (*(int*)ptr1 != *(int*)ptr2)
                        {
                            return false;
                        }
                        if (*(int*)(ptr1 + 2) != *(int*)(ptr2 + 2))
                        {
                            return false;
                        }
                        if (*(int*)(ptr1 + 4) != *(int*)(ptr2 + 4))
                        {
                            return false;
                        }
                        if (*(int*)(ptr1 + 6) != *(int*)(ptr2 + 6))
                        {
                            return false;
                        }
                        if (*(int*)(ptr1 + 8) != *(int*)(ptr2 + 8))
                        {
                            return false;
                        }
                        ptr1 += 10;
                        ptr2 += 10;
                        num -= 10;
                    }
                }
                while (num > 0 && *(int*)ptr1 == *(int*)ptr2)
                {
                    ptr1 += 2;
                    ptr2 += 2;
                    num -= 2;
                }
                return num <= 0;
            }


        }
    }
}
