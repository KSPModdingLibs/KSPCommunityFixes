// Check the results of the replacement camera projection functions against Unity's own for correctness.
//#define DEBUG_CAMERAPROJECTION

using HarmonyLib;
using KSPCommunityFixes.Library;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Vectrosity;

#if DEBUG_CAMERAPROJECTION
using System.IO;
using System.Text;
#endif

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
        private static TransformMatrix screenToWorld; // Not a normal transformation matrix, do not use as such.
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

            // ScreenToWorld (NB: not ClipToWorld).
            // Credit to Charles Rogers for this incredible solution.
            // It takes screen space coordinates (x, y, w) and converts them directly to world space
            // without any intermediate steps. It's at least 6x faster than Unity's own ScreenToWorldPoint.

            BuildScreenToWorldMatrix(out screenToWorld, in worldToClip);
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

#if !DEBUG_CAMERAPROJECTION
            return new Vector3((float)x, (float)y, (float)w);
#else
            Vector3 result = new Vector3((float)x, (float)y, (float)w);
            CheckResult(nameof(WorldToScreenPoint), in worldPosition, camera.WorldToScreenPoint(worldPosition), in result);
            return result;
#endif
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

#if !DEBUG_CAMERAPROJECTION
            return new Vector3((float)x, (float)y, (float)w);
#else
            Vector3 result = new Vector3((float)x, (float)y, (float)w);
            CheckResult(nameof(WorldToViewportPoint), in worldPosition, camera.WorldToViewportPoint(worldPosition), in result);
            return result;
