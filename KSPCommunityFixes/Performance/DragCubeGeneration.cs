﻿/*
There are 3 (public) entry points to the drag cube generation :
- DragCubeSystem.RenderProceduralDragCube(Part)
    - Immediate call, triggered the part DragCubeList.SetDragWeights(), 
      itself called by the FlightIntegrator FixedUpdate
    - The call is rate limited (see DragCubeList.SetDragWeights_Procedural()) to 1 per second
      The way this is implemented, all parts using procedural drag cubes will get updated in the same frame
    - Ignores all drag cube modifiers like IMultipleDragCube, ModuleDragModifier or ModuleDragAreaModifier
    - In stock, it is used by ModuleProceduralFairing, (optionally) by ModuleJettison, (optionally) by the 
      asteroid/comet modules and (optionally) by ModulePartVariants
- DragCubeSystem.SetupDragCubeCoroutine(Part) is a coroutine. It is called during loading at part compilation,
    - It will generate multiple drag cubes if a module implementing IMultipleDragCube is found on the part
    - IMultipleDragCube.AssumeDragCubePosition is called on the copy of the part, that method being (usually)
      used to set an animation to a specfic state. This require waiting for the next frame after calling that
      method, as animations aren't propagated to meshes state immediately.
- DragCubeSystem.SetupDragCubeCoroutine(Part, ConfigNode) is a variant of the above. It is unused is stock, 
  but could potentially be used by mods.


So the plan is to patch those 3 entry points.
- DragCubeSystem.RenderProceduralDragCube(Part) can be patched to do a direct render of the current part,
  without having to instantiate a copy. Additionally, it would be interesting to revisit the rate limiting
  logic to ensure individual calls are spread out on different frames
- DragCubeSystem.SetupDragCubeCoroutine(Part) (and its variant) coudl be patched using two implementations :
    - If no IMultipleDragCube is found on the part (or if it define a procedural drag cube), we can also
      do a direct render of the current part without instantiating a copy
    - If the part uses a IMultipleDragCube module, we can't escape instantiating a new part. When doing so,
      aside from optimizing the instantiated part setup, we can avoid instantiating the whole child parts tree
      in the editor by temporarily detaching the direct childs before instantiating, and re-parenting them
      immediately.
*/

