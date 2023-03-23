using HarmonyLib;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Version = System.Version;

namespace KSPCommunityFixes.BugFixes
{
    internal class CorrectDragForFlags : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(FlagDecalBackground), nameof(FlagDecalBackground.EnableCurrentFlagMesh)),
                this));
        }

        static void FlagDecalBackground_EnableCurrentFlagMesh_Postfix(FlagDecalBackground __instance)
        {
            if (__instance.flagMeshes == null)
                return;

            if (__instance.flagSizeOffset == 1000 && TryGetDragCube(__instance, out DragCube dragCube))
            {
                if (__instance.part.dragCubes.cubes.Count > 0)
                    __instance.part.dragCubes.cubes[0] = dragCube;
                else
                    __instance.part.dragCubes.cubes.Add(dragCube); 
            }
        }

        static readonly Dictionary<Part, Dictionary<int, DragCube>> flagDecalBackgroundDragCubesPerPrefab = new Dictionary<Part, Dictionary<int, DragCube>>(5);

        public static bool TryGetDragCube(FlagDecalBackground instance, out DragCube dragCube)
        {
            Part prefab = instance.part.partInfo?.partPrefab;
            if (prefab.IsNullOrDestroyed())
            {
                dragCube = null;
                return false;
            }

            if (!flagDecalBackgroundDragCubesPerPrefab.TryGetValue(prefab, out Dictionary<int, DragCube> dragCubes))
            {
                dragCubes = new Dictionary<int, DragCube>();
                flagDecalBackgroundDragCubesPerPrefab[prefab] = dragCubes;
            }

            int id = instance.flagSize;
            if (instance.displayingPortrait)
                id += 1000;

            if (!dragCubes.TryGetValue(id, out dragCube))
            {
                dragCube = RenderDragCubeFast(instance.part);
                dragCubes[id] = dragCube;
            }

            return true;
        }

        static Dictionary<Shader, bool> shaderHasAlphaDict = new Dictionary<Shader, bool>();

        static bool MaterialShaderHasAlpha(Material material)
        {
            if (!shaderHasAlphaDict.TryGetValue(material.shader, out bool hasAlpha))
            {
                hasAlpha = material.shader.name.Contains("Alpha");
                shaderHasAlphaDict[material.shader] = hasAlpha;
            }

            return hasAlpha;
        }

        private static readonly List<Component> staticComponentBuffer = new List<Component>(100);

        static DragCube RenderDragCubeFast(Part part)
        {
            DragCubeSystem dragCubeSystem = DragCubeSystem.Instance;

            DragCube dragCube = new DragCube();

            Part dragPart = Object.Instantiate(part, Vector3.zero, Quaternion.identity);
            GameObject dragObject = dragPart.gameObject;
            dragPart.enabled = false;
            dragPart.SetMirror(Vector3.one);
            dragObject.SetActive(true);

            for (int i = 0; i < dragPart.children.Count; i++)
                if (dragPart.children[i].partTransform.parent == dragPart.partTransform)
                    Object.DestroyImmediate(dragPart.children[i].gameObject);

            staticComponentBuffer.Clear();
            dragObject.GetComponentsInChildren(true, staticComponentBuffer);

            int cameraLayer = dragCubeSystem.cameraLayerInt;
            Bounds partBounds = default;

            int mainTexId = PropertyIDs._MainTex;
            int bumpMapId = PropertyIDs._BumpMap;

            for (int i = staticComponentBuffer.Count; i-- > 0;)
            {
                Component component = staticComponentBuffer[i];

                if (component is MonoBehaviour monoBehaviour)
                {
                    if (monoBehaviour is FXPrefab)
                    {
                        Object.DestroyImmediate(monoBehaviour);
                        continue;
                    }

                    monoBehaviour.enabled = false;
                    continue;
                }

                if (component is ParticleSystem)
                {
                    Object.DestroyImmediate(component);
                    continue;
                }

                if (component is Collider collider)
                {
                    collider.enabled = false;
                    continue;
                }

                if (component is Transform)
                {
                    GameObject gameObject = component.gameObject;
                    if (((1 << gameObject.layer) & 2) == 0)
                        gameObject.layer = cameraLayer;
                    continue;
                }

                if (component is Renderer renderer)
                {
                    GameObject rendererGameObject = renderer.gameObject;
                    if (renderer is ParticleSystemRenderer || !rendererGameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    if (rendererGameObject.CompareTag("Drag_Hidden"))
                    {
                        Object.DestroyImmediate(renderer);
                        continue;
                    }

                    partBounds.Encapsulate(renderer.bounds);

                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    Material[] materials = renderer.materials;

                    for (int j = materials.Length; j-- > 0;)
                    {
                        Material material = materials[j];
                        Material dragMaterial;
                        if (material.HasProperty(PropertyIDs._BumpMap))
                            dragMaterial = new Material(dragCubeSystem.dragShaderBumped);
                        else
                            dragMaterial = new Material(dragCubeSystem.dragShader);

                        if (MaterialShaderHasAlpha(material) && material.HasProperty(mainTexId))
                        {
                            dragMaterial.SetTexture(mainTexId, material.GetTexture(mainTexId));
                            dragMaterial.SetTextureOffset(mainTexId, material.GetTextureOffset(mainTexId));
                            dragMaterial.SetTextureScale(mainTexId, material.GetTextureScale(mainTexId));
                        }
                        if (material.HasProperty(bumpMapId))
                        {
                            dragMaterial.SetTexture(bumpMapId, material.GetTexture(bumpMapId));
                            dragMaterial.SetTextureOffset(bumpMapId, material.GetTextureOffset(bumpMapId));
                            dragMaterial.SetTextureScale(bumpMapId, material.GetTextureScale(bumpMapId));
                        }
                        materials[j] = dragMaterial;
                    }
                    renderer.materials = materials;
                }
            }

            staticComponentBuffer.Clear();

            for (int i = 0; i < 6; i++)
            {
                dragCubeSystem.SetAeroCamera((DragCube.DragFace)i, partBounds);
                dragCubeSystem.UpdateAeroTexture();
                CalculateAerodynamicsFast(out float area, out float drag, out float depth);
                dragCube.Area[i] = area;
                dragCube.Drag[i] = drag;
                dragCube.Depth[i] = depth;
            }
            dragCube.Center = partBounds.center;
            dragCube.Size = partBounds.size;

            dragObject.SetActive(value: false);
            Object.Destroy(dragObject);

            return dragCube;
        }

        private static float[] _dragTable;

        private static float[] GetDragTable()
        {
            if (_dragTable == null)
            {
                _dragTable = new float[256];
                AnimationCurve dragCurve = DragCubeSystem.Instance.dragCurve;
                for (int i = 0; i < 256; i++)
                    _dragTable[i] = dragCurve.Evaluate(i / 255f);
            }
            return _dragTable;
        }

        private static void CalculateAerodynamicsFast(out float area, out float drag, out float depth)
        {
            DragCubeSystem dragCubeSystem = DragCubeSystem.Instance;
            float[] dragTable = GetDragTable();

            // ARGB32 texture
            NativeArray<byte> pixels = dragCubeSystem.aeroTexture.GetRawTextureData<byte>();

            Camera cam = dragCubeSystem.aeroCamera;
            float lerpConstant = cam.nearClipPlane + (cam.farClipPlane - cam.nearClipPlane);

            drag = 0f;
            depth = 0f;
            area = 0f;

            int hits = 0;
            int length = pixels.Length;
            int i = 0;
            while (i < length)
            {
                if (pixels[i] != 0)
                {
                    hits++;

                    byte r = pixels[i + 1];
                    if (r > 0)
                    {
                        drag += dragTable[r];
                    }

                    byte g = pixels[i + 2];
                    if (g > 0)
                    {
                        float pixelDepth = lerpConstant * (g / 255f);
                        depth = Math.Max(depth, pixelDepth);
                    }
                }
                i += 4;
            }

            if (hits > 0)
            {
                float pixelArea = Mathf.Pow(2f * dragCubeSystem.aeroCameraSize / dragCubeSystem.resolution, 2f);
                area = pixelArea * hits;
                drag /= hits;
            }
        }
    }
}
