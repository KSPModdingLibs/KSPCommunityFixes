using System;
using HarmonyLib;
using KSP.UI.Screens.Editor;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    class FixGetUnivseralTime : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Planetarium), nameof(Planetarium.GetUniversalTime)),
                this));
        }

        static bool Planetarium_GetUniversalTime_Prefix(ref double __result)
        {
            if (HighLogic.LoadedSceneIsEditor || Planetarium.fetch == null)
                __result = HighLogic.CurrentGame?.UniversalTime ?? 0d;
            else
                __result = Planetarium.fetch.time;

            return false;
        }

    }
}