#endif
        }

        #endregion

        #region Clip to World

        private static void BuildScreenToWorldMatrix(out TransformMatrix matrix, in Matrix4x4 worldToClip)
        {
            double xScaled = viewport.x / viewport.halfWidth;
            double yScaled = viewport.y / viewport.halfHeight;

            Matrix4x4 clipToWorld = worldToClip.inverse;
            matrix.m00 = clipToWorld.m00 / viewport.halfWidth;
            matrix.m01 = clipToWorld.m01 / viewport.halfHeight;
            matrix.m02 = -clipToWorld.m00 * xScaled - clipToWorld.m01 * yScaled - clipToWorld.m02 * projectionMatrix.m22 + clipToWorld.m03 - clipToWorld.m00 - clipToWorld.m01;
            matrix.m03 = clipToWorld.m02 * projectionMatrix.m23;
            matrix.m10 = clipToWorld.m10 / viewport.halfWidth;
            matrix.m11 = clipToWorld.m11 / viewport.halfHeight;
            matrix.m12 = -clipToWorld.m10 * xScaled - clipToWorld.m11 * yScaled - clipToWorld.m12 * projectionMatrix.m22 + clipToWorld.m13 - clipToWorld.m10 - clipToWorld.m11;
            matrix.m13 = clipToWorld.m12 * projectionMatrix.m23;
            matrix.m20 = clipToWorld.m20 / viewport.halfWidth;
            matrix.m21 = clipToWorld.m21 / viewport.halfHeight;
            matrix.m22 = -clipToWorld.m20 * xScaled - clipToWorld.m21 * yScaled - clipToWorld.m22 * projectionMatrix.m22 + clipToWorld.m23 - clipToWorld.m20 - clipToWorld.m21;
            matrix.m23 = clipToWorld.m22 * projectionMatrix.m23;
        }

        public static Vector3 ScreenToWorldPoint(Camera camera, Vector3 screenPosition)
        {
            //if (!patchEnabled)
            //    return camera.ScreenToWorldPoint(screenPosition);

            if (lastCachedFrame != KSPCommunityFixes.UpdateCount)
                UpdateCache();

            double x = screenPosition.x;
            double y = screenPosition.y;
            double z = screenPosition.z;

            // NB: not a normal matrix multiplication.
            double x1 = (screenToWorld.m00 * x + screenToWorld.m01 * y + screenToWorld.m02) * z + screenToWorld.m03;
            double y1 = (screenToWorld.m10 * x + screenToWorld.m11 * y + screenToWorld.m12) * z + screenToWorld.m13;
            double z1 = (screenToWorld.m20 * x + screenToWorld.m21 * y + screenToWorld.m22) * z + screenToWorld.m23;

#if !DEBUG_CAMERAPROJECTION
            return new Vector3((float)x1, (float)y1, (float)z1);
#else
            Vector3 result = new Vector3((float)x1, (float)y1, (float)z1);
            CheckResult(nameof(ScreenToWorldPoint), in screenPosition, camera.ScreenToWorldPoint(screenPosition), in result);
            return result;
#endif
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

#if DEBUG_CAMERAPROJECTION
        #region Debugging

        // Debugging settings. Edit these values in-game with UnityExplorerKSP.
        public static int maxUlps = 1000;
        public static int maxLogsPerFrame = 2; // Logging many hundreds or thousands of times per frame will freeze the game.
        public static LogLevel logLevel = LogLevel.All;

        public enum LogLevel
        {
            None,
            Summary,
            All
        }

        private static long lastFrameDebug;
        private static long logsThisFrame;
        private static long totalDiscrepanciesThisFrame;
        private static long totalCorrectThisFrame;
        private static Dictionary<string, int> logCount = new Dictionary<string, int>();
        private static StringBuilder sb = new StringBuilder();

        private static Dictionary<int, FrameDebugData> csv = new Dictionary<int, FrameDebugData>();
        private static bool takingCSV;
        private static int csvFrameCount;
        private static int framesPerUlps;
        private static int[] ulpLevels;

        private struct FrameDebugData
        {
            public int count;
            public float averageCorrect;
            public float averageIncorrect;
        }

        private static void CheckResult(string functionName, in Vector3 input, Vector3 expected, in Vector3 result)
        {
            // Exit early if we are not logging.
            if (logLevel == LogLevel.None && !takingCSV)
                return;

            // Once per frame that one of the functions is used.
            if (lastFrameDebug != KSPCommunityFixes.UpdateCount)
            {
                // Summary is logged regardless of whether there are any discrepancies.
                if (logLevel == LogLevel.Summary)
                {
                    sb.Clear();
                    sb.AppendLine($"[KSPCF/OptimisedVectorLines]: Camera projection result mismatches detected:");
                    foreach (var kvp in logCount)
                        sb.AppendLine($"{kvp.Key}: {kvp.Value}");

                    sb.AppendLine($"Total: {totalDiscrepanciesThisFrame} mismatches, {totalCorrectThisFrame} correct results this frame ({totalCorrectThisFrame / (float)(totalCorrectThisFrame + totalDiscrepanciesThisFrame) * 100:N0}%).");

                    Debug.Log(sb.ToString());
                    logCount.Clear();
                }

                if (takingCSV)
                {
                    maxUlps = ulpLevels[Mathf.FloorToInt(csvFrameCount / (float)framesPerUlps)];
                    csvFrameCount++;

                    AddToCSV();

                    if (csvFrameCount >= framesPerUlps * ulpLevels.Length)
                        FinishCSV();
                }

                lastFrameDebug = KSPCommunityFixes.UpdateCount;
                logsThisFrame = 0;
                totalCorrectThisFrame = 0;
                totalDiscrepanciesThisFrame = 0;
            }

            // Do the actual comparison.
            if (Numerics.AlmostEqual(result.x, expected.x, maxUlps)
                && Numerics.AlmostEqual(result.y, expected.y, maxUlps)
                && Numerics.AlmostEqual(result.z, expected.z, maxUlps))
            {
                totalCorrectThisFrame++;

                return;
            }

            totalDiscrepanciesThisFrame++;

            // Only log that there was a mismatch on this function if it's only a summary.
            if (logLevel == LogLevel.Summary)
            {
                logCount[functionName] = logCount.GetValueOrDefault(functionName) + 1;
                return;
            }
            else if (logLevel == LogLevel.All)
            {
                // Log the details of the mismatch, only if the maxLogsPerFrame is not exceeded.
                if (logsThisFrame >= maxLogsPerFrame)
                    return;

                logsThisFrame++;
                sb.Clear();
                sb.AppendLine($"[KSPCF/OptimisedVectorLines]: Camera projection result mismatch in {functionName} on frame {KSPCommunityFixes.UpdateCount}.");
                sb.AppendLine($"Input: ({input.x:G9}, {input.y:G9}, {input.z:G9})");
                sb.AppendLine($"Expected: ({expected.x:G9}, {expected.y:G9}, {expected.z:G9})");
                sb.AppendLine($"Result: ({result.x:G9}, {result.y:G9}, {result.z:G9})");
                Debug.LogWarning(sb.ToString());
            }
        }

        // Call from the UnityExplorer console to start taking a CSV.
        public static void StartCSV(int framesPerUlps, params int[] ulpLevels)
        {
            if (takingCSV)
                return;

            csv.Clear();
            takingCSV = true;
            csvFrameCount = 0;

            VectorLineCameraProjection.framesPerUlps = framesPerUlps;
            VectorLineCameraProjection.ulpLevels = ulpLevels;
        }

        private static void AddToCSV()
        {
            if (csv.TryGetValue(maxUlps, out FrameDebugData data))
            {
                data.count++;
                data.averageCorrect += (totalCorrectThisFrame - data.averageCorrect) / data.count;
                data.averageIncorrect += (totalDiscrepanciesThisFrame - data.averageIncorrect) / data.count;

                csv[maxUlps] = data;
            }
            else
            {
                data = new FrameDebugData
                {
                    count = 1,
                    averageCorrect = totalCorrectThisFrame,
                    averageIncorrect = totalDiscrepanciesThisFrame
                };
                csv.Add(maxUlps, data);
            }
        }

        private static void FinishCSV()
        {
            if (!takingCSV)
                return;

            takingCSV = false;

            string dateTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(KSPCommunityFixes.ModPath, $"KSPCF_CameraProjection_{dateTime}.csv");

            sb.Clear();
            sb.AppendLine("maxUlps,averageCorrect,averageIncorrect");
            foreach (var kvp in csv)
                sb.AppendLine($"{kvp.Key},{kvp.Value.averageCorrect},{kvp.Value.averageIncorrect}");

            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[KSPCF/OptimisedVectorLines]: CSV saved to {path}.");
            csv.Clear();
            sb.Clear();
        }

        #endregion
#endif
    }
}
