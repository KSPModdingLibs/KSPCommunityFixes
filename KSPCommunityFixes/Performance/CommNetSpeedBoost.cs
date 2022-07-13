using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.Performance
{
	class CommNetSpeedBoost : BasePatch
	{
		private static double packedInterval = 0.5;
		private static double unpackedInterval = 5.0;

		protected override void ApplyPatches(List<PatchInfo> patches)
		{
			ConfigNode settingsNode = KSPCommunityFixes.SettingsNode.GetNode("COMM_NET_PERFORMANCE_SETTINGS");

			if (settingsNode != null)
			{
				settingsNode.TryGetValue("packedInterval", ref packedInterval);
				settingsNode.TryGetValue("unpackedInterval", ref unpackedInterval);
			}

			patches.Add(new PatchInfo(
				PatchMethodType.Prefix,
				AccessTools.Method(typeof(CommNet.CommNetNetwork), nameof(CommNet.CommNetNetwork.Update)),
				this));
		}

		static bool CommNetNetwork_Update_Prefix(CommNet.CommNetNetwork __instance)
		{
			if (!__instance.queueRebuild && !__instance.commNet.IsDirty)
			{
				double timeSinceLastUpdate = Time.timeSinceLevelLoad - __instance.prevUpdate;

				if (FlightGlobals.ActiveVessel != null)
				{
					double interval = FlightGlobals.ActiveVessel.packed ? packedInterval : unpackedInterval;
					if (timeSinceLastUpdate < interval)
					{
						__instance.graphDirty = true;
						return false;
					}
				}
			}

			return true;
		}
	}
}
