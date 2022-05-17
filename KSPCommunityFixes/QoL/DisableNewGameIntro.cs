using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.QoL
{
    class DisableNewGameIntro : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ScenarioNewGameIntro), nameof(ScenarioNewGameIntro.OnAwake)),
                this));
        }

        static void ScenarioNewGameIntro_OnAwake_Prefix(ScenarioNewGameIntro __instance)
        {
            __instance.editorComplete = true;
            __instance.kscComplete = true;
            __instance.tsComplete = true;
        }
    }
}
