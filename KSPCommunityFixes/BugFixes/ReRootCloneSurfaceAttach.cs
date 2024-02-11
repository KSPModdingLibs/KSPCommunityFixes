using Expansions.Missions.Editor;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    class ReRootCloneSurfaceAttach : BasePatch
    {
        // the name here isn't important, but if anyone is debugging an issue I'd like to make it clear where it came from.
        // I'm pretty sure the empty string has some special meaning, and we need to be sure it doesn't collide with any existing node IDs
        // we also use this to fix up attach nodes when loading a craft file.
        const string CLONED_NODE_ID = "KSPCF-reroot-srfAttachNode";

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(AttachNode), nameof(AttachNode.ReverseSrfNodeDirection)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(AttachNode), nameof(AttachNode.ChangeSrfNodePosition)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.FindAttachNode)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EditorLogicBase), nameof(EditorLogicBase.clearAttachNodes)),
                this));
        }

        // In stock, this function is called after reversing a surface attachment during a re-root operation.
        // it tries to alter a part's surface attachment so that it mirrors the surface attach node of its parent.
        // But that's not a great idea, because a lot of things depend on the surface attach node never changing.
        // For example, if the user then picks the part back up, it won't attach the same way to anything else
        // To fix this, instead of using the child's actual srfAttachNode we create a new surface attach node and
        // just stick it in the regular AttachNode list.
        static bool AttachNode_ReverseSrfNodeDirection_Prefix(AttachNode __instance, AttachNode fromNode)
        {
            // Ideally we would be cloning fromNode in order to match the original attachment as closely as possible.
            // however when loading a saved craft file, we won't know anything about that node, possibly leading
            // to differing behavior.  So we'll clone this part's attachnode instead.
            AttachNode newSrfAttachNode = AttachNode.Clone(__instance);
            newSrfAttachNode.attachedPart = fromNode.owner;
            newSrfAttachNode.id = CLONED_NODE_ID;

            // convert the position, orientation from the other part's local space into ours
            Vector3 positionWorld = fromNode.owner.transform.TransformPoint(fromNode.position);
            Vector3 orientationWorld = fromNode.owner.transform.TransformDirection(fromNode.orientation);
            newSrfAttachNode.originalPosition = newSrfAttachNode.owner.transform.InverseTransformPoint(positionWorld);
            newSrfAttachNode.originalOrientation = -newSrfAttachNode.owner.transform.InverseTransformDirection(orientationWorld);
            newSrfAttachNode.position = newSrfAttachNode.originalPosition;
            newSrfAttachNode.orientation = newSrfAttachNode.originalOrientation;
            newSrfAttachNode.owner.attachNodes.Add(newSrfAttachNode);

            // now clear the srfAttachNodes from both parts
            __instance.attachedPart = null;
            fromNode.attachedPart = null;

            return false;
        }

        // this function is just horribly broken and no one could call it, ever
        static bool AttachNode_ChangeSrfNodePosition_Prefix()
        {
            return false;
        }

        static void Part_FindAttachNode_Postfix(Part __instance, string nodeId, ref AttachNode __result)
        {
            // TODO: ideally we would only do this if we know we're in the process of loading a craft file.  Is there any way to check that?
            if (__result == null && nodeId == CLONED_NODE_ID)
            {
                AttachNode newSrfAttachNode = AttachNode.Clone(__instance.srfAttachNode);
                newSrfAttachNode.id = CLONED_NODE_ID;
                __instance.attachNodes.Add(newSrfAttachNode);
                __result = newSrfAttachNode;
            }
        }

        // In the editor, when detaching parts, remove the extra attachnode we added
        static void EditorLogicBase_clearAttachNodes_Postfix(Part part)
        {
            for (int i = 0; i < part.attachNodes.Count; i++)
            {
                AttachNode attachNode = part.attachNodes[i];

                if (attachNode.id == CLONED_NODE_ID && attachNode.attachedPart.IsNullRef())
                {
                    part.attachNodes.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
