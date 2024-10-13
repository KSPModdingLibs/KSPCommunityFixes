// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/249

using System;

namespace KSPCommunityFixes.BugFixes
{
    internal class ModuleActiveRadiatorNoParentException : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(ModuleActiveRadiator), nameof(ModuleActiveRadiator.IsSibling));
        }

        private static bool ModuleActiveRadiator_IsSibling_Override(ModuleActiveRadiator instance, Part targetPart)
        {
            Part parent = instance.part.parent;
            if (parent.IsNullOrDestroyed())
                return false;

            if (targetPart == parent)
                return true;

            if (parent.parent.IsNotNullOrDestroyed() && targetPart == parent.parent)
                return true;

            if (targetPart.parent.IsNotNullOrDestroyed() && targetPart.parent == parent)
                return true;

            return false;
        }
    }
}
