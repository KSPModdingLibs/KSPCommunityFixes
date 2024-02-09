// see https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/142

// #define REROOT_DEBUG_MODULE

using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

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
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(AttachNode), nameof(AttachNode.ReverseSrfNodeDirection)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(AttachNode), nameof(AttachNode.ChangeSrfNodePosition)),
                this));
        }

        // this function gets called on the old parent node when a surface attachment is being reversed during re-root
        // the old parent now needs to become surface-attached to the new parent, but the position of the surface attach node should not change in worldspace
        // the stock code fixes the position but not the orientation of the node.
        private static void AttachNode_ReverseSrfNodeDirection_Postfix(AttachNode __instance, AttachNode fromNode)
        {
            Vector3 oldAttachNodeWorldOrientation = fromNode.owner.transform.TransformDirection(fromNode.orientation);
            __instance.orientation = -__instance.owner.transform.InverseTransformDirection(oldAttachNodeWorldOrientation);
        }

        private static bool AttachNode_ChangeSrfNodePosition_Prefix()
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
