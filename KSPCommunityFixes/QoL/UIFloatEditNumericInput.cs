using HarmonyLib;
using KSP.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class UIFloatEditNumericInput : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        private static UIPartActionNumericFloatEdit prefab;

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(UIPartActionController), "Awake"),
                this));
        }

        static void UIPartActionController_Awake_Postfix(UIPartActionController __instance)
        {
            if (prefab == null)
            {
                UIPartActionFloatEdit original = null;
                UIPartActionFloatRange floatRange = null;

                foreach (var uiPartActionFieldItem in __instance.fieldPrefabs)
                {
                    if (uiPartActionFieldItem is UIPartActionFloatEdit)
                        original = (UIPartActionFloatEdit)uiPartActionFieldItem;
                    else if (uiPartActionFieldItem is UIPartActionFloatRange)
                        floatRange = (UIPartActionFloatRange)uiPartActionFieldItem;
                }

                if (original != null && floatRange != null)
                    prefab = UIPartActionNumericFloatEdit.CreatePrefab(original, floatRange);
            }

            if (prefab != null)
            {
                for (int i = 0; i < __instance.fieldPrefabs.Count; i++)
                {
                    if (__instance.fieldPrefabs[i] is UIPartActionFloatEdit)
                    {
                        __instance.fieldPrefabs[i] = prefab;
                        break;
                    }
                }
            }
        }
    }

    [UI_FloatEdit]
    public class UIPartActionNumericFloatEdit : UIPartActionFloatEdit
    {
        public static UIPartActionNumericFloatEdit CreatePrefab(UIPartActionFloatEdit prefabComponent, UIPartActionFloatRange floatRange)
        {
            GameObject customPrefab = Instantiate(prefabComponent.gameObject);
            DontDestroyOnLoad(customPrefab);
            customPrefab.name = "UIPartActionNumericFloatEdit";
            customPrefab.transform.SetParent(UIMasterController.Instance.transform);

            UIPartActionFloatEdit originalComponent = customPrefab.GetComponent<UIPartActionFloatEdit>();
            UIPartActionNumericFloatEdit customComponent = customPrefab.AddComponent<UIPartActionNumericFloatEdit>();

            customComponent.fieldName = originalComponent.fieldName;
            customComponent.fieldValue = originalComponent.fieldValue;
            customComponent.incLarge = originalComponent.incLarge;
            customComponent.incSmall = originalComponent.incSmall;
            customComponent.decLarge = originalComponent.decLarge;
            customComponent.decSmall = originalComponent.decSmall;
            customComponent.slider = originalComponent.slider;

            customComponent.originalObjects = new GameObject[originalComponent.transform.childCount];
            int i = 0;
            foreach (Transform child in originalComponent.transform)
            {
                customComponent.originalObjects[i] = child.gameObject;
                i++;
            }

            customComponent.numericContainer = Instantiate(floatRange.transform.Find("InputFieldHolder").gameObject);
            customComponent.numericContainer.name = "InputFieldHolder";
            customComponent.numericContainer.transform.SetParent(originalComponent.transform);
            customComponent.fieldNameNumeric = customComponent.numericContainer.transform.Find("NameNumeric").GetComponent<TextMeshProUGUI>();
            customComponent.inputField = customComponent.numericContainer.GetComponentInChildren<TMP_InputField>();

            Destroy(originalComponent);

            return customComponent;
        }

        public TextMeshProUGUI fieldNameNumeric;
        public TMP_InputField inputField;
        public GameObject numericContainer;
        public GameObject[] originalObjects;

        public override void Setup(UIPartActionWindow window, Part part, PartModule partModule, UI_Scene scene, UI_Control control, BaseField field)
        {
            base.Setup(window, part, partModule, scene, control, field);

            fieldNameNumeric.text = field.guiName;
            inputField.onEndEdit.AddListener(OnFieldInput);
            inputField.onSelect.AddListener(AddInputFieldLock);
            GameEvents.onPartActionNumericSlider.Add(ToggleNumericSlider);
            ToggleNumericSlider(GameSettings.PAW_NUMERIC_SLIDERS);
            window.usingNumericValue = true;
        }

        private void OnDestroy()
        {
            GameEvents.onPartActionNumericSlider.Remove(ToggleNumericSlider);
        }

        private void ToggleNumericSlider(bool numeric)
        {
            foreach (GameObject originalObject in originalObjects)
            {
                originalObject.SetActive(!numeric);
            }

            numericContainer.SetActive(numeric);
            fieldNameNumeric.gameObject.SetActive(numeric);

            if (numeric)
            {
                inputField.text = GetFieldValue().ToString($"F{floatControl.sigFigs}");
            }
            else
            {
                OnFieldInput(inputField.text);
                float value = GetFieldValue();
                string unit = floatControl.unit;
                int sigFigs = floatControl.sigFigs;
                string text = ((!floatControl.useSI) ? (KSPUtil.LocalizeNumber(value, "F" + sigFigs) + unit) : KSPUtil.PrintSI(value, unit, sigFigs));
                fieldValue.text = text;

                float min, max;
                if (floatControl.incrementLarge == 0f)
                {
                    min = floatControl.minValue;
                    max = floatControl.maxValue;
                }
                else if (floatControl.incrementSmall == 0f)
                {
                    min = IntervalBase(value, floatControl.incrementLarge);
                    max = min + floatControl.incrementLarge;
                }
                else
                {
                    min = IntervalBase(value, floatControl.incrementSmall);
                    max = min + floatControl.incrementSmall;
                }
                slider.minValue = Mathf.Max(min, floatControl.minValue);
                slider.maxValue = Mathf.Min(max, floatControl.maxValue);
                slider.value = value;
            }
        }

        private float IntervalBase(float value, float increment)
        {
            float num = Mathf.Floor((value + floatControl.incrementSlide / 2f) / increment) * increment;
            if (num > floatControl.maxValue - increment)
                num = floatControl.maxValue - increment;

            return num;
        }

        private void OnFieldInput(string input)
        {
            if (float.TryParse(input, out float result))
            {
                if (floatControl.incrementSlide > 0f)
                    result = Mathf.Ceil(result / floatControl.incrementSlide) * floatControl.incrementSlide;

                result = Mathf.Clamp(result, floatControl.minValue, floatControl.maxValue);
                SetFieldValue(result);
                inputField.text = result.ToString($"F{floatControl.sigFigs}");
            }
            RemoveInputfieldLock();
        }

        private float GetFieldValue()
        {
            return field.GetValue<float>(field.host);
        }
    }
}
