// When a vessel is split off another one (decoupling, undocking, a part being destroyed) or loaded
// without a valid reference transform, Vessel.FallBackReferenceTransform() is responsible for picking
// a control point for it. It delegates that choice to ShipConstruction.findFirstCrewablePart() :
//
//   public void FallBackReferenceTransform()
//   {
//       if (this[referenceTransformId] == null)
//           SetReferenceTransform(ShipConstruction.findFirstCrewablePart(rootPart), true);
//   }
//
//   public static Part findFirstCrewablePart(Part part)
//   {
//       if (part.CrewCapacity > 0 && part.protoModuleCrew.Count > 0 && part.isControlSource > Vessel.ControlLevel.NONE)
//           return part;
//       ...recurse over part.children...
//   }
//
// findFirstCrewablePart() answers a different question than the one being asked : it looks for a part a
// kerbal is sitting in and can fly the vessel from, which is what its other callers (crew transfer, vessel
// spawning) want. A probe core fails the crew capacity test outright, and an empty command pod fails the
// "crew aboard" test, so an uncrewed vessel gets no control point at all : SetReferenceTransform(null, true)
// resets referenceTransformId to 0 and referenceTransformPart to null, and the vessel is then oriented by
// its root part. Every navball reading, SAS hold and autopilot attitude on that vessel is taken from
// whatever the root part happens to be, until the player manually picks a control point.
//
// We reimplement FallBackReferenceTransform() so that when findFirstCrewablePart() comes up empty, it falls
// back to the first part that is a control source, searched in the same root-first order. A crewed part
// still wins, matching how KSP ranks a kerbal above a probe core elsewhere in its control state handling.
// findFirstCrewablePart() itself is left alone, as its other callers want its existing meaning.

using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes;

internal class UncrewedControlPointFallback : BasePatch
{
    protected override Version VersionMin => new(1, 8, 0);

    protected override void ApplyPatches()
    {
        AddPatch(PatchType.Override, typeof(Vessel), nameof(Vessel.FallBackReferenceTransform));
    }

    private static void Vessel_FallBackReferenceTransform_Override(Vessel __instance)
    {
        if (__instance[__instance.referenceTransformId].IsNotNullOrDestroyed())
            return;

        Part referencePart = ShipConstruction.findFirstCrewablePart(__instance.rootPart);

        // This is the part we are actually intending to patch : stock passes the null straight through.
        if (referencePart.IsNullOrDestroyed())
            referencePart = FindFirstControlSourcePart(__instance.rootPart);

        __instance.SetReferenceTransform(referencePart, true);
    }

    private static Part FindFirstControlSourcePart(Part part)
    {
        if (part.isControlSource > Vessel.ControlLevel.NONE)
            return part;

        List<Part> children = part.children;
        for (int i = 0; i < children.Count; i++)
        {
            Part controlSource = FindFirstControlSourcePart(children[i]);
            if (controlSource.IsNotNullOrDestroyed())
                return controlSource;
        }

        return null;
    }
}
