using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

/*
The purpose of this patch if to allow BaseField and associated features (PAW controls, persistence, etc) to work when
a custom BaseField is added to a BaseFieldList (ie, a Part or PartModule) with a host instance other than the BaseFieldList
owner. This allow to dynamically add fields defined in another class to a Part or PartModule and to benefit from all the 
associated KSP sugar :
- PAW UI controls 
- Value and symmetry events
- Automatic persistence on the Part/PartModule hosting the BaseFieldList

The whole thing seems actually designed with such a scenario in mind, but for some reason some BaseField and BaseFieldList 
methods are using the BaseFieldList.host instance instead of the BaseField.host instance (as for why BaseFieldList has a 
"host" at all, I've no idea and this seems to be a design oversight). There is little to no consistency in which host 
reference is used, they are even sometimes mixed in the same method. For example, BaseFieldList.Load() uses BaseFieldList.host 
in its main body, then calls BaseFieldList.SetOriginalValue() which is relying on BaseField.host.

Changing every place where a `host` reference is acquired to ensure the BaseField.host reference is used allow to use a custom
host instance, and shouldn't result in any behavior change. This being said, the stock code can theoretically allow a plugin 
to instantiate a custom BaseField with a null host and have it kinda functional if that field is only used to SetValue() / 
Getvalue() and as long as the field isn't persistent and doesn't have any associated UI_Control. This feels like an extremly
improbable scenario, so this is probably fine.
*/

namespace KSPCommunityFixes.Modding
{
    [PatchPriority(Order = 0)]
    class BaseFieldListUseFieldHost : BasePatch
    {
        private static AccessTools.FieldRef<object, Callback<object>> BaseField_OnValueModified_FieldRef;

        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {

            BaseField_OnValueModified_FieldRef = AccessTools.FieldRefAccess<Callback<object>>(typeof(BaseField<KSPField>), nameof(BaseField<KSPField>.OnValueModified));
            if (BaseField_OnValueModified_FieldRef == null)
                throw new MissingFieldException($"BaseFieldListUseFieldHost patch could not find the BaseField.OnValueModified event backing field");

            MethodInfo BaseField_GetValue_MethodInfo = null;
            foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(typeof(BaseField<KSPField>)))
            {
                if (declaredMethod.Name == nameof(BaseField<KSPField>.GetValue) && !declaredMethod.IsGenericMethod)
                {
                    BaseField_GetValue_MethodInfo = declaredMethod;
                    break;
                }
            }
            if (BaseField_GetValue_MethodInfo == null)
                throw new MissingMethodException($"BaseFieldListUseFieldHost patch could not find the BaseField.GetValue() method");

