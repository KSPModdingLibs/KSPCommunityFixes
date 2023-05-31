using System;
using System.Collections.Generic;
using HarmonyLib;

namespace KSPCommunityFixes.BugFixes
{
    class ThumbnailSpotlight : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

		protected override void ApplyPatches(List<PatchInfo> patches)
		{
			patches.Add(new PatchInfo(
				PatchMethodType.Postfix,
				AccessTools.Method(typeof(CraftThumbnail), nameof(CraftThumbnail.TakePartSnapshot)),
				this));
		}

		private static void CraftThumbnail_TakePartSnapshot_Postfix()
		{
			if (CraftThumbnail.snapshotCamera.IsNotNullOrDestroyed())
			{
				UnityEngine.Object.Destroy(CraftThumbnail.snapshotCamera.gameObject);
			}
		}
	}
}
