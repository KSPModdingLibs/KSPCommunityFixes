using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    // Unity 2019.4 crashes in CullCastersCommon when it renders a camera whose world position or
    // view matrix translation are NaN or infinite with a directional light casting shadows.
    // The culling plane list is empty but is handled as if it's full, leading to reading memory
    // that hasn't been initialized.
    // This patch runs before a camera render, checking for NaNs, and disabling directional shadows
    // if one is found, then re-enabling after, and logging the problem for later troubleshooting.
    internal class NaNCameraShadowCrash : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches() { }

        protected override void OnPatchApplied()
        {
            Camera.onPreCull += OnPreCull;
            Camera.onPostRender += OnPostRender;
        }

        private static Camera guardedCamera;
        private static int guardedFrame;
        private static readonly List<Light> suppressedLights = new List<Light>();
        private static readonly List<LightShadows> suppressedShadows = new List<LightShadows>();
        private static readonly HashSet<string> loggedCameras = new HashSet<string>();

        private static void OnPreCull(Camera camera)
        {
            // Restore in case the guarded camera's post render never came, so shadows can't stay off.
            if (guardedCamera.IsNotNullRef() && (guardedCamera.IsNullOrDestroyed() || guardedFrame != Time.frameCount))
                RestoreShadows();

            // A render nested inside a guarded one already has shadows off.
            if (guardedCamera.IsNotNullRef())
                return;

            Vector3 position = camera.transform.position;
            Matrix4x4 view = camera.worldToCameraMatrix;
            if (IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z)
                && IsFinite(view.m03) && IsFinite(view.m13) && IsFinite(view.m23))
                return;

            foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (light.type != LightType.Directional || light.shadows == LightShadows.None || !light.isActiveAndEnabled)
                    continue;

                suppressedLights.Add(light);
                suppressedShadows.Add(light.shadows);
                light.shadows = LightShadows.None;
            }

            guardedCamera = camera;
            guardedFrame = Time.frameCount;

            if (loggedCameras.Add(camera.name))
                Debug.LogWarning($"[KSPCF:NaNCameraShadowCrash] Camera '{GetPath(camera.transform)}' is rendering from position {position} " +
                                 $"with view matrix translation ({view.m03}, {view.m13}, {view.m23}). Disabling directional light shadows " +
                                 $"for its renders to avoid a crash in Unity's shadow culling.");
        }

        private static void OnPostRender(Camera camera)
        {
            if (ReferenceEquals(camera, guardedCamera))
                RestoreShadows();
        }

        private static void RestoreShadows()
        {
            for (int i = 0; i < suppressedLights.Count; i++)
                if (suppressedLights[i].IsNotNullOrDestroyed())
                    suppressedLights[i].shadows = suppressedShadows[i];

            suppressedLights.Clear();
            suppressedShadows.Clear();
            guardedCamera = null;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static string GetPath(Transform transform)
        {
            StringBuilder sb = new StringBuilder(transform.name);
            for (Transform parent = transform.parent; parent.IsNotNullRef(); parent = parent.parent)
                sb.Insert(0, '/').Insert(0, parent.name);
            return sb.ToString();
        }
    }
}
