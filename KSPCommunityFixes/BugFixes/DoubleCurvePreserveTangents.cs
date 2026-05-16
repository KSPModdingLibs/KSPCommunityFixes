using System;
using System.Runtime.CompilerServices;

namespace KSPCommunityFixes.BugFixes
{
    class DoubleCurvePreserveTangents : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(DoubleCurve), nameof(DoubleCurve.RecomputeTangents));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool DoubleCurve_RecomputeTangents_Prefix(DoubleCurve __instance)
        {
            // The existing function has a test if ( count == 1 ) and, if true, it
            // will flatten the tangents of the key regardless of if it is
            // set to autotangent or not. Since the tangents of a single-key
            // curve don't matter, we skip the function in that case.
            return __instance.keys.Count != 1;
        }
    }
}
