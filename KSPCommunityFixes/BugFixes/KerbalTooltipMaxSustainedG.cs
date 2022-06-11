using System;
using System.Collections.Generic;

namespace KSPCommunityFixes
{
    public class KerbalTooltipMaxSustainedG : BasePatch
    {
        // fix the kerbal tooltip giving wrong "max sustained G limit" information
        // Credit to NathanKell for the fix

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            // replace the orginial delegate, no need for harmony patching here
            ProtoCrewMember.MaxSustainedG = MaxSustainedG;
        }

        public static double MaxSustainedG(ProtoCrewMember pcm)
        {
            // orginial : return Math.Pow(PhysicsGlobals.KerbalGOffset * GToleranceMult(pcm), 1.0 / PhysicsGlobals.KerbalGPower);
            return Math.Pow(PhysicsGlobals.KerbalGOffset, 1.0 / PhysicsGlobals.KerbalGPower) * ProtoCrewMember.GToleranceMult(pcm);
        }
    }
}
