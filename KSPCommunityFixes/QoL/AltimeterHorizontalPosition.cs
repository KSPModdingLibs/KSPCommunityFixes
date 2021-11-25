using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.Localization;
using KSP.UI.Screens;
using KSP.UI.Screens.Flight;
using UnityEngine;

namespace KSPCommunityFixes
{
    // Overview :
    // Allow tweaking the altimeter widget horizontal position between the comms widget on the left
    // and the launcher toolbar on the right, by adding a tweakable in the pause menu settings, similar
    // to the navball horizontal position tweakable.

    // Technical note :
    // The vessel types filter button (top center of the screen in map view) isn't inactive when
    // in flight view, it is actually just behind the altimeter widget. To avoid it being visible
    // when the altimeter isn't at the stock position, we deactivate the map filter gameobject.
    // However, the map filter has UIPanelTransition components that attempt to run a coroutine
    // on the 2 top gameobjects on various gameevents (OnCameraChange, onVesselChange, onVesselWasModified)
    // To avoid errors, we disable the second child instead of the top level gameobject :
    // -> [TrackingFilters (UIPanelTransition)]
    //    -> [IVACollapseGroup (UIPanelTransition)]
    //       -> [hoverArea] -> this is the gameobject we are disabling

    class AltimeterHorizontalPosition : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(FlightUIModeController), "Start"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(GameplaySettingsScreen), "DrawMiniSettings"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(GameplaySettingsScreen), "ApplySettings"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(MapView), "enterMapView"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(MapView), "exitMapView"),
                this));
        }

        private static float altimeterPosition = -1f;

        protected override void OnLoadData(ConfigNode node)
        {
            if (!node.TryGetValue(nameof(altimeterPosition), ref altimeterPosition))
            {
                altimeterPosition = -1f;
            }
        }

        // Transform the [0, 1] altimeterPosition setting into a position that doesn't overlap the
        // others stock UI elements at the top of the screen
        private static void SetTopFramePosition()
        {
            if (FlightUIModeController.Instance == null || FlightUIModeController.Instance.altimeterFrame == null || TelemetryUpdate.Instance == null || ApplicationLauncher.Instance == null)
            {
                return;
            }

            // get the signal widget gameobjects on the right of the screen
            RectTransform telemetryTransform = (RectTransform)TelemetryUpdate.Instance.transform;
            float minX = telemetryTransform.anchoredPosition.x + telemetryTransform.sizeDelta.x;
            minX *= telemetryTransform.lossyScale.x;

            // get the application launcher toolbar on the left of the screen
            RectTransform appLauncherTransform = (RectTransform)ApplicationLauncher.Instance.launcherSpace.GetChild(0).transform;
            float maxX = appLauncherTransform.sizeDelta.x;
            maxX *= appLauncherTransform.lossyScale.x;

            // compute the available space between those two elements
            RectTransform topFrame = (RectTransform)FlightUIModeController.Instance.altimeterFrame.transform;
            float topFrameHalfWidth = topFrame.sizeDelta.x * 0.5f * topFrame.lossyScale.x;
            float anchorMin = (minX + topFrameHalfWidth) / Screen.width;
            float anchorMax = (Screen.width - maxX - topFrameHalfWidth) / Screen.width;

            // if position is undefined, set it to the center of the screen (stock default position)
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
            // position the altimeter
            SetTopFramePosition();

            // hide map the vessel filters button
            GameObject trackingFiltersHoverArea = FlightUIModeController.Instance?.MapOptionsQuadrant.gameObject.transform?.GetChild(0)?.GetChild(0).gameObject;
            if (trackingFiltersHoverArea != null)
            {
                trackingFiltersHoverArea.SetActive(false);
            }
        }

        // add an altimeter horizontal position slider to the pause menu settings, similar to the stock navball position tweakable
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

        // save the altimeter position on apply/accept actions in the pause settings window
        static void GameplaySettingsScreen_ApplySettings_Postfix()
        {
            ConfigNode node = new ConfigNode();
            node.AddValue(nameof(altimeterPosition), altimeterPosition);
            SaveData<AltimeterHorizontalPosition>(node);
        }

        // show the map vessel filters button when entering map view
        static void MapView_enterMapView_Prefix()
        {
            GameObject trackingFiltersHoverArea = FlightUIModeController.Instance?.MapOptionsQuadrant.gameObject.transform?.GetChild(0)?.GetChild(0).gameObject;

            if (trackingFiltersHoverArea != null)
            {
                trackingFiltersHoverArea.SetActive(true);
            }
        }

        // hide the map vessel filters button when entering map view
        static void MapView_exitMapView_Postfix()
        {
            GameObject trackingFiltersHoverArea = FlightUIModeController.Instance?.MapOptionsQuadrant.gameObject.transform?.GetChild(0)?.GetChild(0).gameObject;

            if (trackingFiltersHoverArea != null)
            {
                trackingFiltersHoverArea.SetActive(false);
            }
        }
    }
}
