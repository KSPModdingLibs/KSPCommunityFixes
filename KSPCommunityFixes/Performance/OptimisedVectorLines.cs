using HarmonyLib;
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

            // todo: there are more calls that could be patched but I don't know how often they are used.
        }

        static IEnumerable<CodeInstruction> VectorLine_Line3D_Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceWorldToScreenPoint(instructions, 2);

        static IEnumerable<CodeInstruction> VectorLine_BehindCamera_Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceWorldToViewportPoint(instructions, 2);

        static IEnumerable<CodeInstruction> VectorLine_IntersectAndDoSkip_Transpiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceWorldToScreenPoint(instructions, 2);

        static IEnumerable<CodeInstruction> VectorLine_Draw3D_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // todo: Two loops isn't optimal.

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

        private static IEnumerable<CodeInstruction> ReplaceScreenToViewportPoint(IEnumerable<CodeInstruction> instructions, int count)
        {
            MethodInfo Camera_ScreenToViewportPoint = AccessTools.Method(typeof(Camera), nameof(Camera.ScreenToViewportPoint), new Type[] { typeof(Vector3) });
            MethodInfo VectorLineOptimisation_ScreenToViewportPoint = AccessTools.Method(typeof(VectorLineCameraProjection), nameof(VectorLineCameraProjection.ScreenToViewportPoint));

            return ReplaceCall(instructions, Camera_ScreenToViewportPoint, VectorLineOptimisation_ScreenToViewportPoint, count);
        }
    }

    public static class VectorLineCameraProjection
    {
        // Based on CameraProjectionCache from UnityCsReference.
        // https://github.com/Unity-Technologies/UnityCsReference/blob/2019.4/Editor/Mono/Camera/CameraProjectionCache.cs

        public static bool patchEnabled = true;
        public static long lastCachedFrame;

        private static Matrix4x4 worldToClip;
        private static Matrix4x4 worldToClipInverse;

        // todo: remove.
        static Matrix4x4 m_worldToCameraInv;
        static Matrix4x4 m_projectionInv;

        // Storing viewport info instead of using Rect properties grants us a few extra frames.
        public struct ViewportInfo
        {
            public float halfWidth;
            public float halfHeight;
            public float width;
            public float height;
            public float x;
            public float y;

            public ViewportInfo(Rect viewport)
            {
                width = viewport.width;
                height = viewport.height;
                halfWidth = width * 0.5f;
                halfHeight = height * 0.5f;
                x = viewport.x;
                y = viewport.y;
            }
        }

        public static ViewportInfo viewport;

        public static Vector3 WorldToScreenPoint(Camera camera, Vector3 worldPosition)
        {
            // These patchEnabled checks are commented out atm in case they affect performance.
            // For testing they can be re-enabled and patchEnabled edited in UnityExplorer.

            //if (!patchEnabled)
            //    return camera.WorldToScreenPoint(worldPosition);

            if (lastCachedFrame != KSPCommunityFixes.frameCount)
                UpdateCache();

            Vector3 screen = WorldToClip(ref worldToClip, ref worldPosition);
            screen.x = viewport.x + (1.0f + screen.x) * viewport.halfWidth;
            screen.y = viewport.y + (1.0f + screen.y) * viewport.halfHeight;

            return screen;
        }

        public static Vector3 WorldToViewportPoint(Camera camera, Vector3 worldPosition)
        {
            //if (!patchEnabled)
            //    return camera.WorldToViewportPoint(worldPosition);

            if (lastCachedFrame != KSPCommunityFixes.frameCount)
                UpdateCache();

            Vector3 clip = WorldToClip(ref worldToClip, ref worldPosition);
            clip.x = (1.0f + clip.x) * 0.5f;
            clip.y = (1.0f + clip.y) * 0.5f;

            return clip;
        }


        public static Vector3 ScreenToWorldPoint(Camera camera, Vector3 screenPosition)
        {
            //if (!patchEnabled)
            //    return camera.ScreenToWorldPoint(screenPosition);

            if (lastCachedFrame != KSPCommunityFixes.frameCount)
                UpdateCache();

            // A two multiplication world to clip-space conversion, exactly in reverse.
            // TODO: I would like if this could be done with one multiplication like WorldToScreenPoint, but I don't understand it well enough.

            // Resources:
            // https://learnopengl.com/Getting-started/Coordinate-Systems
            // https://www.songho.ca/math/homogeneous/homogeneous.html
            // https://stackoverflow.com/questions/7692988/opengl-math-projecting-screen-space-to-world-space-coords

            // Examples:
            // https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Graphics/ViewportExtensions.cs#L46
            // https://github.com/rickomax/psxprev/blob/no-deps/Common/GeomMath.cs#L248
            // https://github.com/Prograda/Skybolt/blob/b9d4f410ecfe4e2ba1634971a0ecd017815192db/src/Skybolt/SkyboltQt/Viewport/ScreenTransformUtil.cpp#L36
            // https://github.com/GrognardsFromHell/OpenTemple/blob/master/Core/GFX/NumericsExtensions.cs#L40

            // It must replicate the behaviour of Camera.ScreenToWorldPoint exactly, including the third component of screenPosition being in world units.
            // https://docs.unity3d.com/ScriptReference/Camera.ScreenToWorldPoint.html

            // Normalized device coordinates.
            Vector4 clipSpace = new Vector4()
            {
                x = (screenPosition.x - m_Viewport.x) / m_Viewport.width * 2f - 1f,
                y = (screenPosition.y - m_Viewport.y) / m_Viewport.height * 2f - 1f,
                z = 1f,
                w = screenPosition.z,
            };

            // Perspective multiplication moves it to clip space.
            clipSpace.x *= clipSpace.w;
            clipSpace.y *= clipSpace.w;
            clipSpace.z *= clipSpace.w;

            Vector4 viewSpace = m_projectionInv * clipSpace;
            viewSpace.w = 1f;
            Vector4 worldSpace = m_worldToCameraInv * viewSpace;

            return worldSpace;
        }

        public static Vector3 ScreenToViewportPoint(Camera camera, Vector3 position)
        {
            //if (!patchEnabled)
            //    return camera.ScreenToViewportPoint(position);

            if (lastCachedFrame != KSPCommunityFixes.frameCount)
                UpdateCache();

            // Not used by VectorLine.

            return Vector3.zero;
        }

        private static void UpdateCache()
        {
            Camera camera = VectorLine.cam3D;

            worldToClip = camera.projectionMatrix * camera.worldToCameraMatrix;
            viewport = new ViewportInfo(camera.pixelRect);
            m_Viewport = camera.pixelRect;

            lastCachedFrame = KSPCommunityFixes.frameCount;
            m_worldToCameraInv = camera.worldToCameraMatrix.inverse;
            m_projectionInv = camera.projectionMatrix.inverse;

            cacheDirty = false;
        }

        private static Vector3 WorldToClip(Matrix4x4 m, Vector3 point)
        {
            // Skip z and use result.z as w.

            Vector3 result = default;
            result.x = m.m00 * point.x + m.m01 * point.y + m.m02 * point.z + m.m03;
            result.y = m.m10 * point.x + m.m11 * point.y + m.m12 * point.z + m.m13;
            result.z = m.m30 * point.x + m.m31 * point.y + m.m32 * point.z + m.m33;
            float num = 1f / result.z;
            result.x *= num;
            result.y *= num;
            return result;
        }
    }

    // todo: How to make this obey Settings.cfg?

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class VectorLineFrameCounter : MonoBehaviour
    {
        protected void Awake()
        {
            DontDestroyOnLoad(this);
        }

        protected void Update()
        {
            // I think this is faster than it would be to compare Time.frameCount to a cached frame count in every call to VectorLineCameraProjection.

            VectorLineCameraProjection.cacheDirty = true;
        }
    }
}
