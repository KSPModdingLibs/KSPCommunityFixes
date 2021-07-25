using System.Collections.Generic;
using HarmonyLib;
using KSP.Localization;
using KSP.UI.Screens;
using KSP.UI.Screens.Flight;
using UnityEngine;

namespace KSPCommunityFixes.UI
{
    class AltimeterHorizontalPosition : BasePatch
    {
        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(FlightUIModeController), "Start"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(GameplaySettingsScreen), "DrawMiniSettings"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(GameplaySettingsScreen), "ApplySettings"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(MapView), "enterMapView"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(MapView), "exitMapView"),
                GetType()));
        }

        private static float altimeterPosition = -1f;

        protected override void OnLoadData(ConfigNode node)
        {
            if (!node.TryGetValue(nameof(altimeterPosition), ref altimeterPosition))
            {
                altimeterPosition = -1f;
            }
        }

        private static void SetTopFramePosition(bool defaultPosition = false)
        {
            if (FlightUIModeController.Instance == null || FlightUIModeController.Instance.altimeterFrame == null || TelemetryUpdate.Instance == null || ApplicationLauncher.Instance == null)
            {
                return;
            }

            RectTransform telemetryTransform = (RectTransform)TelemetryUpdate.Instance.transform;
            float minX = telemetryTransform.anchoredPosition.x + telemetryTransform.sizeDelta.x;
            minX *= telemetryTransform.lossyScale.x;

            RectTransform appLauncherTransform = (RectTransform)ApplicationLauncher.Instance.launcherSpace.GetChild(0).transform;
            float maxX = appLauncherTransform.sizeDelta.x;
            maxX *= appLauncherTransform.lossyScale.x;

            RectTransform topFrame = (RectTransform)FlightUIModeController.Instance.altimeterFrame.transform;
            float topFrameHalfWidth = topFrame.sizeDelta.x * 0.5f * topFrame.lossyScale.x;

            float anchorMin = (minX + topFrameHalfWidth) / Screen.width;
            float anchorMax = (Screen.width - maxX - topFrameHalfWidth) / Screen.width;

            if (altimeterPosition < 0f || altimeterPosition > 1f)
            {
                altimeterPosition = ((Screen.width * 0.5f) - minX) / (Screen.width - maxX);
            }

            float anchorPos = anchorMin + (anchorMax - anchorMin) * altimeterPosition;


            topFrame.anchorMax = new Vector2(anchorPos, topFrame.anchorMax.y);
            topFrame.anchorMin = new Vector2(anchorPos, topFrame.anchorMin.y);
        }

        static void FlightUIModeController_Start_Postfix()
        {
            SetTopFramePosition();

            if (FlightUIModeController.Instance?.MapOptionsQuadrant != null)
                FlightUIModeController.Instance.MapOptionsQuadrant.gameObject.SetActive(false);
        }

        static void GameplaySettingsScreen_DrawMiniSettings_Postfix(GameplaySettingsScreen __instance, ref DialogGUIBase[] __result)
        {
            DialogGUIBase[] modifiedResult = new DialogGUIBase[__result.Length + 1];

            int lastPos = __result.Length - 2;

            for (int i = 0; i < modifiedResult.Length; i++)
            {
                if (i < lastPos)
                {
                    modifiedResult[i] = __result[i];
                }
                else if (i == lastPos)
                {
                    modifiedResult[i] = new DialogGUIHorizontalLayout(TextAnchor.MiddleLeft, 
                        new DialogGUILabel(() => Localizer.Format("Altimeter pos (Left<->Right)"), 150f), 
                        new DialogGUISlider(() => altimeterPosition, 0f, 1f, wholeNumbers: false, 200f, 20f, delegate (float f)
                    {
                        altimeterPosition = f;
                        SetTopFramePosition();
                    }), new DialogGUIFlexibleSpace());
                }
                else
                {
                    modifiedResult[i] = __result[i - 1];
                }
            }

            __result = modifiedResult;
        }

        static void GameplaySettingsScreen_ApplySettings_Postfix()
        {
            ConfigNode node = new ConfigNode();
            node.AddValue(nameof(altimeterPosition), altimeterPosition);
            SaveData<AltimeterHorizontalPosition>(node);
        }

        static void MapView_enterMapView_Prefix()
        {
            if (FlightUIModeController.Instance?.MapOptionsQuadrant != null)
                FlightUIModeController.Instance.MapOptionsQuadrant.gameObject.SetActive(true);
        }

        static void MapView_exitMapView_Postfix()
        {
            if (FlightUIModeController.Instance?.MapOptionsQuadrant != null)
                FlightUIModeController.Instance.MapOptionsQuadrant.gameObject.SetActive(false);
        }
    }
}
