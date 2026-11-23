using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class PartBoundsIgnoreDisabledTransforms : BasePatch
    {
        const int TransparentFX = 1;

        protected override Version VersionMin => new(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(PartGeometryUtil), nameof(PartGeometryUtil.GetPartRendererBounds));
        }

        static readonly HashSet<string> disabledGOs = [];
        static readonly List<Bounds> bounds = [];
        static readonly List<Renderer> renderers = [];

        static bool PartGeometryUtil_GetPartRendererBounds_Prefix(Part p, out Bounds[] __result)
        {
            disabledGOs.Clear();
            using var bguard = new ListClearGuard<Bounds>(bounds);
            using var rguard = new ListClearGuard<Renderer>(renderers);

            // config defined ignores
            foreach (string ignore in p.partRendererBoundsIgnore)
                disabledGOs.Add(ignore);

            // stock variant switcher
            if (p.variants != null)
            {
                foreach (PartGameObjectInfo info in p.variants.SelectedVariant.InfoGameObjects)
                {
                    if (!info.Status)
                        disabledGOs.Add(info.Name);
                }
            }

            if (HighLogic.LoadedSceneIsEditor)
                GetPartRendererBoundsEditor(p);
            else
                GetPartRendererBoundsFlight(p);

            __result = [.. bounds];
            return false;
        }

        static void GetPartRendererBoundsFlight(Part p)
        {
            Transform modelTransform = GetPartModelTransform(p);
            if (modelTransform.IsNullOrDestroyed())
                return;

            GetRenderersRecursive(modelTransform);
        }

        static void GetRenderersRecursive(Transform parent)
        {
            GameObject go = parent.gameObject;

            if (!go.activeSelf)
                return;

            // Effect meshes (Waterfall, etc.) live on the TransparentFX layer.
            // See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/386
            if (go.layer == TransparentFX)
                return;

            if (disabledGOs.Contains(go.name))
                return;

            parent.GetComponents(renderers);

            foreach (Renderer r in renderers)
            {
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                    bounds.Add(r.bounds);
            }

            int count = parent.childCount;
            for (int i = 0; i < count; ++i)
                GetRenderersRecursive(parent.GetChild(i));
        }

        /// <summary>
        /// Same as <see cref="GetPartRendererBoundsFlight"/>, except that TransparentFX renderers
        /// are included and renderers come from the part's cached model renderer lists.
        /// </summary>
        /// <remarks>
        /// Layers can't be used in the editor because it moves all of a held part onto
        /// TransparentFX, then back to the default layer when the part is dropped.
        /// See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/410
        /// </remarks>
        static void GetPartRendererBoundsEditor(Part p)
        {
            renderers.AddRange(p.FindModelMeshRenderersReadOnly());
            renderers.AddRange(p.FindModelSkinnedMeshRenderersReadOnly());

            Transform partTransform = p.partTransform.IsNullRef() ? p.transform : p.partTransform;

            foreach (Renderer r in renderers)
            {
                if (IsRendererIncluded(r, partTransform))
                    bounds.Add(r.bounds);
            }
        }

        /// <summary>
        /// Returns false if the renderer's object, or any of its parents below
        /// <paramref name="partTransform"/>, is inactive or named in <see cref="disabledGOs"/>.
        /// </summary>
        static bool IsRendererIncluded(Renderer r, Transform partTransform)
        {
            Transform t = r.transform;
            while (t.IsNotNullOrDestroyed() && !ReferenceEquals(t, partTransform))
            {
                GameObject go = t.gameObject;
                if (!go.activeSelf)
                    return false;

                if (disabledGOs.Contains(go.name))
                    return false;

                t = t.parent;
            }

            return true;
        }

        static Transform GetPartModelTransform(Part part)
        {
            if (part.partTransform.IsNullOrDestroyed())
                return null;

            if (part.HasModuleImplementing<KerbalEVA>())
            {
                Transform result = part.partTransform.Find("model01");
                if (result.IsNotNullOrDestroyed())
                    return result;
            }

            if (part.HasModuleImplementing<ModuleAsteroid>())
            {
                Transform result = part.partTransform.Find("Asteroid");
                if (result.IsNotNullOrDestroyed())
                    return result;
            }

            if (part.HasModuleImplementing<ModuleComet>())
            {
                Transform result = part.partTransform.Find("Comet");
                if (result.IsNotNullOrDestroyed())
                    return result;
            }

            return part.partTransform.Find("model");
        }

        readonly struct ListClearGuard<T>(List<T> list) : IDisposable
        {
            public void Dispose() => list.Clear();
        }
    }
}
