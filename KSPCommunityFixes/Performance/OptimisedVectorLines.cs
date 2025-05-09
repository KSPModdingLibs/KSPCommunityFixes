using HarmonyLib;
using KSPCommunityFixes.Library;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Vectrosity;

namespace KSPCommunityFixes.Performance
{
    public class OptimisedVectorLines : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Transpiler, typeof(VectorLine), nameof(VectorLine.Line3D));

            AddPatch(PatchType.Transpiler, typeof(VectorLine), nameof(VectorLine.BehindCamera));
            AddPatch(PatchType.Transpiler, typeof(VectorLine), nameof(VectorLine.IntersectAndDoSkip));

            AddPatch(PatchType.Transpiler, typeof(VectorLine), nameof(VectorLine.Draw3D));
        }

        #region VectorLine Patches

        static IEnumerable<CodeInstruction> VectorLine_Line3D_Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceWorldToScreenPoint(instructions, 2);

        static IEnumerable<CodeInstruction> VectorLine_BehindCamera_Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceWorldToViewportPoint(instructions, 2);

        static IEnumerable<CodeInstruction> VectorLine_IntersectAndDoSkip_Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceWorldToScreenPoint(instructions, 2);

        static IEnumerable<CodeInstruction> VectorLine_Draw3D_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            instructions = ReplaceWorldToScreenPoint(instructions, 2);
            instructions = ReplaceScreenToWorldPoint(instructions, 4);

            return instructions;
        }

        static IEnumerable<CodeInstruction> VectorLine_SetIntersectionPoint3D_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceScreenToWorldPoint(instructions, 2);
        }

        private static IEnumerable<CodeInstruction> ReplaceCall(IEnumerable<CodeInstruction> instructions, MethodInfo original, MethodInfo replacement, int count = 1)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int counter = 0;

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Callvirt && code[i].Calls(original))
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = replacement;

                    if (++counter == count)
                        break;
                }
            }

            return code;
        }

        private static IEnumerable<CodeInstruction> ReplaceWorldToViewportPoint(IEnumerable<CodeInstruction> instructions, int count)
        {
            MethodInfo Camera_WorldToViewportPoint = AccessTools.Method(typeof(Camera), nameof(Camera.WorldToViewportPoint), new Type[] { typeof(Vector3) });
            MethodInfo VectorLineOptimisation_WorldToViewportPoint = AccessTools.Method(typeof(VectorLineCameraProjection), nameof(VectorLineCameraProjection.WorldToViewportPoint));

            return ReplaceCall(instructions, Camera_WorldToViewportPoint, VectorLineOptimisation_WorldToViewportPoint, count);
        }

        private static IEnumerable<CodeInstruction> ReplaceWorldToScreenPoint(IEnumerable<CodeInstruction> instructions, int count)
        {
            MethodInfo Camera_WorldToScreenPoint = AccessTools.Method(typeof(Camera), nameof(Camera.WorldToScreenPoint), new Type[] { typeof(Vector3) });
            MethodInfo VectorLineOptimisation_WorldToScreenPoint = AccessTools.Method(typeof(VectorLineCameraProjection), nameof(VectorLineCameraProjection.WorldToScreenPoint));

            return ReplaceCall(instructions, Camera_WorldToScreenPoint, VectorLineOptimisation_WorldToScreenPoint, count);
        }

        private static IEnumerable<CodeInstruction> ReplaceScreenToWorldPoint(IEnumerable<CodeInstruction> instructions, int count)
        {
            MethodInfo Camera_ScreenToWorldPoint = AccessTools.Method(typeof(Camera), nameof(Camera.ScreenToWorldPoint), new Type[] { typeof(Vector3) });
            MethodInfo VectorLineOptimisation_ScreenToWorldPoint = AccessTools.Method(typeof(VectorLineCameraProjection), nameof(VectorLineCameraProjection.ScreenToWorldPoint));

            return ReplaceCall(instructions, Camera_ScreenToWorldPoint, VectorLineOptimisation_ScreenToWorldPoint, count);
        }

        private static IEnumerable<CodeInstruction> ReplaceViewportToWorldPoint(IEnumerable<CodeInstruction> instructions, int count)
        {
            MethodInfo Camera_ViewportToWorldPoint = AccessTools.Method(typeof(Camera), nameof(Camera.ViewportToWorldPoint), new Type[] { typeof(Vector3) });
            MethodInfo VectorLineOptimisation_ViewportToWorldPoint = AccessTools.Method(typeof(VectorLineCameraProjection), nameof(VectorLineCameraProjection.ViewportToWorldPoint));

            return ReplaceCall(instructions, Camera_ViewportToWorldPoint, VectorLineOptimisation_ViewportToWorldPoint, count);
        }

        #endregion
    }

    public static class VectorLineCameraProjection
    {
        // Based on CameraProjectionCache from UnityCsReference.
        // https://github.com/Unity-Technologies/UnityCsReference/blob/2019.4/Editor/Mono/Camera/CameraProjectionCache.cs

        public static bool patchEnabled = true;
        public static long lastCachedFrame;

        private static TransformMatrix worldToClip;
        private static TransformMatrix clipToWorld;
        private static Matrix4x4 projectionMatrix;

        // Storing viewport info instead of using Rect properties grants us a few extra frames.
        public struct ViewportInfo
        {
            public double halfWidth;
            public double halfHeight;
            public double width;
            public double height;
            public double x;
            public double y;

            public ViewportInfo(Rect viewport)
            {
                width = viewport.width;
                height = viewport.height;
                halfWidth = width * 0.5;
                halfHeight = height * 0.5;
                x = viewport.x;
                y = viewport.y;
            }
        }

        private static ViewportInfo viewport;

        private static void UpdateCache()
        {
            lastCachedFrame = KSPCommunityFixes.UpdateCount;
            Camera camera = VectorLine.cam3D;

            viewport = new ViewportInfo(camera.pixelRect);
            projectionMatrix = camera.projectionMatrix;

            // WorldToClip.
            // Normally this would be a 4x4 matrix, but we omit the third row.
            // This is because users of WorldToScreen/Viewport expect world distance from the camera plane
            // (which is w after projection) in the z component, not z in NDC.
            // Therefore we never need to calculate the z component, only x, y and w.
            // w is the right value because m32 in the projection matrix just copies z in view space into w.

            Matrix4x4 worldToClip = projectionMatrix * camera.worldToCameraMatrix;
            VectorLineCameraProjection.worldToClip = new TransformMatrix(
                worldToClip.m00, worldToClip.m01, worldToClip.m02, worldToClip.m03,
                worldToClip.m10, worldToClip.m11, worldToClip.m12, worldToClip.m13,
                worldToClip.m30, worldToClip.m31, worldToClip.m32, worldToClip.m33);

            // ClipToWorld
            // Omit the fourth row because we don't need to figure out w when converting from clip space to world space.
            // We already have w and instead need to figure out the z component (see function).

            Matrix4x4 clipToWorld = worldToClip.inverse;
            VectorLineCameraProjection.clipToWorld = new TransformMatrix(
                clipToWorld.m00, clipToWorld.m01, clipToWorld.m02, clipToWorld.m03,
                clipToWorld.m10, clipToWorld.m11, clipToWorld.m12, clipToWorld.m13,
                clipToWorld.m20, clipToWorld.m21, clipToWorld.m22, clipToWorld.m23
            );
        }

        #region World to Clip

        public static Vector3 WorldToScreenPoint(Camera camera, Vector3 worldPosition)
        {
            // These patchEnabled checks are commented out atm in case they affect performance.
            // For testing they can be re-enabled and patchEnabled edited in UnityExplorer.

            //if (!patchEnabled)
            //    return camera.WorldToScreenPoint(worldPosition);

            if (lastCachedFrame != KSPCommunityFixes.UpdateCount)
                UpdateCache();

            double x = worldPosition.x;
            double y = worldPosition.y;
            double w = worldPosition.z;

            // z becomes w after this projection with our xyw matrix. Clip space z is never calculated.
            worldToClip.MutateMultiplyPoint3x4(ref x, ref y, ref w);

            // Perspective division and viewport conversion.
            double num = 0.5 / w;
            x = (0.5 + num * x) * viewport.width + viewport.x;
            y = (0.5 + num * y) * viewport.height + viewport.y;

            return new Vector3((float)x, (float)y, (float)w);
        }

        public static Vector3 WorldToViewportPoint(Camera camera, Vector3 worldPosition)
        {
            //if (!patchEnabled)
            //    return camera.WorldToViewportPoint(worldPosition);

            if (lastCachedFrame != KSPCommunityFixes.UpdateCount)
                UpdateCache();

            double x = worldPosition.x;
            double y = worldPosition.y;
            double w = worldPosition.z;

            // z becomes w after this projection with our xyw matrix. Clip space z is never calculated.
            worldToClip.MutateMultiplyPoint3x4(ref x, ref y, ref w);

            // Perspective division and viewport conversion.
            double num = 0.5 / w;
            x = 0.5 + num * x;
            y = 0.5 + num * y;

            return new Vector3((float)x, (float)y, (float)w);
        }

        #endregion

        #region Clip to World

        public static bool useScreenToWorldPoint = false;

        public static Vector3 ScreenToWorldPoint(Camera camera, Vector3 screenPosition)
        {
            //if (!patchEnabled)
            //    return camera.ScreenToWorldPoint(screenPosition);

            if (lastCachedFrame != KSPCommunityFixes.UpdateCount)
                UpdateCache();

            // Convert to individual doubles for speed.
            double x = screenPosition.x;
            double y = screenPosition.y;
            double w = screenPosition.z;

            // Convert from screen space to viewport space and undo perspective division.
            x = ((x - viewport.x) / viewport.halfWidth - 1) * w;
            y = ((y - viewport.y) / viewport.halfHeight - 1) * w;

            // w is the distance from the camera plane, but we need z in clip space so that we can use clipToWorld.
            // Making w negative gives us a coordinate in front of the camera in view space.
            // Then we do a tiny part of the normal projection matrix to get z in clip space.
            double z = projectionMatrix.m22 * (-w) + projectionMatrix.m23;

            double x1 = clipToWorld.m00 * x + clipToWorld.m01 * y + clipToWorld.m02 * z + clipToWorld.m03 * w;
            double y1 = clipToWorld.m10 * x + clipToWorld.m11 * y + clipToWorld.m12 * z + clipToWorld.m13 * w;
            double z1 = clipToWorld.m20 * x + clipToWorld.m21 * y + clipToWorld.m22 * z + clipToWorld.m23 * w;

            return new Vector3((float)x1, (float)y1, (float)z1);
        }

        public static Vector3 ViewportToWorldPoint(Camera camera, Vector3 position)
        {
            //if (!patchEnabled)
            //    return camera.ViewportToWorldPoint(position);

            //if (lastCachedFrame != KSPCommunityFixes.frameCount)
            //    UpdateCache();

            // Not used by VectorLine.
            throw new NotImplementedException();
        }

        #endregion
    }
}
