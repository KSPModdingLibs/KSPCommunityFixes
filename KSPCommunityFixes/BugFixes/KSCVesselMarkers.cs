using System;
using KSP.UI.Screens;

namespace KSPCommunityFixes.BugFixes
{
    internal class KSCVesselMarkers : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 5);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(FlightGlobals), "RemoveVessel");
            AddPatch(PatchType.Prefix, typeof(KSP.UI.Screens.KSCVesselMarkers), "SpawnVesselMarkers");
        }

        static void KSCVesselMarkers_SpawnVesselMarkers_Prefix(KSP.UI.Screens.KSCVesselMarkers __instance)
        {
            // Vessel destruction can refresh markers before the delayed initial spawn, which would append duplicates.
            __instance.ClearVesselMarkers();
        }

        static void FlightGlobals_RemoveVessel_Postfix(Vessel vessel)
        {
            KSP.UI.Screens.KSCVesselMarkers markerManager = KSP.UI.Screens.KSCVesselMarkers.fetch;
            if (markerManager.IsNullOrDestroyed())
                return;

            // Recovery and automatic debris cleanup bypass onVesselWillDestroy, leaving markers behind.
            // Match the vessel by managed reference even while its Unity object is being destroyed.
            for (int i = markerManager.markers.Count - 1; i >= 0; i--)
            {
                KSCVesselMarker marker = markerManager.markers[i];
                if (!ReferenceEquals(marker.v, vessel))
                    continue;

                marker.Terminate();
                markerManager.markers.RemoveAt(i);
            }
        }
    }
}