            // Note : fortunately, we don't need to patch the generic BaseField.GetValue<T>(object host) method because it calls
            // the non-generic GetValue method
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                BaseField_GetValue_MethodInfo,
                this, nameof(BaseField_GetValue_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseField<KSPField>), nameof(BaseField<KSPField>.SetValue)),
                this, nameof(BaseField_SetValue_Prefix)));


            // BaseField.Read() is a public method called from :
            // - BaseFieldList.Load()
            // - BaseFieldList.ReadValue() (2 overloads)
            // The method is really tiny so there is a potential inlining risk (doesn't happen in my tests, but this stuff can be platform
            // dependent). It's only really critical to have BaseFieldList.Load() being patched, the ReadValue() methods are unused in the
            // stock codebase, and it is doubtfull anybody would ever call them. 
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseField), nameof(BaseField.Read)),
                this));

            // We also patch BaseFieldList.Load() because :
            // - Of the above mentioned inlining risk
            // - Because it pass the (arguably wrong) host reference to the UI_Control.Load() method, and even though none
            //   of the various overloads make use of that argument we might want to be consistent. This being said, in the context
            //   of a UI_Control, it might be expected that the "host" should be the host of the control (ie, a Part or PartModule),
            //   and not the host of the backing BaseField. In the end, it is doubtful anybody as ever relied on this unused-in-stock
            //   unclear-what-this-is stuff. Not to mention I'm not even sure anybody has ever defined a custom UI_Control (B9PS maybe ?),
            //   since this isn't something that can be done out of the box without some major hacking around.
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseFieldList), nameof(BaseFieldList.Load)),
                this));
        }

        static bool BaseField_GetValue_Prefix(BaseField<KSPField> __instance, out object __result)
        {
            try
            {
                // uses the field host reference instead of the reference passed as a parameter
                // in case the field host reference is null, let the call throw and call the original method
                // In theory, this should never happen unless a plugin is doing very sketchy like instantiating
                // a BaseField manually with a null host.
                __result = __instance._fieldInfo.GetValue(__instance._host);
                return false;
            }
            catch
            {
                __result = null;
                return true;
            }
        }

        static bool BaseField_SetValue_Prefix(BaseField<KSPField> __instance, object newValue, out bool __result)
        {
            try
            {
                __instance._fieldInfo.SetValue(__instance._host, newValue);

                // Note : since BaseField.OnValueModified is a "field-like event", it is relying on a compiler-generated
                // private backing field, and the public event "synctatic suger" member can only be invoked from the declaring
                // class, so we can't directly call "__instance.OnValueModified()" here. Additionally, the compiler has the extremly
                // bad taste to name the backing field "OnValueModified" too, resulting in an "ambiguous reference" if we try to use
                // it through the publicized assembly. So we have to resort to creating a FieldRef open delegate for that backing field.
                BaseField_OnValueModified_FieldRef(__instance)?.Invoke(newValue);
                __result = true;
                return false;
            }
            catch
            {
                __result = false;
                return true;
            }
        }

        static bool BaseField_Read_Prefix(BaseField __instance, string value)
        {
            BaseField.ReadPvt(__instance._fieldInfo, value, __instance._host);
            return false;
        }

        static bool BaseFieldList_Load_Prefix(BaseFieldList __instance, ConfigNode node)
        {
            for (int i = 0; i < node.values.Count; i++)
            {
                ConfigNode.Value value = node.values[i];
                BaseField baseField = __instance[value.name];
                if (baseField == null || baseField.hasInterface || baseField.uiControlOnly)
                {
                    continue;
                }

                // The original code calls BaseField.Read() here. We bypass it to avoid
                // any inlining risk and call directly the underlying static method.
                BaseField.ReadPvt(baseField._fieldInfo, value.value, baseField._host);

                if (baseField.uiControlFlight.GetType() != typeof(UI_Label))
                {
                    ConfigNode node2 = node.GetNode(value.name + "_UIFlight");
                    if (node2 != null)
                    {
                        baseField.uiControlFlight.Load(node2, baseField._host);
                    }
                }
                else if (baseField.uiControlEditor.GetType() != typeof(UI_Label))
                {
                    ConfigNode node3 = node.GetNode(value.name + "_UIEditor");
                    if (node3 != null)
                    {
                        baseField.uiControlEditor.Load(node3, baseField._host);
                    }
                }
            }
            for (int j = 0; j < node.nodes.Count; j++)
            {
                ConfigNode configNode = node.nodes[j];
                BaseField baseField2 = __instance[configNode.name];
                if (baseField2 == null || !baseField2.hasInterface || baseField2.uiControlOnly)
                {
                    continue;
                }

                object value2 = baseField2.GetValue(baseField2._host);
                if (value2 == null)
                {
                    continue;
                }
                (value2 as IConfigNode)?.Load(configNode);
                if (baseField2.uiControlFlight.GetType() != typeof(UI_Label))
                {
                    ConfigNode node4 = node.GetNode(configNode.name + "_UIFlight");
                    if (node4 != null)
                    {
                        baseField2.uiControlFlight.Load(node4, baseField2._host);
                    }
                }
                else if (baseField2.uiControlEditor.GetType() != typeof(UI_Label))
                {
                    ConfigNode node5 = node.GetNode(configNode.name + "_UIEditor");
                    if (node5 != null)
                    {
                        baseField2.uiControlEditor.Load(node5, baseField2._host);
                    }
                }
            }
            __instance.SetOriginalValue();
            return false;
        }


    }
}
