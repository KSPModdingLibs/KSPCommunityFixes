// see https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/142

// #define REROOT_DEBUG_MODULE

using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine.UIElements;
using UnityEngine;

#if REROOT_DEBUG_MODULE
using UnityEngine;
#endif

namespace KSPCommunityFixes.BugFixes
{
    class ReRootPreserveSurfaceAttach : BasePatch
    {
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
        }

        // In stock, this function is called after reversing a surface attachment during a re-root operation.
        // it tries to alter a part's surface attachment so that it mirrors the surface attach node of its parent.
        // But that's not a great idea, because a lot of things depend on the surface attach node never changing.
        // For example, if the user then picks the part back up, it won't attach the same way to anything else
        // To Fix this, instead of using the child's actual srfAttachNode, we create a new surface attach node and
        // just stick it in the regular AttachNode list.
        static bool AttachNode_ReverseSrfNodeDirection_Prefix(AttachNode __instance, AttachNode fromNode)
        {
            // note that instead of cloning the child's srfAttachNode and using its properties, we use the fromNode
            // because we want to mirror the previous state as much as possible - this node WAS the other part's srfAttachNode
            AttachNode newSrfAttachNode = AttachNode.Clone(fromNode);
            newSrfAttachNode.owner = __instance.owner;
            newSrfAttachNode.attachedPart = fromNode.owner;
            newSrfAttachNode.id = "KSPCF-reroot-srfAttachNode";
            Vector3 positionWorld = fromNode.owner.transform.TransformPoint(fromNode.position);
            Vector3 orientationWorld = fromNode.owner.transform.TransformDirection(fromNode.orientation);
            newSrfAttachNode.position = newSrfAttachNode.originalPosition = newSrfAttachNode.owner.transform.InverseTransformPoint(positionWorld);
            newSrfAttachNode.orientation = newSrfAttachNode.originalOrientation = -newSrfAttachNode.owner.transform.InverseTransformDirection(orientationWorld);
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
    }

#if REROOT_DEBUG_MODULE
    public class SrfAttachInfo : PartModule
    {
        [KSPField(guiActive = true, guiActiveEditor = true)]
        public string attachInfo;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "srf node visual debug")]
        [UI_Toggle]
        public bool attachVisualDebug = true;

        void Update()
        {
            attachInfo = "\n";

            if (part.srfAttachNode != null)
                attachInfo += "srf: " + part.srfAttachNode.position + " / " + part.srfAttachNode.orientation + "\n";

            for (int i = 0; i < part.attachNodes.Count; i++)
            {
                attachInfo += $"{part.attachNodes[i].id}: {part.attachNodes[i].position} / {part.attachNodes[i].orientation}\n";
            }
        }

        private void OnRenderObject()
        {
            if (!attachVisualDebug)
                return;

            if (!part.attachRules.srfAttach)
                return;

            Vector3 pos = part.transform.rotation * part.srfAttachNode.position + part.transform.position;
            Vector3 dir = part.transform.TransformDirection(part.srfAttachNode.orientation);

            Camera cam = GetActiveCam();
            GLStart();
            DrawPoint(cam, pos, Color.green);
            DrawRay(cam, pos, dir * 0.5f, Color.red);
            GLEnd();
        }

        private static Material _material;

        private static Material Material
        {
            get
            {
                if (_material == null)
                    _material = new Material(Shader.Find("Hidden/Internal-Colored"));
                return _material;
            }
        }

        private static void GLStart()
        {
            GL.PushMatrix();
            Material.SetPass(0);
            GL.LoadPixelMatrix();
            GL.Begin(GL.LINES);
        }

        private static void GLEnd()
        {
            GL.End();
            GL.PopMatrix();
        }


        private static Camera GetActiveCam()
        {
            Camera cam;
            if (HighLogic.LoadedSceneIsEditor)
                cam = EditorLogic.fetch.editorCamera;
            else if (HighLogic.LoadedSceneIsFlight)
                cam = MapView.MapIsEnabled ? PlanetariumCamera.Camera : FlightCamera.fetch.mainCamera;
            else
                cam = Camera.main;
            return cam;
        }

        private static void DrawRay(Camera cam, Vector3 origin, Vector3 direction, Color color)
        {
            Vector3 screenPoint1 = cam.WorldToScreenPoint(origin);
            Vector3 screenPoint2 = cam.WorldToScreenPoint(origin + direction);

            GL.Color(color);
            GL.Vertex3(screenPoint1.x, screenPoint1.y, 0);
            GL.Vertex3(screenPoint2.x, screenPoint2.y, 0);
        }

        private static void DrawPoint(Camera cam, Vector3 position, Color color)
        {
            DrawRay(cam, position + Vector3.up * 0.1f, -Vector3.up * 0.2f, color);
            DrawRay(cam, position + Vector3.right * 0.1f, -Vector3.right * 0.2f, color);
            DrawRay(cam, position + Vector3.forward * 0.1f, -Vector3.forward * 0.2f, color);
        }
    }
#endif
}
