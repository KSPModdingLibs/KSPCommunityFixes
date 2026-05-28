// KSP 1.8 introduced a new orbit bisection solver (_SolveSOI). The new solver
// is faster, but buggy. This causes flickering where an orbit line "flickers"
// between multiple different solutions.
//
// We can sorta fix this at the expense of some performance by just reverting
// back to the old solver, which is what this patch does.

using System;

namespace KSPCommunityFixes.BugFixes;

internal class PatchedConicEncounterFlickering : BasePatch
{
    protected override Version VersionMin => new Version(1, 8, 0);

    protected override void ApplyPatches() { }

    protected override void OnPatchApplied()
    {
        Orbit.SolveSOI = Orbit._SolveSOI_BSP;
    }
}
