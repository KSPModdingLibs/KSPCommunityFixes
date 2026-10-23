using System;
using System.Collections.Generic;

namespace KSPCommunityFixes
{
    // Select the actual encounter patch, without depending on cross-body intersection counts.
    public class TargetBodyEncounterPeriapsis : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 5);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(ManeuverNodeEditorTabIntercept),
                nameof(ManeuverNodeEditorTabIntercept.UpdateUIElements));
        }

        static void ManeuverNodeEditorTabIntercept_UpdateUIElements_Postfix(
            ManeuverNodeEditorTabIntercept __instance, ref Orbit ___orbitContainingUT,
            string ___cacheAutoLOC_7001411, string ___cacheAutoLOC_6002332)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || !(vessel.targetObject is CelestialBody body))
                return;

            Orbit first = vessel.orbit;
            if (FlightUIModeController.Instance.Mode == FlightUIMode.MANEUVER_EDIT)
            {
                ManeuverNode node = __instance.mannodeEditorManager.SelectedManeuverNode;
                if (node != null)
                    first = node.nextPatch;
            }

            double now = Planetarium.GetUniversalTime();
            if (!BodyEncounterDisplay.TryGetPeriapsis(first, body, now, out Orbit encounter, out double periapsisUT))
                return;

            ___orbitContainingUT = encounter;
            __instance.intercept1DistanceLabel.text = KSPUtil.PrintSI(encounter.PeA, ___cacheAutoLOC_7001411, 6);
            __instance.intercept1TimeLabel.text = ___cacheAutoLOC_6002332 +
                KSPUtil.PrintTime(now - periapsisUT, 3, explicitPositive: true);
        }
    }

    public static class BodyEncounterDisplay
    {
        // Reuse existing active future patches; no solver work on UI refresh.
        public static bool TryGetPeriapsis(Orbit first, CelestialBody target, double now,
            out Orbit encounter, out double periapsisUT)
        {
            encounter = null;
            periapsisUT = double.NaN;
            var visited = new HashSet<Orbit>();
            for (Orbit patch = first; patch != null && visited.Add(patch); patch = patch.nextPatch)
            {
                if (!patch.activePatch)
                    break;
                if (!ReferenceEquals(patch.referenceBody, target))
                    continue;

                double ut = patch.StartUT + patch.timeToPe;
                if (double.IsNaN(ut) || double.IsInfinity(ut) || double.IsNaN(patch.PeA) || double.IsInfinity(patch.PeA) ||
                    ut < now || ut < patch.StartUT ||
                    (patch.patchEndTransition != Orbit.PatchTransitionType.FINAL && ut > patch.EndUT))
                    continue;

                encounter = patch;
                periapsisUT = ut;
                return true;
            }
            return false;
        }
    }
}
