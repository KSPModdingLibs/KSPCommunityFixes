using System;

namespace KSPCommunityFixes
{
    public class BlockMapViewPartClick : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(Part), "UpdateMouseOver");
        }

        static bool Part_UpdateMouseOver_Prefix()
        {
            return !HighLogic.LoadedSceneIsFlight || CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Map;
        }
    }
}
