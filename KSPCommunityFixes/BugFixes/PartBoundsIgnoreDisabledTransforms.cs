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
            PartGeometryUtil.disabledVariantGOs = new List<string>();
        }

        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartGeometryUtil), nameof(PartGeometryUtil.GetPartRendererBounds))));
        }

        static readonly List<Renderer> partRenderersBuffer = new List<Renderer>();

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

            try 
            {
                GetRenderersRecursive(modelTransform, partRenderersBuffer, PartGeometryUtil.disabledVariantGOs);

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

        static readonly List<Renderer> rendererBuffer = new List<Renderer>();

        static void GetRenderersRecursive(Transform parent, List<Renderer> renderers, List<string> excludedGOs)
        {
            // note : this will result in a slight change in behavior vs stock
            // as this will exclude childs as well, wereas stock will still include them.
            // But stock behavior could be classified as a bug...
            if (excludedGOs.Contains(parent.name))
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
                    GetRenderersRecursive(child, renderers, excludedGOs);
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
