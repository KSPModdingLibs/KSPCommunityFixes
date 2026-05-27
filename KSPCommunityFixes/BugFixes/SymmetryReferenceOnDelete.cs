// Fixes https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/382
//
// EditorLogic.wipeSymmetry() severs symmetry references one-sidedly: it removes outside-the-subtree
// counterparts from the wiped subtree's parts, but never removes those subtree parts from the outside
// counterparts' lists. That's harmless when the outside counterpart is also being destroyed, but when
// a part is removed from symmetry while it still has children and is then deleted (or moved), a
// surviving counterpart keeps a now-destroyed reference. ShipConstruct.SaveShip() then dereferences
// symmetryCounterparts[k].partInfo with no null guard and throws, breaking the editor.
//
// We re-implement wipeSymmetry() with the reference removal made two-sided, fixing the root cause and
// keeping the live model consistent for the other unguarded symmetryCounterparts consumers.

using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes;

internal class SymmetryReferenceOnDelete : BasePatch
{
    protected override Version VersionMin => new(1, 8, 0);

    protected override void ApplyPatches()
    {
        AddPatch(PatchType.Override, typeof(EditorLogic), nameof(EditorLogic.wipeSymmetry));
    }

    private static void EditorLogic_wipeSymmetry_Override(Part selPart)
    {
        selPart.symmetryCounterparts ??= [];
        selPart.symmetryCounterparts.Clear();

        List<Part> subtree = EditorLogicBase.FindPartsInChildren(selPart);
        HashSet<Part> subtreeSet = [.. subtree];
        foreach (var subpart in subtree)
        {
            var counterparts = subpart.symmetryCounterparts;

            int j = 0;
            int count = counterparts.Count;
            for (int i = 0; i < count; ++i)
            {
                var counterpart = counterparts[i];
                if (subtreeSet.Contains(counterpart))
                {
                    counterparts[j++] = counterpart;
                    continue;
                }

                // Remove the reverse reference as well. This is the part that
                // we are actually intending to patch.
                if (counterpart.IsNotNullOrDestroyed())
                {
                    counterpart.symmetryCounterparts.Remove(subpart);
                    counterpart.SetRemoveSymmetryVisibililty();
                }
            }

            counterparts.RemoveRange(j, count - j);
        }

        selPart.SetRemoveSymmetryVisibililty();
    }
}
