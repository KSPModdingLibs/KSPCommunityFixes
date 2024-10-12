using System;

namespace KSPCommunityFixes
{
    public class ForceSyncSceneSwitch : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(SceneTransitionMatrix), nameof(SceneTransitionMatrix.GetTransitionValue), new Type[] { typeof(GameScenes), typeof(GameScenes) });
        }

        static bool SceneTransitionMatrix_GetTransitionValue_Prefix(out bool __result)
        {
            __result = false;
            return false;
        }
    }
}
