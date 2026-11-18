using System;

namespace KSPCommunityFixes.BugFixes
{
    /// <summary>
    /// ModuleRCS reads the control input in Update() (once per frame) but fires in FixedUpdate() (every physics step),
    /// so it acts on input one or more physics steps old. At high physics warp or hyper-warp that delay is long enough
    /// to make SAS and autopilot attitude holds oscillate.
    /// Fix: call Update() at the start of FixedUpdate(). Update() only reads input, so the extra call is harmless, and
    /// calling it rather than copying its body keeps other mods' patches on it working (e.g. RP-1's avionics lock).
    /// </summary>
    class RCSInputLag : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(ModuleRCS), nameof(ModuleRCS.FixedUpdate));
        }

        private static void ModuleRCS_FixedUpdate_Prefix(ModuleRCS __instance)
        {
            __instance.Update();
        }
    }
}
