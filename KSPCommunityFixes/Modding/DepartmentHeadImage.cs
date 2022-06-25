using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.UI.Screens;

namespace KSPCommunityFixes.Modding
{
    class DepartmentHeadImage : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Administration), nameof(Administration.AddKerbalListItem)),
                this));
        }

        static void Administration_AddKerbalListItem_Postfix(Administration __instance, ref Strategies.DepartmentConfig dep)
        {
            if (dep.AvatarPrefab != null || dep.HeadImage == null)
                return;

            KSP.UI.UIListItem item = __instance.scrollListKerbals.GetUilistItemAt(__instance.scrollListKerbals.Count - 1);
            KerbalListItem kerbal = item.GetComponent<KerbalListItem>();
            kerbal.kerbalImage.texture = dep.HeadImage;
            kerbal.kerbalImage.material = kerbal.kerbalImage.defaultMaterial;
        }
    }
}
