using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
	internal class ReRootFixSurfaceAttach : BasePatch
	{
		protected override void ApplyPatches(List<PatchInfo> patches)
		{
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(AttachNode), nameof(AttachNode.ReverseSrfNodeDirection)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(AttachNode), nameof(AttachNode.ChangeSrfNodePosition)),
                this));
        }

        static void AttachNode_ReverseSrfNodeDirection_Postfix(AttachNode __instance, AttachNode fromNode)
        {
            // the stock function only moves the position, not orientation
            Vector3 orientationWorld = fromNode.owner.transform.TransformDirection(fromNode.orientation);
            __instance.orientation = -__instance.owner.transform.InverseTransformDirection(orientationWorld);

            // Terrible hack: the surface attach logic in the editor seems broken (or I don't understand how it's supposed to work)
            // For node attachments, the orientation vector points towards the other part.  But that doesn't seem to be the case for surface attachments - or at least not consistently.
            // Most stock parts have a surface attach node of 0,0,-Z, 0,0,1 OR +x,0,0, 1,0,0.
            // That is, if the surface attach node is positioned at a negative Z coordinate, the orientation is 0,0,1.  if the attachNode is at a positive X coordinate, the orientation is 1,0,0.
            // This is inconsistent!  But yet somehow the editor works correctly with these two configurations.  I sort of suspect there's a rotation being applied out of order somewhere.
            // But it means that we can't apply a consistent rule here AND we can't fix the editor attachment logic without breaking all the existing stock content (or at least, the ones that are set up inconsistently)
            // I don't really understand it, but negating the x coord seems to set up the orientation of the attachnode in a way that makes the editor position the part correctly.
            // Is this just a consequence of the fact that this reversal is really a "mirroring" of the attachment rather than a rotation?  I'm not sure.
            __instance.orientation.x = -__instance.orientation.x;

            // the stock function does not touch the "original" values - but lots of code breaks when they don't match
            // and the original values are not stored in the craft file, but they get set to the position/orientation on loading the craft
            // So even if we wanted to keep them, they'd be lost on a save/load.
            __instance.originalOrientation = __instance.orientation;
            __instance.originalPosition = __instance.position;

            // TODO: this patch is not quite complete (or needs separate fixes):
            // - the stock ModulePartVariants will stomp over attachnode positions when loading a craft file, which ends up positioning the parts incorrectly after reversing a surface attachment
            // - Something is causing attachnodes to not be positioned correctly after reloading a vessel in flight - initial launch is correct, revert to launch seems to restore the prefab's srfAttachNode
        }

        // this function is just horribly broken and no one could call it, ever
        static bool AttachNode_ChangeSrfNodePosition_Prefix()
        {
            return false;
        }
    }
}
