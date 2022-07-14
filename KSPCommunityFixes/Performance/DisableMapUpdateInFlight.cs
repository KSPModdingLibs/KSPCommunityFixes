// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/59

using System;
using HarmonyLib;
using System.Collections.Generic;

namespace KSPCommunityFixes.Performance
{
    class DisableMapUpdateInFlight : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(OrbitRendererBase), nameof(OrbitRendererBase.Start)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(OrbitRendererBase), nameof(OrbitRendererBase.LateUpdate)),
                this));
        }

        static void OrbitRendererBase_Start_Postfix(OrbitRendererBase __instance)
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && !MapView.fetch.updateMap)
            {
                if (__instance.objectNode.IsNotNullRef())
                    __instance.objectNode.gameObject.SetActive(false);

                if (__instance.ascNode.IsNotNullRef())
                    __instance.ascNode.gameObject.SetActive(false);

                if (__instance.descNode.IsNotNullRef())
                    __instance.descNode.gameObject.SetActive(false);

                if (__instance.apNode.IsNotNullRef())
                    __instance.apNode.gameObject.SetActive(false);

                if (__instance.peNode.IsNotNullRef())
                    __instance.peNode.gameObject.SetActive(false);
            }
        }

        static bool OrbitRendererBase_LateUpdate_Prefix()
        {
            return MapView.fetch.updateMap;
        }
    }
}
