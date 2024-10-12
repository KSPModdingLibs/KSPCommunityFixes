using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes
{
    public class ForceSyncSceneSwitch : BasePatch
    {
        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(SceneTransitionMatrix), "GetTransitionValue", new Type[] { typeof(GameScenes), typeof(GameScenes) }),
                this));
        }

        static bool SceneTransitionMatrix_GetTransitionValue_Prefix(out bool __result)
        {
            __result = false;
            return false;
        }
    }
}
