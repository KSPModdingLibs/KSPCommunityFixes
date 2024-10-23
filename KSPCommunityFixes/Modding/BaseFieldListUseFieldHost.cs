using HarmonyLib;
using MonoMod.Utils;
using System;
using System.Collections.Generic;

/*
The purpose of this patch if to allow BaseField and associated features (PAW controls, persistence, etc) to work when
a custom BaseField is added to a BaseFieldList (ie, a Part or PartModule) with a host instance other than the BaseFieldList
owner. This allow to dynamically add fields defined in another class to a Part or PartModule and to benefit from all the 
associated KSP sugar :
- PAW UI controls 
- Value and symmetry events
- Automatic persistence on the Part/PartModule hosting the BaseFieldList

Potential use cases for this are either :
- Having a part or module-level PAW item associated to and sharing the state of a common field, for example a field in a KSPAddon.
- Extending external (typically stock) modules with additional PAW UI controls and/or persisted fields.

The whole thing seems actually designed with such a scenario in mind, but for some reason some BaseField and BaseFieldList 
methods are using the BaseFieldList.host instance instead of the BaseField.host instance (as for why BaseFieldList has a 
"host" at all, I've no idea and this seems to be a design oversight). There is little to no consistency in which host 
reference is used, they are even sometimes mixed in the same method. For example, BaseFieldList.Load() uses BaseFieldList.host 
in its main body, then calls BaseFieldList.SetOriginalValue() which is relying on BaseField.host.

Changing every place where a `host` reference is acquired to ensure the BaseField.host reference is used allow to use a custom
host instance, and shouldn't result in any behavior change. This being said, the stock code can technically allow a plugin 
to instantiate a custom BaseField with a null host and have it kinda functional if that field is only used to SetValue() / 
Getvalue(), as long as the field isn't persistent and doesn't have any associated UI_Control, so we implement a fallback
to the stock behavior in this case.
*/

namespace KSPCommunityFixes.Modding
{
    [PatchPriority(Order = 0)]
    class BaseFieldListUseFieldHost : BasePatch
    {
        private static AccessTools.FieldRef<object, Callback<object>> BaseField_OnValueModified_FieldRef;

        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            BaseField_OnValueModified_FieldRef = AccessTools.FieldRefAccess<Callback<object>>(typeof(BaseField<KSPField>), nameof(BaseField<KSPField>.OnValueModified));
            if (BaseField_OnValueModified_FieldRef == null)
                throw new MissingFieldException($"BaseFieldListUseFieldHost patch could not find the BaseField.OnValueModified event backing field");

            // Note : fortunately, we don't need to patch the generic BaseField.GetValue<T>(object host) method because it calls
            // the non-generic GetValue method
            AddPatch(PatchType.Override, AccessTools.FirstMethod(typeof(BaseField<KSPField>), (m) => m.Name == nameof(BaseField<KSPField>.GetValue) && !m.IsGenericMethod), nameof(BaseField_GetValue_Override));

            AddPatch(PatchType.Override, typeof(BaseField<KSPField>), nameof(BaseField<KSPField>.SetValue), nameof(BaseField_SetValue_Override));

            // BaseField.Read() is a public method called from :
            // - BaseFieldList.Load()
            // - BaseFieldList.ReadValue() (2 overloads)
            // The method is really tiny so there is a potential inlining risk (doesn't happen in my tests, but this stuff can be platform
            // dependent). It's only really critical to have BaseFieldList.Load() being patched, the ReadValue() methods are unused in the
            // stock codebase, and it is doubtfull anybody would ever call them. 
            AddPatch(PatchType.Override, typeof(BaseField), nameof(BaseField.Read));

            // We also patch BaseFieldList.Load() because :
            // - Of the above mentioned inlining risk
            // - Because it pass the (arguably wrong) host reference to the UI_Control.Load() method, and even though none
            //   of the various overloads make use of that argument we might want to be consistent.
            AddPatch(PatchType.Override, typeof(BaseFieldList), nameof(BaseFieldList.Load));
        }

        static object BaseField_GetValue_Override(BaseField<KSPField> instance, object host)
        {
            try
            {
                // In case the field host is null, use the parameter
                if (ReferenceEquals(instance._host, null))
                    return instance._fieldInfo.GetValue(host);

                // Uses the field host reference instead of the reference passed as a parameter
                return instance._fieldInfo.GetValue(instance._host);
            }
            catch
            {
                PDebug.Error("Value could not be retrieved from field '" + instance._name + "'");
                return null;
            }
        }

        static bool BaseField_SetValue_Override(BaseField<KSPField> instance, object newValue, object host)
        {
            try
            {
                // In case the field host is null, use the parameter
                if (ReferenceEquals(instance._host, null))
                    instance._fieldInfo.SetValue(host, newValue);

                // Uses the field host reference instead of the reference passed as a parameter
                instance._fieldInfo.SetValue(instance._host, newValue);

                // Note : since BaseField.OnValueModified is a "field-like event", it is relying on a compiler-generated
                // private backing field, and the public event "syntactic sugar" member can only be invoked from the declaring
                // class, so we can't directly call "__instance.OnValueModified()" here. Additionally, the compiler has the extremly
                // bad taste to name the backing field "OnValueModified" too, resulting in an ambiguous reference if we try to use
                // it through the publicized assembly. So we have to resort to creating a FieldRef open delegate for that backing field.
                BaseField_OnValueModified_FieldRef(instance).Invoke(newValue);
                return true;
            }
            catch (Exception ex)
            {
                PDebug.Error(string.Concat("Value '", newValue, "' could not be set to field '", instance._name, "'"));
                PDebug.Error(ex.Message + "\n" + ex.StackTrace + "\n" + ex.Data);
                return false;
            }
        }

