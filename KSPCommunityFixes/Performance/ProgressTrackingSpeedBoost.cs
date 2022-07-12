using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.Performance
{
	public class ProgressTrackingSpeedBoost : BasePatch
	{
		protected override void ApplyPatches(List<PatchInfo> patches)
		{
			patches.Add(new PatchInfo(
				PatchMethodType.Prefix,
				AccessTools.Method(typeof(ProgressTracking), nameof(ProgressTracking.Update)),
				this));

			patches.Add(new PatchInfo(
				PatchMethodType.Postfix,
				AccessTools.DeclaredConstructor(typeof(KSPAchievements.CelestialBodySubtree), new Type[] { typeof(CelestialBody) }),
				this,
				nameof(CelestialBodySubtree_Constructor_Postfix)));
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
