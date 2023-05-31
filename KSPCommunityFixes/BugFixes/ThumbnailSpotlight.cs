using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes
{
    class ThumbnailSpotlight : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

		protected override void ApplyPatches(List<PatchInfo> patches)
		{
			patches.Add(new PatchInfo(
				PatchMethodType.Postfix,
				AccessTools.Method(typeof(CraftThumbnail), "TakePartSnapshot"),
				this));
		}

		private static void CraftThumbnail_TakePartSnapshot_Postfix()
		{
			if (CraftThumbnail.snapshotCamera != null)
			{
				UnityEngine.Object.Destroy(CraftThumbnail.snapshotCamera.gameObject);
			}
		}
	}
}
