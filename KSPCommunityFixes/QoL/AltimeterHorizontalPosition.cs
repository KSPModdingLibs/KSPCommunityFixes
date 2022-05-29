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
        public static string LOC_SettingsTitle =
            "Altimeter pos (Left<->Right)";
        public static string LOC_SettingsTooltip =
            "Set the horizontal position of the flight scene altimeter widget";

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(FlightUIModeController), "Start"),
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

        public static float altimeterPosition = -1f;

        protected override void OnLoadData(ConfigNode node)
        {
            if (!node.TryGetValue(nameof(altimeterPosition), ref altimeterPosition))
            {
                altimeterPosition = -1f;
            }
        }

        // Transform the [0, 1] altimeterPosition setting into a position that doesn't overlap the
        // others stock UI elements at the top of the screen
        public static void SetTopFramePosition()
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

        // show the map vessel filters button when entering map view
        static void MapView_enterMapView_Prefix()
        {
            GameObject trackingFiltersHoverArea = FlightUIModeController.Instance?.MapOptionsQuadrant.gameObject.transform?.GetChild(0)?.GetChild(0).gameObject;

            if (trackingFiltersHoverArea != null)
            {
                trackingFiltersHoverArea.SetActive(true);

                // Fix issue #39
                // updateButtonsToFilter is checking for the isActiveAndEnabled state of button GO before setting state.
                // Since we disabled the containing GO when this was called in MapViewFiltering.Init(), the buttons state  
                // will be incorrect initially, so update them manually.
                MapViewFiltering.Instance.updateButtonsToFilter(MapViewFiltering.vesselTypeFilter);
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
