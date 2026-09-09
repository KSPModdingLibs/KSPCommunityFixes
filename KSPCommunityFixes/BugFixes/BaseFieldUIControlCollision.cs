using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    /// <summary>
    /// <see cref="BaseField.SetupUIControls"/> caches the <see cref="UI_Control"/> attributes declared on a
    /// field in a <c>Dictionary&lt;int, UI_Control[]&gt;</c> keyed by <see cref="FieldInfo.GetHashCode"/>.
    /// A hash code isn't an identity so as soon as two <see cref="FieldInfo"/> instances share one, every
    /// <see cref="BaseField"/> created for the second field silently gets the controls declared on the first
    /// one.<para/>
    /// When the colliding fields have compatible control types this only results in a wrong PAW widget
    /// (wrong slider range, a label instead of a toggle etc), but when they don't, the <see cref="UIPartActionItem"/>
    /// instantiated from the wrong control reads the field value as the wrong type and throws.<para/>
    /// We reimplement the method with a <see cref="FieldInfo"/> keyed cache, which is an actual identity.
    /// </summary>
    internal class BaseFieldUIControlCollision : BasePatch
    {
        private static readonly Dictionary<FieldInfo, UI_Control[]> uiControlsByFieldInfo = new Dictionary<FieldInfo, UI_Control[]>(500);
        private static readonly UI_Control[] noUIControls = new UI_Control[0];

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(BaseField), nameof(BaseField.SetupUIControls));
        }

        private static void BaseField_SetupUIControls_Override(BaseField instance)
        {
            FieldInfo fieldInfo = instance.FieldInfo;
            if (!uiControlsByFieldInfo.TryGetValue(fieldInfo, out UI_Control[] uiControls))
            {
                uiControls = (UI_Control[])fieldInfo.GetCustomAttributes(typeof(UI_Control), false);

                // Fields with no UI_Control attribute share one static empty array instead of caching a per-field empty one
                if (uiControls == null || uiControls.Length == 0)
                    uiControls = noUIControls;

                uiControlsByFieldInfo.Add(fieldInfo, uiControls);
            }

            for (int i = 0; i < uiControls.Length; i++)
            {
                UI_Control uiControl = uiControls[i];

                if ((uiControl.scene & UI_Scene.Flight) != 0)
                {
                    if (instance._uiControlFlight == null)
                        instance._uiControlFlight = instance.SetupUIControl(uiControl);
                    else
                        Debug.LogError($"BaseField {fieldInfo.Name} in {instance.host.GetType().Name} has too many UICtrls marked for Flight. Max == 1. UICtrl scene set to {(int)uiControl.scene}");
                }

                if ((uiControl.scene & UI_Scene.Editor) != 0)
                {
                    if (instance._uiControlEditor == null)
                    {
                        instance._uiControlEditor = instance.SetupUIControl(uiControl);
                        instance.guiActiveEditor = true;
                    }
                    else
                    {
                        Debug.LogError($"BaseField {fieldInfo.Name} in {instance.host.GetType().Name} has too many UICtrls marked for Editor. Max == 1. UICtrl scene set to {(int)uiControl.scene}");
                    }
                }
            }

            if (instance._uiControlEditor == null)
                instance._uiControlEditor = instance.SetupUIControlLabel();

            if (instance._uiControlFlight == null)
                instance._uiControlFlight = instance.SetupUIControlLabel();
        }
    }
}