        static void BaseField_Read_Override(BaseField instance, string value, object host)
        {
            if (ReferenceEquals(instance._host, null))
                BaseField.ReadPvt(instance._fieldInfo, value, host);

            BaseField.ReadPvt(instance._fieldInfo, value, instance._host);
        }

        static void BaseFieldList_Load_Override(BaseFieldList instance, ConfigNode node)
        {
            for (int i = 0; i < node.values.Count; i++)
            {
                ConfigNode.Value value = node.values[i];
                BaseField baseField = instance[value.name];
                if (baseField == null || baseField.hasInterface || baseField.uiControlOnly)
                    continue;

                object host = ReferenceEquals(baseField._host, null) ? instance.host : baseField._host;

                // The original code calls BaseField.Read() here. We bypass it to avoid
                // any inlining risk and call directly the underlying static method.
                BaseField.ReadPvt(baseField._fieldInfo, value.value, host);

                if (baseField.uiControlFlight.GetType() != typeof(UI_Label))
                {
                    ConfigNode controlNode = node.GetNode(value.name + "_UIFlight");
                    if (controlNode != null)
                        baseField.uiControlFlight.Load(controlNode, host);
                }
                else if (baseField.uiControlEditor.GetType() != typeof(UI_Label))
                {
                    ConfigNode controlNode = node.GetNode(value.name + "_UIEditor");
                    if (controlNode != null)
                        baseField.uiControlEditor.Load(controlNode, host);
                }
            }
            for (int j = 0; j < node.nodes.Count; j++)
            {
                ConfigNode configNode = node.nodes[j];
                BaseField baseField = instance[configNode.name];
                if (baseField == null || !baseField.hasInterface || baseField.uiControlOnly)
                    continue;

                object host = ReferenceEquals(baseField._host, null) ? instance.host : baseField._host;

                object value = baseField.GetValue(host);
                if (value == null)
                    continue;

                (value as IConfigNode)?.Load(configNode);

                if (baseField.uiControlFlight.GetType() != typeof(UI_Label))
                {
                    ConfigNode controlNode = node.GetNode(configNode.name + "_UIFlight");
                    if (controlNode != null)
                        baseField.uiControlFlight.Load(controlNode, host);
                }
                else if (baseField.uiControlEditor.GetType() != typeof(UI_Label))
                {
                    ConfigNode controlNode = node.GetNode(configNode.name + "_UIEditor");
                    if (controlNode != null)
                        baseField.uiControlEditor.Load(controlNode, host);
                }
            }
            instance.SetOriginalValue();
        }
    }

    // This is an example module we want to add fields to.
    // Here we use the most failsafe and recommended pattern, which is to create and destroy an extension object at the 
    // earliest and latest possible moment in the module lifecycle.
    // In a real use case, this would have to be accomplished by harmony-patching the relevant methods.
    // It's technically possible to use other patterns, but tracking parts/modules lifetime externally is full of difficult 
    // to handle corner-case scenarios, and is likely to cause way more overhead, I really advise against attempting it.

    // This mean that adding BaseFields to external modules is only achievable if the methods exist to be patched :
    // - A post-instantiation method, ideally OnAwake(), but OnStart()/Start()/OnStartFinished() can work too.
    // - OnDestroy(), so the object associated with the external module can be untracked and thus garbage collected
    //   Without that, the pattern would result in a memory leak. In the absence of an OnDestroy() method, it's possible
    //   to clean all extensions on scene switches, which still result in a leak, but a temporary one. If you have to
    //   resort to this, I would advise to consider other options for achieving equivalent functionality, either using a
    //   new, separate module, or (but that isn't very recommende deither) putting the functionality in a derived module
    //   and MM-swapping it.

    // Do note that if you want to define a [KSPField(isPersistant = true)] field and benefit from the built-in persistence,
    // your custom BaseField must be added to the target module before PartModule.Load() is called, so the only option is
    // to add the BaseField from OnAwake().
    public class CustomFieldDemoModule : PartModule
    {
        public override void OnAwake()
        {
            CustomFieldDemoModuleExtension.Instantiate(this);
        }

        private void OnDestroy()
        {
            CustomFieldDemoModuleExtension.Destroy(this);
        }
    }

    public class CustomFieldDemoModuleExtension
    {
        private static Dictionary<int, CustomFieldDemoModuleExtension> extensions = new Dictionary<int, CustomFieldDemoModuleExtension>();

        public static void Instantiate(CustomFieldDemoModule target)
        {
            // Using the instance ID for the dictionary keys is slightly faster than using the instance itself.
            int targetID = target.GetInstanceID();

            // Using TryAdd is not necessary if you can guarantee that Instantiate() will never be called more than once for the same
            // module, which is a relatively safe assumption if you call it from the module OnAwake() or constructor, and a very unsafe
            // one in pretty much every other case (Start, OnStart...)
            extensions.TryAdd(targetID, new CustomFieldDemoModuleExtension(target));
        }

        public static void Destroy(CustomFieldDemoModule target)
        {
            extensions.Remove(target.GetInstanceID());
        }

        public static CustomFieldDemoModuleExtension Get(CustomFieldDemoModule module)
        {
            if (extensions.TryGetValue(module.GetInstanceID(), out CustomFieldDemoModuleExtension extension))
                return extension;

            return null;
        }

        private CustomFieldDemoModuleExtension(CustomFieldDemoModule target)
        {

        }
    }
}