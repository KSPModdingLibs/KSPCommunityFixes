using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.Performance
{
    public class ProgressTrackingSpeedBoost : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(ProgressTracking), nameof(ProgressTracking.Update));

            AddPatch(PatchType.Postfix, AccessTools.DeclaredConstructor(typeof(KSPAchievements.CelestialBodySubtree), new Type[] { typeof(CelestialBody) }),
                nameof(CelestialBodySubtree_Constructor_Postfix));
        }

        static bool ProgressTracking_Update_Prefix(ProgressTracking __instance)
        {
            if (!HighLogic.LoadedSceneIsEditor && FlightGlobals.ActiveVessel != null)
            {
                __instance.achievementTree.IterateVessels(FlightGlobals.ActiveVessel);
            }

            return false;
        }

        static void CelestialBodySubtree_Constructor_Postfix(KSPAchievements.CelestialBodySubtree __instance)
        {
            __instance.OnIterateVessels = null;
        }
    }
}
