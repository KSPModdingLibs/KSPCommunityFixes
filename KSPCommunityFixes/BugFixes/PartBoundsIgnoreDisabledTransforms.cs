using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class PartBoundsIgnoreDisabledTransforms : BasePatch
    {
        static PartBoundsIgnoreDisabledTransforms()
        {
            PartGeometryUtil.disabledVariantGOs = [];
        }

        protected override Version VersionMin => new(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(PartGeometryUtil), nameof(PartGeometryUtil.GetPartRendererBounds));
        }

        static readonly List<Renderer> partRenderersBuffer = [];

        static bool PartGeometryUtil_GetPartRendererBounds_Prefix(Part p, out Bounds[] __result)
        {
            PartGeometryUtil.disabledVariantGOs.Clear();

            // config defined ignores
            for (int i = p.partRendererBoundsIgnore.Count; i-- > 0;)
                PartGeometryUtil.disabledVariantGOs.Add(p.partRendererBoundsIgnore[i]);

            // stock variant switcher
            if (p.variants != null)
            {
                PartVariant selectedVariant = p.variants.SelectedVariant;
                for (int i = selectedVariant.InfoGameObjects.Count; i-- > 0;)
                {
                    PartGameObjectInfo info = selectedVariant.InfoGameObjects[i];
                    if (!info.Status)
                        PartGeometryUtil.disabledVariantGOs.Add(info.Name);
                }
            }

            Transform modelTransform = GetPartModelTransform(p);

            // Ignore renderers on the TransparentFX layer (layer 1).
            //
            // We can't do this unconditionally because when a part is detached in the editor KSP
            // temporarily moves all its renderers to layer 1. If we were then we'd get 0-sized
            // bounds. We instead take a cue from DragCubeGeneration and only filter out layer 1
            // when the part is not frozen.
            //
            // This matches the same rule as done in https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/150
            // which isn't really perfect, but is probably good enough for our purposes.
            bool ignoreTransparentFX = !p.frozen;

            try
            {
                GetRenderersRecursive(modelTransform, partRenderersBuffer, PartGeometryUtil.disabledVariantGOs, ignoreTransparentFX);

                __result = new Bounds[partRenderersBuffer.Count];
                for (int i = __result.Length; i-- > 0;)
                    __result[i] = partRenderersBuffer[i].bounds;
            }
            finally
            {
                partRenderersBuffer.Clear();
            }

            return false;
        }

        static readonly List<Renderer> rendererBuffer = [];

        static void GetRenderersRecursive(Transform parent, List<Renderer> renderers, List<string> excludedGOs, bool ignoreTransparentFX)
        {
            // note : this will result in a slight change in behavior vs stock
            // as this will exclude childs as well, wereas stock will still include them.
            // But stock behavior could be classified as a bug...
            if (excludedGOs.Contains(parent.name))
                return;

            if (ignoreTransparentFX && parent.gameObject.layer == 1)
                return;

            try
            {
                parent.GetComponents(rendererBuffer);
                for (int i = rendererBuffer.Count; i-- > 0;)
                {
                    Renderer r = rendererBuffer[i];
                    Type rType = r.GetType();
                    if (rType == typeof(MeshRenderer) || rType == typeof(SkinnedMeshRenderer))
                        renderers.Add(r);
                }
            }
            finally
            {
                rendererBuffer.Clear();
            }

            for (int i = parent.childCount; i-- > 0;)
            {
                Transform child = parent.GetChild(i);
                if (child.gameObject.activeSelf)
                    GetRenderersRecursive(child, renderers, excludedGOs, ignoreTransparentFX);
            }
        }

        static Transform GetPartModelTransform(Part part)
        {
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
    }
}
