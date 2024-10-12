using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.QoL
{
    class DisableNewGameIntro : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(ScenarioNewGameIntro), nameof(ScenarioNewGameIntro.OnAwake));
        }

        static void ScenarioNewGameIntro_OnAwake_Prefix(ScenarioNewGameIntro __instance)
        {
            __instance.editorComplete = true;
            __instance.kscComplete = true;
            __instance.tsComplete = true;
        }
    }
}