using HarmonyLib;
using KSP.UI;
using KSP.UI.Screens.DebugToolbar.Screens.Physics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using KSPCommunityFixes.Library;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace KSPCommunityFixes.Performance
{
    public class DragCubeGeneration : BasePatch
    {
        protected override void ApplyPatches()
        {
            dragMaterial = new Material(DragCubeSystem.Instance.dragShader);
            defaultCubeNameArray = new[] { defaultCubeName };

            childTransformsBuffer = new List<Transform>(20);
            staticComponentBuffer = new List<Component>(20);

            rendererListPool = new ListPool<Renderer>();
            multipleDragCubeListPool = new ListPool<IMultipleDragCube>();
            moduleDragModifierListPool = new ListPool<ModuleDragModifier>();
            moduleDragAreaModifierListPool = new ListPool<ModuleDragAreaModifier>();
            dragRendererInfoListPool = new ListPool<DragRendererInfo>();
            commandBufferPool = new ObjectPool<CommandBuffer>(buffer => buffer.name = "KSPCFDragCubeRenderer", buffer => buffer.Clear());

            FieldInfo f_Canvas_willRenderCanvases = AccessTools.Field(typeof(Canvas), "willRenderCanvases");
            fieldRef_Canvas_WillRenderCanvases = AccessTools.FieldRefAccess<object, Canvas.WillRenderCanvases>(f_Canvas_willRenderCanvases);

            AddPatch(PatchType.Prefix, typeof(DragCubeSystem), nameof(DragCubeSystem.RenderProceduralDragCube));

            AddPatch(PatchType.Transpiler, typeof(DragCubeSystem), nameof(DragCubeSystem.SetupDragCubeCoroutine), new[] {typeof(Part)}, nameof(DragCubeSystem_SetupDragCubeCoroutine_MoveNextTranspiler));

            AddPatch(PatchType.Transpiler, typeof(DragCubeSystem), nameof(DragCubeSystem.SetupDragCubeCoroutine), new[] {typeof(Part), typeof(ConfigNode)}, nameof(DragCubeSystem_SetupDragCubeCoroutine_MoveNextTranspiler));

            AddPatch(PatchType.Prefix, typeof(ScreenPhysics), nameof(ScreenPhysics.Start));
        }

        private static bool DragCubeSystem_RenderProceduralDragCube_Prefix(Part p, out DragCube __result)
        {
            __result = RenderDragCubeImmediate(p, string.Empty);
            return false;
        }

        private static IEnumerable<CodeInstruction> DragCubeSystem_SetupDragCubeCoroutine_MoveNextTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_DragCubeSystem_getInstance = AccessTools.PropertyGetter(typeof(DragCubeSystem), nameof(DragCubeSystem.Instance));
            MethodInfo m_DragCubeSystem_RenderDragCubesCoroutine = AccessTools.Method(typeof(DragCubeSystem), nameof(DragCubeSystem.RenderDragCubesCoroutine));
            MethodInfo m_DragCubeGeneration_RenderDragCubesCoroutine = AccessTools.Method(typeof(DragCubeGeneration), nameof(DragCubeGeneration.RenderDragCubesCoroutine));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];
                if (instruction.opcode == OpCodes.Callvirt && ReferenceEquals(instruction.operand, m_DragCubeSystem_RenderDragCubesCoroutine))
                {
                    for (int j = i - 1; j > i - 15; j--)
                    {
                        CodeInstruction prev = code[j];
                        if (prev.opcode == OpCodes.Call && ReferenceEquals(prev.operand, m_DragCubeSystem_getInstance))
                        {
                            prev.opcode = OpCodes.Nop;
                            prev.operand = null;
                            instruction.operand = m_DragCubeGeneration_RenderDragCubesCoroutine;
                        }
                    }
                }
            }

            return code;
        }

        private static void ScreenPhysics_Start_Prefix(ScreenPhysics __instance)
        {
            GameObject buttonObject = Object.Instantiate(__instance.saveButton.gameObject, __instance.transform);
            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(DragCubeDebugger.Open);
            TextMeshProUGUI text = buttonObject.GetComponentInChildren<TextMeshProUGUI>();
            text.text = "Open KSPCF drag cube debugger";
        }

        internal const int textureSize = 256;
        private const float cameraOffset = 0.1f;
        private const float cameraDoubleOffset = cameraOffset * 2f;
        public const string defaultCubeName = "Default";
        public static string[] defaultCubeNameArray = { defaultCubeName };

        private static AccessTools.FieldRef<object, Canvas.WillRenderCanvases> fieldRef_Canvas_WillRenderCanvases;

        private static Material dragMaterial; // static instance is ok (created once and never modified)
        private static List<Transform> childTransformsBuffer; // static buffer is ok (used in an atomic operation)
        private static List<Component> staticComponentBuffer; // static buffer is ok (used in an atomic operation)

        // these objects are used in the coroutine over the span of multiple frames, meaning we can't use a single static
        // instance, each coroutine has to to get its own instance. So instead we use object pools to avoid allocations.
        // see issue #146 : https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/146
        private static ListPool<Renderer> rendererListPool = new ListPool<Renderer>();
        private static ListPool<IMultipleDragCube> multipleDragCubeListPool = new ListPool<IMultipleDragCube>();
        private static ListPool<ModuleDragModifier> moduleDragModifierListPool = new ListPool<ModuleDragModifier>();
        private static ListPool<ModuleDragAreaModifier> moduleDragAreaModifierListPool = new ListPool<ModuleDragAreaModifier>();
        private static ListPool<DragRendererInfo> dragRendererInfoListPool = new ListPool<DragRendererInfo>();
        private static ObjectPool<CommandBuffer> commandBufferPool = new ObjectPool<CommandBuffer>();

        private struct DragRendererInfo
        {
            public Renderer renderer;
            public ShadowCastingMode shadowCastingMode;
            public bool receiveShadow;

            public DragRendererInfo(Renderer renderer)
            {
                this.renderer = renderer;
                shadowCastingMode = renderer.shadowCastingMode;
                receiveShadow = renderer.receiveShadows;
            }

            public void RestoreRendererState()
            {
                renderer.shadowCastingMode = shadowCastingMode;
                renderer.receiveShadows = receiveShadow;
            }
        }

        public static IEnumerator RenderDragCubesCoroutine(Part part, ConfigNode dragConfig)
        {
            float dragModifier = 1f;
            float areaModifier = 1f;
            List<PartModule> modules = part.modules.modules;
            for (int i = modules.Count; i-- > 0;)
            {
                PartModule pm = modules[i];
                if (pm is IMultipleDragCube iMultipleDragCube && iMultipleDragCube.IsMultipleCubesActive)
                {
                    if (iMultipleDragCube.UsesProceduralDragCubes())
                    {
                        Debug.Log($"[KSPCF/DragCubeGeneration] Part '{part.partInfo.name}' has defined a procedural drag cube setup");
                        dragConfig.AddValue("procedural", "True");
                        yield break;
                    }

                    Debug.Log($"[KSPCF/DragCubeGeneration] Creating drag cubes for part '{part.partInfo.name}'");
                    if (KSPCFFastLoader.PartCompilationInProgress)
                        yield return RenderDragCubesOnCopy(part, part.dragCubes, dragConfig);
                    else
                        yield return DragCubeSystem.Instance.StartCoroutine(RenderDragCubesOnCopy(part, part.dragCubes, dragConfig));

                    yield return null;
                    yield break;
                }

                if (pm is ModuleDragModifier moduleDragMod && moduleDragMod.dragCubeName == defaultCubeName)
                {
                    dragModifier = moduleDragMod.dragModifier;
                }
                else if (pm is ModuleDragAreaModifier moduleAreaMod && moduleAreaMod.dragCubeName == defaultCubeName)
                {
                    areaModifier = moduleAreaMod.areaModifier;
                }
            }

            Debug.Log($"[KSPCF/DragCubeGeneration] Creating drag cubes for part '{part.partInfo.name}'");
            DragCube dragCube = RenderDragCubeImmediate(part, defaultCubeName);
            for (int i = 6; i-- > 0;)
            {
                dragCube.drag[i] *= dragModifier;
                dragCube.area[i] *= areaModifier;
            }
            part.DragCubes.Cubes.Add(dragCube);
            dragConfig.AddValue("cube", dragCube.SaveToString());
            yield return null;
        }

        // reimplementation of DragCubeSystem.Instance.RenderDragCubesCoroutine(originalPart, dragConfig)
        public static IEnumerator RenderDragCubesOnCopy(Part originalPart, DragCubeList dragCubeList, ConfigNode dragConfig)
        {
            if (originalPart.frozen)
                Debug.LogWarning($"[KSPCF/DragCubeGeneration] Generating drag cubes on detached part ({originalPart.partInfo.name}) in the editor isn't supported and will give incorrect results.");

            int childCount = originalPart.children.Count;
            if (childCount > 0)
            {
                for (int i = childCount; i-- > 0;)
                {
                    Transform childTransform = originalPart.children[i].transform;
                    if (childTransform.parent.IsNullRef())
                        continue;

                    childTransform.SetParent(null, true);
                    childTransformsBuffer.Add(childTransform);
                }
            }

            Part part;

            try
            {
                part = Object.Instantiate(originalPart, Vector3.zero, Quaternion.identity);
            }
            finally
            {
                for (int i = childTransformsBuffer.Count; i-- > 0;)
                    childTransformsBuffer[i].SetParent(originalPart.transform, true);

                childTransformsBuffer.Clear();
            }

            part.enabled = false;

            part.mirrorVector = Vector3.one;
            part.isMirrored = false;
            part.mirrorAxis = part.mirrorRefAxis;
            Transform modelTransform = GetModelTransform(part);

            if (modelTransform.IsNullRef())
            {
                Debug.LogError($"[KSPCF/DragCubeGeneration] Model transform not found on part '{originalPart.partInfo.name}', no drag cube generated");
                yield break;
            }

            float rescaleFactor = part.rescaleFactor;
            modelTransform.localScale = new Vector3(rescaleFactor, rescaleFactor, rescaleFactor);

            for (int i = part.attachNodes.Count; i-- > 0;)
            {
                AttachNode attachNode = part.attachNodes[i];
                attachNode.position = attachNode.originalPosition;
                attachNode.orientation = attachNode.originalOrientation;
            }

            GameObject partObject = part.gameObject;
            // fix issue #154 : that substring in the name is actually checked in the Vessel.OnDestroy() method, doing a bunch of cleanup...
            partObject.name += " Drag Rendering Clone"; 
            partObject.SetActive(true);

            ModuleJettison moduleJettison = null;
            ModulePartVariants modulePartVariants = null;

            List<IMultipleDragCube> multipleDragCubeList = multipleDragCubeListPool.Get();
            List<ModuleDragModifier> moduleDragModifierList = moduleDragModifierListPool.Get();
            List<ModuleDragAreaModifier> moduleDragAreaModifierList = moduleDragAreaModifierListPool.Get();

            try
            {
                partObject.GetComponentsInChildren(true, staticComponentBuffer);
                DragCubeSystem dragCubeSystem = DragCubeSystem.Instance;
                int cameraLayer = dragCubeSystem.cameraLayerInt;

                int componentCount = staticComponentBuffer.Count;
                for (int i = 0; i < componentCount; i++)
                {
                    Component component = staticComponentBuffer[i];

                    if (component is MonoBehaviour monoBehaviour)
                    {
                        if (monoBehaviour is FXPrefab fxPrefab)
                        {
                            ParticleSystem particleSystem = fxPrefab.particleSystem;
                            Object.DestroyImmediate(fxPrefab);
                            if (particleSystem.IsNotNullOrDestroyed())
                                Object.DestroyImmediate(particleSystem);

                            continue;
                        }

                        monoBehaviour.enabled = false;

                        if (monoBehaviour is IMultipleDragCube iMultipleDragCube && iMultipleDragCube.IsMultipleCubesActive)
                        {
                            if (monoBehaviour is ModuleJettison && moduleJettison.IsNullRef())
                            {
                                moduleJettison = (ModuleJettison)monoBehaviour;
                            }
                            else if (monoBehaviour is ModulePartVariants && modulePartVariants.IsNullRef())
                            {
                                modulePartVariants = (ModulePartVariants)monoBehaviour;
                            }

                            multipleDragCubeList.Add(iMultipleDragCube);
                        }
                        else if (monoBehaviour is ModuleDragModifier moduleDragMod)
                        {
                            moduleDragModifierList.Add(moduleDragMod);
                        }
                        else if (monoBehaviour is ModuleDragAreaModifier moduleAreaMod)
                        {
                            moduleDragAreaModifierList.Add(moduleAreaMod);
                        }

                        continue;
                    }

                    if (component is Collider collider)
                    {
                        collider.enabled = false;
                        continue;
                    }

                    if (component is Renderer renderer)
                    {
                        GameObject go = renderer.gameObject;

                        // See https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/150
                        // Stock rules for drag cube generation is to ignore any renderer on layer 1 (TransparentFX)
                        // But when a part is detached in the editor, all renderers are set to this layer, resulting
                        // in no drag cube being generated at all. We can mitigate the issue by ignoring that rule
                        // when the part is detached by checking the "frozen" state, however this is imperfect as
                        // this flag isn't set until the part has been dropped, and renderers that should be ignored
                        // won't be, potentially generating incorrect drag cubes.
                        if (go.layer == 1 && !part.frozen)
                            renderer.enabled = false;

                        go.layer = cameraLayer;
                    }
                }
            }
            finally
            {
                staticComponentBuffer.Clear();
            }

            if (multipleDragCubeList.Count > 2)
            {
                Debug.LogWarning($"[KSPCF/DragCubeGeneration] Part '{part.partInfo.name}' has more than two IMultipleDragCube part modules. You should consider procedural drag cubes.");
            }

            if (modulePartVariants.IsNotNullRef() && moduleJettison.IsNotNullRef())
            {
                modulePartVariants.moduleJettison = moduleJettison;
                part.variants = modulePartVariants;
                moduleJettison.SetVariants();
            }

            DragCubeSystem dgs = DragCubeSystem.Instance;
            Texture2D output = dgs.aeroTexture;
            Camera camera = dgs.aeroCamera;
            Transform cameraTransform = camera.transform;

            try
            {
                camera.cullingMask = 0;

                foreach (IMultipleDragCube multipleDragCube in multipleDragCubeList)
                {
                    string[] cubeNames = multipleDragCube.GetDragCubeNames();

                    if (modulePartVariants.IsNotNullRef() && ReferenceEquals(multipleDragCube, moduleJettison) && originalPart.baseVariant != null)
                        modulePartVariants.SetVariant(originalPart.baseVariant.Name);

                    if (cubeNames == null || cubeNames.Length == 0)
                        cubeNames = defaultCubeNameArray;

                    foreach (string cubeName in cubeNames)
                    {
                        DragCube dragCube = new DragCube(cubeName);
                        try
                        {
                            multipleDragCube.AssumeDragCubePosition(cubeName);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[KSPCF/DragCubeGeneration] Call to '{multipleDragCube.AssemblyQualifiedName()}.AssumeDragCubePosition()' failed on part '{part.partInfo.name}' for drag cube '{cubeName}'\n{e}");
                            continue;
                        }
                        
                        KSPCFFastLoader.RequestFrameSkip();
                        yield return null;

                        float dragModifier = 1f;
                        float areaModifier = 1f;

                        foreach (ModuleDragModifier moduleDragModifier in moduleDragModifierList)
                        {
                            if (moduleDragModifier.dragCubeName == cubeName)
                            {
                                dragModifier = moduleDragModifier.dragModifier;
                                break;
                            }
                        }

                        foreach (ModuleDragAreaModifier moduleDragAreaModifier in moduleDragAreaModifierList)
                        {
                            if (moduleDragAreaModifier.dragCubeName == cubeName)
                            {
                                areaModifier = moduleDragAreaModifier.areaModifier;
                                break;
                            }
                        }

                        List<Renderer> rendererList = rendererListPool.Get();
                        List<DragRendererInfo> dragRenderers = dragRendererInfoListPool.Get();
                        CommandBuffer commandBuffer = commandBufferPool.Get();

                        try
                        {
                            modelTransform.GetComponentsInChildren(false, rendererList);
                            AnalyzeDragRenderers(part, rendererList, false, dragRenderers, out Bounds partBounds);

                            dragCube.center = partBounds.center;
                            dragCube.size = partBounds.size;

                            SetupCommandBuffer(commandBuffer, camera, dragRenderers);

                            for (int i = 0; i < 6; i++)
                            {
                                DragCube.DragFace face = (DragCube.DragFace)i;
                                SetupCameraForFace(face, part, partBounds, camera, cameraTransform, out float cameraSize, out float farClipPlane);
                                Render(cubeName, face, camera, output);
                                CalculateAerodynamics(output, cameraSize, farClipPlane, out float area, out float drag, out float depth);

                                dragCube.Area[i] = area * areaModifier;
                                dragCube.Drag[i] = drag * dragModifier;
                                dragCube.Depth[i] = depth;
                            }

                        }
                        finally
                        {
                            camera.RemoveAllCommandBuffers();
                            rendererListPool.Release(rendererList);
                            dragRendererInfoListPool.Release(dragRenderers);
                            commandBufferPool.Release(commandBuffer);
                        }

                        dragCubeList.Cubes.Add(dragCube);
                        dragConfig.AddValue("cube", dragCube.SaveToString());
                    }
                }
            }
            finally
            {
                camera.cullingMask = 1 << dgs.cameraLayerInt;

                partObject.SetActive(false);
                Object.DestroyImmediate(partObject);

                multipleDragCubeListPool.Release(multipleDragCubeList);
                moduleDragModifierListPool.Release(moduleDragModifierList);
                moduleDragAreaModifierListPool.Release(moduleDragAreaModifierList);
            }
        }

        public static DragCube RenderDragCubeImmediate(Part part, string dragCubeName)
        {
            if (part.frozen)
                Debug.LogWarning($"[KSPCF/DragCubeGeneration] Generating drag cubes on detached part ({part.partInfo.name}) in the editor isn't supported and will give incorrect results.");

            DragCube dragCube = new DragCube(dragCubeName);

            List<Renderer> rendererList = rendererListPool.Get();
            List<DragRendererInfo> dragRenderers = dragRendererInfoListPool.Get();
            CommandBuffer commandBuffer = commandBufferPool.Get();

            DragCubeSystem dgs = DragCubeSystem.Instance;
            Texture2D output = dgs.aeroTexture;
            Camera camera = dgs.aeroCamera;
            Transform cameraTransform = camera.transform;

            try
            {
                Transform modelTransform = GetModelTransform(part);
                modelTransform.GetComponentsInChildren(false, rendererList);
                AnalyzeDragRenderers(part, rendererList, false, dragRenderers, out Bounds partBounds);

                dragCube.center = partBounds.center;
                dragCube.size = partBounds.size;

                camera.cullingMask = 0;
                SetupCommandBuffer(commandBuffer, camera, dragRenderers);

                for (int i = 0; i < 6; i++)
                {
                    DragCube.DragFace face = (DragCube.DragFace)i;
                    SetupCameraForFace(face, part, partBounds, camera, cameraTransform, out float cameraSize, out float farClipPlane);
                    Render(string.Empty, face, camera, output);
                    CalculateAerodynamics(output, cameraSize, farClipPlane, out float area, out float drag, out float depth);

                    dragCube.Area[i] = area;
                    dragCube.Drag[i] = drag;
                    dragCube.Depth[i] = depth;
                }
            }
            finally
            {
                camera.cullingMask = 1 << dgs.cameraLayerInt;
                camera.RemoveAllCommandBuffers();
                foreach (DragRendererInfo dragRendererInfo in dragRenderers)
                    dragRendererInfo.RestoreRendererState();

                rendererListPool.Release(rendererList);
                dragRendererInfoListPool.Release(dragRenderers);
                commandBufferPool.Release(commandBuffer);
            }

            return dragCube;
        }

        private static void SetupCommandBuffer(CommandBuffer commandBuffer, Camera camera, List<DragRendererInfo> renderers)
        {
            for (int i = renderers.Count; i-- > 0;)
                commandBuffer.DrawRenderer(renderers[i].renderer, dragMaterial);

            camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffer);
        }

        private static void Render(string dragCubeName, DragCube.DragFace face, Camera camera, Texture2D output)
        {
            Canvas.WillRenderCanvases savedDelegate = fieldRef_Canvas_WillRenderCanvases();
            fieldRef_Canvas_WillRenderCanvases() = null;

            try
            {
                camera.Render();
            }
            finally
            {
                fieldRef_Canvas_WillRenderCanvases() = savedDelegate;
            }
            
            RenderTexture current = RenderTexture.active;

            try
            {
                RenderTexture.active = camera.targetTexture;
                output.ReadPixels(new Rect(0f, 0f, textureSize, textureSize), 0, 0);
                output.Apply();

                if (DragCubeDebugger.requireTextureCopy)
                    DragCubeDebugger.CopyTextureToDebugger(dragCubeName, face);
            }
            finally
            {
                RenderTexture.active = current;
            }
        }

        private static void SetupCameraForFace(DragCube.DragFace face, Part part, Bounds partBounds, Camera camera, Transform cameraTransform, out float cameraSize, out float farClipPlane)
        {
            Vector3 boundsSize = partBounds.size;
            cameraSize = GetCameraSize(face, boundsSize);
            farClipPlane = GetCameraDepth(face, boundsSize);

            cameraTransform.position = GetCameraPosition(part, face, partBounds);
            cameraTransform.rotation = GetCameraRotation(face, part.transform);
            camera.orthographicSize = cameraSize;
            camera.nearClipPlane = 0f;
            camera.farClipPlane = farClipPlane;
        }

        private static float GetCameraSize(DragCube.DragFace direction, Vector3 boundsSize)
        {
            float size = 0f;

            switch (direction)
            {
                case DragCube.DragFace.XP:
                case DragCube.DragFace.XN:
                    size = Mathf.Max(boundsSize.z, boundsSize.y);
                    break;
                case DragCube.DragFace.YP:
                case DragCube.DragFace.YN:
                    size = Mathf.Max(boundsSize.x, boundsSize.z);
                    break;
                case DragCube.DragFace.ZP:
                case DragCube.DragFace.ZN:
                    size = Mathf.Max(boundsSize.x, boundsSize.y);
                    break;
            }

            return size * 0.5f;
        }

        private static float GetCameraDepth(DragCube.DragFace direction, Vector3 boundsSize)
        {
            return direction switch
            {
                DragCube.DragFace.XP => boundsSize.x,
                DragCube.DragFace.XN => boundsSize.x,
                DragCube.DragFace.YP => boundsSize.y,
                DragCube.DragFace.YN => boundsSize.y,
                DragCube.DragFace.ZP => boundsSize.z,
                DragCube.DragFace.ZN => boundsSize.z,
                _ => throw new NotImplementedException()
            } + cameraDoubleOffset;
        }

        private static Quaternion lookLeft = Quaternion.LookRotation(Vector3.left);
        private static Quaternion lookRight = Quaternion.LookRotation(Vector3.right);
        private static Quaternion lookDown = Quaternion.LookRotation(Vector3.down);
        private static Quaternion lookUp = Quaternion.LookRotation(Vector3.up);
        private static Quaternion lookBack = Quaternion.LookRotation(Vector3.back);
        private static Quaternion lookForward = Quaternion.LookRotation(Vector3.forward);

        private static Quaternion GetCameraRotation(DragCube.DragFace face, Transform partTransform)
        {
            return face switch
            {
                DragCube.DragFace.XP => partTransform.rotation * lookLeft,
                DragCube.DragFace.XN => partTransform.rotation * lookRight,
                DragCube.DragFace.YP => partTransform.rotation * lookDown,
                DragCube.DragFace.YN => partTransform.rotation * lookUp,
                DragCube.DragFace.ZP => partTransform.rotation * lookBack,
                DragCube.DragFace.ZN => partTransform.rotation * lookForward,
                _ => throw new NotImplementedException()
            };
        }

        private static Vector3 GetCameraPosition(Part part, DragCube.DragFace direction, Bounds partBounds)
        {
            Vector3 pos = direction switch
            {
                DragCube.DragFace.XP => new Vector3(partBounds.max.x + cameraOffset, partBounds.center.y, partBounds.center.z),
                DragCube.DragFace.XN => new Vector3(partBounds.min.x - cameraOffset, partBounds.center.y, partBounds.center.z),
                DragCube.DragFace.YP => new Vector3(partBounds.center.x, partBounds.max.y + cameraOffset, partBounds.center.z),
                DragCube.DragFace.YN => new Vector3(partBounds.center.x, partBounds.min.y - cameraOffset, partBounds.center.z),
                DragCube.DragFace.ZP => new Vector3(partBounds.center.x, partBounds.center.y, partBounds.max.z + cameraOffset),
                DragCube.DragFace.ZN => new Vector3(partBounds.center.x, partBounds.center.y, partBounds.min.z - cameraOffset),
                _ => throw new NotImplementedException()
            };

            return part.transform.rotation * pos + part.transform.position;
        }

        private static void CalculateAerodynamics(Texture2D texture, float cameraSize, float farClipPlane, out float area, out float drag, out float depth)
        {
            // assuming ARGB32 texture
            NativeArray<byte> pixels = texture.GetRawTextureData<byte>();

            drag = 0f;
            depth = 0f;
            area = 0f;

            byte maxDepth = 0;
            int totalDrag = 0; // note : enough for the 256x256 texture, but can overflow if using a 1024x1024 texture
            int hits = 0;
            int length = pixels.Length;
            int i = 0;
            while (i < length)
            {
                if (pixels[i] != 0)
                {
                    hits++;

                    // note 1 : stock evaluate the byte against an animation curve, which
                    // is a linear [0,0] to [1,1] line, so it does basically nothing...
                    // note 2 : doing the / 255f division here and storing the float sum, 
                    // (like what stock is doing) cause a loss of precision and is slower.
                    // We do the division once outside the loop, but this mean our values
                    // will be slightly different from stock.
                    totalDrag += pixels[i + 1];
                    byte pixelDepth = pixels[i + 2];
                    if (pixelDepth > maxDepth)
                        maxDepth = pixelDepth;
                }
                i += 4;
            }

            if (hits > 0)
            {
                float pixelArea = Mathf.Pow(2f * cameraSize / textureSize, 2f);
                area = pixelArea * hits;
                drag = totalDrag / (hits * 255f);
                depth = farClipPlane * (maxDepth / 255f);
            }
        }

        private static void AnalyzeDragRenderers(Part part, List<Renderer> partRenderers, bool checkActive, List<DragRendererInfo> result, out Bounds partBounds)
        {
            Matrix4x4 partToWorldMatrix = part.transform.worldToLocalMatrix;
            Vector3 partMin = default;
            Vector3 partMax = default;
            partBounds = default;

            bool isFirstRenderer = true;

            Transform partTransform = part.transform;

            for (int i = partRenderers.Count; i-- > 0;)
            {
                Renderer renderer = partRenderers[i];

                if (!renderer.enabled)
                    continue;

                if (checkActive && !IsTransformActiveInHierarchyBelow(renderer.transform, partTransform))
                    continue;

                GameObject rendererObject = renderer.gameObject;

                if (rendererObject.CompareTag("Drag_Hidden"))
                    continue;

                if (rendererObject.layer == 1)
                    continue;

                Bounds meshBounds;

                if (renderer is MeshRenderer)
                {
                    MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                    if (meshFilter.IsNullRef())
                        continue;

                    meshBounds = meshFilter.mesh.bounds;
                }
                else if (renderer is SkinnedMeshRenderer skinnedMesh)
                {
                    meshBounds = skinnedMesh.localBounds;
                }
                else
                {
                    continue;
                }

                result.Add(new DragRendererInfo(renderer));

                Matrix4x4 localToPart = partToWorldMatrix * renderer.transform.localToWorldMatrix;

                Vector3 extents = meshBounds.extents;
                Vector3 center = meshBounds.center;

                // translate the center
                Vector3 partCenter = localToPart.MultiplyPoint3x4(center);

                // transform the local extents axes
                Vector3 axisX = localToPart.MultiplyVector(extents.x, 0f, 0f);
                Vector3 axisY = localToPart.MultiplyVector(0f, extents.y, 0f);
                Vector3 axisZ = localToPart.MultiplyVector(0f, 0f, extents.z);

                // sum their absolute value to get the resulting extents
                float partExtentsX = Math.Abs(axisX.x) + Math.Abs(axisY.x) + Math.Abs(axisZ.x);
                float partExtentsY = Math.Abs(axisX.y) + Math.Abs(axisY.y) + Math.Abs(axisZ.y);
                float partExtentsZ = Math.Abs(axisX.z) + Math.Abs(axisY.z) + Math.Abs(axisZ.z);

                if (isFirstRenderer)
                {
                    isFirstRenderer = false;
                    partMin = new Vector3(partCenter.x - partExtentsX, partCenter.y - partExtentsY, partCenter.z - partExtentsZ);
                    partMax = new Vector3(partCenter.x + partExtentsX, partCenter.y + partExtentsY, partCenter.z + partExtentsZ);
                }
                else
                {
                    Encapsulate(ref partMin, ref partMax, partCenter, partExtentsX, partExtentsY, partExtentsZ);
                }
            }

            partBounds.SetMinMax(partMin, partMax);
        }

        /// <summary>
        /// Return true if all parents of <paramref name="transform"/> below <paramref name="belowParent"/> are active (including <paramref name="transform"/> itself), false otherwise
        /// </summary>
        private static bool IsTransformActiveInHierarchyBelow(Transform transform, Transform belowParent)
        {
            if (transform.gameObject.activeInHierarchy)
                return true;

            bool activeInHierarchy;
            do
            {
                activeInHierarchy = transform.gameObject.activeSelf;
                transform = transform.parent;
            } 
            while (activeInHierarchy && transform.IsNotNullRef() && transform.RefNotEquals(belowParent));
            
            return activeInHierarchy;
        }

        /// <summary>
        /// Expand a bounding box defined by its min and max points to fit another bounding box defined by its center and extents
        /// </summary>
        private static void Encapsulate(ref Vector3 min, ref Vector3 max, Vector3 partCenter, float partExtentsX, float partExtentsY, float partExtentsZ)
        {
            float minX = partCenter.x - partExtentsX;
            if (minX < min.x)
                min.x = minX;
            else if (minX > max.x)
                max.x = minX;

            float minY = partCenter.y - partExtentsY;
            if (minY < min.y)
                min.y = minY;
            else if (minY > max.y)
                max.y = minY;

            float minZ = partCenter.z - partExtentsZ;
            if (minZ < min.z)
                min.z = minZ;
            else if (minZ > max.z)
                max.z = minZ;

            float maxX = partCenter.x + partExtentsX;
            if (maxX < min.x)
                min.x = maxX;
            else if (maxX > max.x)
                max.x = maxX;

            float maxY = partCenter.y + partExtentsY;
            if (maxY < min.y)
                min.y = maxY;
            else if (maxY > max.y)
                max.y = maxY;

            float maxZ = partCenter.z + partExtentsZ;
            if (maxZ < min.z)
                min.z = maxZ;
            else if (maxZ > max.z)
                max.z = maxZ;
        }

        private static Transform GetModelTransform(Part part)
        {
            if (part.partTransform.IsNullOrDestroyed())
                return null;

            string modelTransformName = null;
            List<PartModule> modules = part.modules.modules;
            for (int i = modules.Count; i-- > 0;)
            {
                PartModule pm = modules[i];
                if (pm is KerbalEVA)
                {
                    modelTransformName = "model01";
                    break;
                }

                if (pm is ModuleAsteroid)
                {
                    modelTransformName = "Asteroid";
                    break;
                }

                if (pm is ModuleComet)
                {
                    modelTransformName = "Comet";
                    break;
                }
            }

            if (modelTransformName == null)
                modelTransformName = "model";

            return part.partTransform.Find(modelTransformName);
        }
    }

    public class DragCubeDebugger : MonoBehaviour
    {
        private Texture2D xp;
        private Texture2D xn;
        private Texture2D yp;
        private Texture2D yn;
        private Texture2D zp;
        private Texture2D zn;

        public static bool requireTextureCopy;
        private static string currentDragCubeName;
        private static DragCubeDebugger instance;

        public static void Open()
        {
            if (instance.IsNullOrDestroyed())
            {
                GameObject go = new GameObject("KSPCF_DragCubeDebugger");
                instance = go.AddComponent<DragCubeDebugger>();
            }
        }

        public static void Close()
        {
            if (instance.IsNullRef())
                return;

            if (!instance.IsDestroyed())
                Destroy(instance.gameObject);

            instance = null;
        }

        public static void CopyTextureToDebugger(string dragCubeName, DragCube.DragFace face)
        {
            if (currentDragCubeName != dragCubeName)
                return;

            Texture2D currentTexture = face switch
            {
                DragCube.DragFace.XP => instance.xp,
                DragCube.DragFace.XN => instance.xn,
                DragCube.DragFace.YP => instance.yp,
                DragCube.DragFace.YN => instance.yn,
                DragCube.DragFace.ZP => instance.zp,
                DragCube.DragFace.ZN => instance.zn,
                _ => throw new NotImplementedException()
            };

            if (currentTexture.IsNullOrDestroyed())
            {
                requireTextureCopy = false;
                return;
            }

            currentTexture.ReadPixels(new Rect(0f, 0f, DragCubeGeneration.textureSize, DragCubeGeneration.textureSize), 0, 0);
            currentTexture.Apply();
        }

        private void Start()
        {
            xp = new Texture2D(DragCubeGeneration.textureSize, DragCubeGeneration.textureSize, TextureFormat.ARGB32, false);
            xn = new Texture2D(DragCubeGeneration.textureSize, DragCubeGeneration.textureSize, TextureFormat.ARGB32, false);
            yp = new Texture2D(DragCubeGeneration.textureSize, DragCubeGeneration.textureSize, TextureFormat.ARGB32, false);
            yn = new Texture2D(DragCubeGeneration.textureSize, DragCubeGeneration.textureSize, TextureFormat.ARGB32, false);
            zp = new Texture2D(DragCubeGeneration.textureSize, DragCubeGeneration.textureSize, TextureFormat.ARGB32, false);
            zn = new Texture2D(DragCubeGeneration.textureSize, DragCubeGeneration.textureSize, TextureFormat.ARGB32, false);

            gameObject.layer = LayerMask.NameToLayer("UI");
            gameObject.AddComponent<RectTransform>();
            Canvas countersCanvas = gameObject.AddComponent<Canvas>();
            countersCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            countersCanvas.pixelPerfect = true;
            countersCanvas.worldCamera = UIMasterController.Instance.appCanvas.worldCamera;
            countersCanvas.planeDistance = 625;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1;
            scaler.referencePixelsPerUnit = 100;

            gameObject.AddComponent<GraphicRaycaster>();

            mainWindow = AddEmptyPanel(gameObject);
            mainWindow.localPosition = new Vector3(0, 0, 0);
            mainWindow.sizeDelta = new Vector2(100, 100);
            mainWindow.anchorMin = new Vector2(0, 1);
            mainWindow.anchorMax = new Vector2(0, 1);
            mainWindow.pivot = new Vector2(0, 1);
            mainWindow.localScale = new Vector3(1, 1, 1);

            Image image = mainWindow.gameObject.AddComponent<Image>();
            image.color = new Color(0.25f, 0.25f, 0.25f, 1);

            DragPanel dg = mainWindow.gameObject.AddComponent<DragPanel>();
            dg.edgeOffset = -250;

            ContentSizeFitter csf = mainWindow.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            VerticalLayoutGroup layout = mainWindow.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            layout.spacing = 5;
            layout.padding = new RectOffset(5, 5, 5, 5);

            RectTransform titleRow = AddEmptyPanel(layout.gameObject);
            HorizontalLayoutGroup titleRowLayout = titleRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            titleRowLayout.childAlignment = TextAnchor.UpperLeft;
            titleRowLayout.childForceExpandHeight = true;
            titleRowLayout.childForceExpandWidth = false;
            titleRowLayout.childControlWidth = false;
            titleRowLayout.spacing = 5;
            titleRowLayout.padding = new RectOffset(0, 0, 0, 0);

            AddButton(titleRow.gameObject, "Close", text => Close());
            Button selectPartButton = AddButton(titleRow.gameObject, "Select part", SelectPart);
            partSelectButtonText = selectPartButton.GetComponentInChildren<Text>();
            AddButton(titleRow.gameObject, "Using part prefab", ToggleOnPrefab);

            AddText(layout.gameObject, "Drag cubes :");

            dragCubeSelection = AddEmptyPanel(layout.gameObject);
            HorizontalLayoutGroup dragCubeSelectionLayout = dragCubeSelection.gameObject.AddComponent<HorizontalLayoutGroup>();
            dragCubeSelectionLayout.childAlignment = TextAnchor.UpperLeft;
            dragCubeSelectionLayout.childForceExpandHeight = true;
            dragCubeSelectionLayout.childForceExpandWidth = false;
            dragCubeSelectionLayout.childControlWidth = false;
            dragCubeSelectionLayout.spacing = 5;
            dragCubeSelectionLayout.padding = new RectOffset(0, 0, 0, 0);

            dragCubeCenterAndSize = AddText(layout.gameObject, string.Empty);
            dragCubeAreaAndDragModifiers = AddText(layout.gameObject, string.Empty);
            Button clipboardButton = AddButton(layout.gameObject, "Copy drag cube config to clipboard", CopyCubeToClipboard);
            //LayoutElement dragCubeSerializedLayout = clipboardButton.gameObject.AddComponent<LayoutElement>();
            //dragCubeSerializedLayout.preferredWidth = 256 * 3;
            //dragCubeSerializedLayout.preferredHeight = 14 * 2;

            RectTransform topRow = AddEmptyPanel(layout.gameObject);
            HorizontalLayoutGroup topRowLayout = topRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            topRowLayout.childAlignment = TextAnchor.UpperLeft;
            topRowLayout.childForceExpandHeight = true;
            topRowLayout.childForceExpandWidth = true;
            topRowLayout.spacing = 5;
            topRowLayout.padding = new RectOffset(0, 0, 0, 0);

            cubeTextYP = AddCubeView(topRow.gameObject, "Down (YP)", yp);
            cubeTextXP = AddCubeView(topRow.gameObject, "Left (XP)", xp);
            cubeTextYN = AddCubeView(topRow.gameObject, "Up (YN)", yn);

            RectTransform bottomRow = AddEmptyPanel(layout.gameObject);
            HorizontalLayoutGroup bottomRowLayout = bottomRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            bottomRowLayout.childAlignment = TextAnchor.UpperLeft;
            bottomRowLayout.childForceExpandHeight = true;
            bottomRowLayout.childForceExpandWidth = true;
            bottomRowLayout.spacing = 5;
            bottomRowLayout.padding = new RectOffset(0, 0, 0, 0);

            cubeTextZP = AddCubeView(bottomRow.gameObject, "Back (ZP)", zp);
            cubeTextXN = AddCubeView(bottomRow.gameObject, "Right (XN)", xn);
            cubeTextZN = AddCubeView(bottomRow.gameObject, "Forward (ZN)", zn);
        }

        private bool partSelectionMode = false;
        private bool renderOnPrefab = true;
        private RectTransform mainWindow;
        private ScreenMessage partSelectionMessage;
        private Text partSelectButtonText;
        private Text cubeTextXP;
        private Text cubeTextXN;
        private Text cubeTextYP;
        private Text cubeTextYN;
        private Text cubeTextZP;
        private Text cubeTextZN;
        private RectTransform dragCubeSelection;
        private Part selectedPart;
        private Text dragCubeCenterAndSize;
        private Text dragCubeAreaAndDragModifiers;
        private DragCube currentDragCube;

        private void SelectPart(Text textComponent)
        {
            if (partSelectionMode)
                return;

            partSelectionMode = true;
            mainWindow.gameObject.SetActive(false);

            if (partSelectionMessage == null)
                partSelectionMessage = new ScreenMessage("Hover on a part and press [ENTER] to select it.\nPress any other key to abort.", 0f, ScreenMessageStyle.UPPER_CENTER);

            partSelectionMessage.duration = float.MaxValue;
            ScreenMessages.PostScreenMessage(partSelectionMessage);
        }

        private void ToggleOnPrefab(Text text)
        {
            ClearTexturesAndResetText();
            renderOnPrefab = !renderOnPrefab;
            if (renderOnPrefab)
                text.text = "Using part prefab";
            else
                text.text = "Using selected part";
        }

        private void LateUpdate()
        {
            if (selectedPart.IsNotNullRef() && selectedPart.IsDestroyed())
            {
                selectedPart = null;
                ClearTexturesAndResetText();
            }

            if (!partSelectionMode)
                return;

            if (Input.GetKeyDown(KeyCode.Return))
            {
                if (Mouse.HoveredPart.IsNotNullOrDestroyed())
                {
                    ExitSelectionMode();

                    selectedPart = Mouse.HoveredPart;
                    partSelectButtonText.text = $"Selected part : {selectedPart.partInfo.title} ({selectedPart.partInfo.name})";

                    dragCubeSelection.ClearChildren();
                    List<string> dragCubeNames = GetDragCubeNames(selectedPart);
                    for (int i = 0; i < dragCubeNames.Count; i++)
                    {
                        string dragCubeName = dragCubeNames[i];
                        Button button = AddButton(dragCubeSelection.gameObject, dragCubeName, OnDragCubeSelect);
                        if (i == 0)
                        {
                            button.onClick.Invoke();
                        }
                    }
                }
            }
            else if (Input.anyKeyDown)
            {
                ExitSelectionMode();
            }
        }

        private void OnDragCubeSelect(Text text)
        {
            foreach (Text buttonText in dragCubeSelection.gameObject.GetComponentsInChildren<Text>())
                buttonText.fontStyle = FontStyle.Normal;

            if (selectedPart.IsNullOrDestroyed())
                return;

            text.fontStyle = FontStyle.Bold;
            RenderDragCube(selectedPart, text.text);
        }

        private void ExitSelectionMode()
        {
            partSelectionMode = false;
            mainWindow.gameObject.SetActive(true);
            partSelectionMessage.duration = 0f;
        }

        private void SetDGText(Text text, DragCube.DragFace face, DragCube dg)
        {
            int i = (int)face;
            text.text = $"area={dg.Area[i]:G4} drag={dg.Drag[i]:G4} depth={dg.Depth[i]:G4}"; // G4 is what KSP uses for serializing
        }

        private void CopyCubeToClipboard(Text text)
        {
            if (currentDragCube == null)
                return;

            GUIUtility.systemCopyBuffer = currentDragCube.SaveToString();
        }

        private List<string> GetDragCubeNames(Part part)
        {
            bool isProcedural = false;
            List<string> dragCubeNames = new List<string>();

            foreach (PartModule module in part.modules)
            {
                if (module is IMultipleDragCube multipleDragCube && multipleDragCube.IsMultipleCubesActive)
                {
                    if (multipleDragCube.UsesProceduralDragCubes())
                    {
                        isProcedural = true;
                        break;
                    }

                    dragCubeNames.AddRange(multipleDragCube.GetDragCubeNames());
                }
            }

            if (isProcedural)
                dragCubeNames.Clear();

            if (!isProcedural && dragCubeNames.Count == 0)
                dragCubeNames.Add(DragCubeGeneration.defaultCubeName);

            dragCubeNames.Insert(0, "Current/Procedural");

            return dragCubeNames;
        }

        private void RenderDragCube(Part part, string dragCubeName)
        {
            StartCoroutine(RenderDragCubeCoroutine(part, dragCubeName));
        }

        private IEnumerator RenderDragCubeCoroutine(Part part, string dragCubeName)
        {
            float dragModifier = 1f;
            float areaModifier = 1f;

            if (renderOnPrefab && part.partInfo?.partPrefab != null)
                part = part.partInfo.partPrefab;

            requireTextureCopy = true;
            if (dragCubeName == "Current/Procedural")
            {
                currentDragCubeName = string.Empty;
                currentDragCube = DragCubeGeneration.RenderDragCubeImmediate(part, currentDragCubeName);
                currentDragCube.name = DragCubeGeneration.defaultCubeName;
            }
            else
            {
                List<PartModule> modules = part.modules.modules;
                bool isMultipleDragCube = false;
                for (int i = modules.Count; i-- > 0;)
                {
                    PartModule pm = modules[i];
                    if (pm is IMultipleDragCube iMultipleDragCube && iMultipleDragCube.IsMultipleCubesActive)
                    {
                        isMultipleDragCube = true;
                    }
                    if (pm is ModuleDragModifier moduleDragMod && moduleDragMod.dragCubeName == dragCubeName)
                    {
                        dragModifier = moduleDragMod.dragModifier;
                    }
                    else if (pm is ModuleDragAreaModifier moduleAreaMod && moduleAreaMod.dragCubeName == dragCubeName)
                    {
                        areaModifier = moduleAreaMod.areaModifier;
                    }
                }

                if (isMultipleDragCube)
                {
                    currentDragCubeName = dragCubeName;
                    DragCubeList dragCubeList = new DragCubeList();
                    yield return DragCubeSystem.Instance.StartCoroutine(DragCubeGeneration.RenderDragCubesOnCopy(part, dragCubeList, new ConfigNode()));
                    currentDragCube = dragCubeList.GetCube(dragCubeName);
                }
                else
                {
                    currentDragCubeName = dragCubeName;
                    currentDragCube = DragCubeGeneration.RenderDragCubeImmediate(part, dragCubeName);
                    for (int i = 6; i-- > 0;)
                    {
                        currentDragCube.drag[i] *= dragModifier;
                        currentDragCube.area[i] *= areaModifier;
                    }
                }
            }
            requireTextureCopy = false;

            if (currentDragCube == null)
            {
                ClearTexturesAndResetText();
                dragCubeCenterAndSize.text = "Error generating drag cube";
                yield break;
            }

            SetDGText(cubeTextXP, DragCube.DragFace.XP, currentDragCube);
            SetDGText(cubeTextXN, DragCube.DragFace.XN, currentDragCube);
            SetDGText(cubeTextYP, DragCube.DragFace.YP, currentDragCube);
            SetDGText(cubeTextYN, DragCube.DragFace.YN, currentDragCube);
            SetDGText(cubeTextZP, DragCube.DragFace.ZP, currentDragCube);
            SetDGText(cubeTextZN, DragCube.DragFace.ZN, currentDragCube);

            dragCubeCenterAndSize.text = $"Center={currentDragCube.center} Size={currentDragCube.size}";
            dragCubeAreaAndDragModifiers.text = $"Drag modifier={dragModifier} Area modifier={areaModifier}";
        }

        private void OnDestroy()
        {
            requireTextureCopy = false;
            Destroy(xp);
            Destroy(xn);
            Destroy(yp);
            Destroy(yn);
            Destroy(zp);
            Destroy(zn);
        }

        private void ClearTexturesAndResetText()
        {
            ClearTexture(xp);
            ClearTexture(xn);
            ClearTexture(yp);
            ClearTexture(yn);
            ClearTexture(zp);
            ClearTexture(zn);

            cubeTextXP.text = string.Empty;
            cubeTextXN.text = string.Empty;
            cubeTextYP.text = string.Empty;
            cubeTextYN.text = string.Empty;
            cubeTextZP.text = string.Empty;
            cubeTextZN.text = string.Empty;

            dragCubeCenterAndSize.text = string.Empty;
            dragCubeAreaAndDragModifiers.text = string.Empty;
        }

        private void ClearTexture(Texture2D texture)
        {
            NativeArray<byte> textureData = texture.GetRawTextureData<byte>();
            for (int i = textureData.Length; i-- > 0;)
                textureData[i] = 0;
            texture.Apply();
        }

        private static RectTransform AddEmptyPanel(GameObject parent)
        {
            GameObject panelObj = new GameObject("Panel");
            panelObj.layer = LayerMask.NameToLayer("UI");

            RectTransform panelRect = panelObj.AddComponent<RectTransform>();

            // Top Left corner as base
            panelRect.anchorMin = new Vector2(0, 1);
            panelRect.anchorMax = new Vector2(0, 1);
            panelRect.pivot = new Vector2(0, 1);
            panelRect.localPosition = new Vector3(0, 0, 0);
            panelRect.localScale = new Vector3(1, 1, 1);

            panelObj.transform.SetParent(parent.transform, true);

            return panelRect;
        }

        private static Text AddText(GameObject parent, string s)
        {
            GameObject text1Obj = new GameObject("Text");

            text1Obj.layer = LayerMask.NameToLayer("UI");

            RectTransform trans = text1Obj.AddComponent<RectTransform>();
            trans.localScale = new Vector3(1, 1, 1);
            trans.localPosition.Set(0, 0, 0);

            Text text = text1Obj.AddComponent<Text>();
            text.supportRichText = true;
            text.text = s;
            text.fontSize = 14;
            text.font = UISkinManager.defaultSkin.font;

            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text1Obj.transform.SetParent(parent.transform, false);

            return text;
        }

        private static Button AddButton(GameObject parent, string text, UnityAction<Text> click)
        {
            GameObject buttonObject = new GameObject("Button");

            buttonObject.layer = LayerMask.NameToLayer("UI");

            RectTransform trans = buttonObject.AddComponent<RectTransform>();
            trans.localScale = new Vector3(1.0f, 1.0f, 1.0f);
            trans.localPosition.Set(0, 0, 0);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.5f, 0f, 0, 0.5f);

            Button button = buttonObject.AddComponent<Button>();
            button.interactable = true;

            Text textObj = AddText(buttonObject, text);

            button.onClick.AddListener(() => click(textObj));

            var csf = buttonObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = buttonObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(5, 5, 5, 5);

            buttonObject.transform.SetParent(parent.transform, false);

            return button;
        }

        private static Text AddCubeView(GameObject parent, string viewName, Texture2D texture)
        {
            RectTransform border = AddEmptyPanel(parent);
            VerticalLayoutGroup borderLayout = border.gameObject.AddComponent<VerticalLayoutGroup>();
            borderLayout.childAlignment = TextAnchor.MiddleCenter;
            borderLayout.childForceExpandHeight = true;
            borderLayout.childForceExpandWidth = true;
            borderLayout.spacing = 0;
            borderLayout.padding = new RectOffset(1, 1, 1, 1);
            RawImage borderImage = border.gameObject.AddComponent<RawImage>();
            borderImage.color = Color.black;

            RectTransform contentObject = AddEmptyPanel(border.gameObject);

            RawImage backgroundImage = contentObject.gameObject.AddComponent<RawImage>();
            backgroundImage.color = new Color(0.25f, 0.25f, 0.25f, 1);

            VerticalLayoutGroup layout = contentObject.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            layout.spacing = 5;
            layout.padding = new RectOffset(0, 0, 0, 0);

            Text title = AddText(contentObject.gameObject, viewName);
            title.alignment = TextAnchor.UpperCenter;

            RectTransform texZP = AddEmptyPanel(contentObject.gameObject);
            LayoutElement layoutZP = texZP.gameObject.AddComponent<LayoutElement>();
            layoutZP.minHeight = DragCubeGeneration.textureSize;
            layoutZP.minWidth = DragCubeGeneration.textureSize;
            RawImage imgZP = texZP.gameObject.AddComponent<RawImage>();
            imgZP.texture = texture;

            Text cubeText = AddText(contentObject.gameObject, string.Empty);
            return cubeText;
        }
    }
}
