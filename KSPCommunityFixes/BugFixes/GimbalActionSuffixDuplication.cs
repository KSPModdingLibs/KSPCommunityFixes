using System;

namespace KSPCommunityFixes.BugFixes
{
    /// <summary>
    /// <see cref="ModuleGimbal.OnStart"/> calls <see cref="ModuleGimbal.SetActionsSuffix"/> once per gimbal
    /// transform instead of once, and each call appends " (actionSuffix)" to the gimbal's action and PAW field
    /// names again. A gimbal driving two transforms with <c>actionSuffix = Vernier</c> shows "Gimbal (Vernier) (Vernier)".<para/>
    /// We skip the call when the suffix has already been appended.
    /// </summary>
    internal class GimbalActionSuffixDuplication : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(ModuleGimbal), nameof(ModuleGimbal.SetActionsSuffix));
        }

        private static bool ModuleGimbal_SetActionsSuffix_Prefix(ModuleGimbal __instance)
        {
            return !__instance.Fields[nameof(ModuleGimbal.gimbalLock)].guiName.EndsWith($" ({__instance.actionSuffix})");
        }
    }
}
