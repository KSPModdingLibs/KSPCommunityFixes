// #define PROFILE_KSPObjectsExtensions

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes
{
    public static class KSPObjectsExtensions
    {
        /// <summary>
        /// Faster version of <see cref="Part.FindModuleImplementing"/>
        /// </summary>
        /// <typeparam name="T">The type of the module to search. Can be an interface.</typeparam>
        /// <returns>The first found instance of type <typeparamref name="T"/>, or <see langword="null"/> if not present on the <see cref="Part"/></returns>
        public static T FindModuleImplementingFast<T>(this Part part) where T : class
        {
            part.cachedModules ??= new Dictionary<Type, PartModule>(10);

            Type moduleType = typeof(T);
            if (!part.cachedModules.TryGetValue(moduleType, out PartModule module))
            {
                if (part.modules == null)
                    return null;

                List<PartModule> modules = part.modules.modules;
                int moduleCount = modules.Count;
                for (int i = 0; i < moduleCount; i++)
                {
                    if (modules[i] is T)
                    {
                        module = modules[i];
                        break;
                    }
                }

                part.cachedModules[moduleType] = module;
            }

            return module as T;
        }

        /// <summary>
        /// Faster version of <see cref="Part.HasModuleImplementing"/>
        /// </summary>
        /// <typeparam name="T">The type of the module to search. Can be an interface.</typeparam>
        /// <returns><see langword="true"/> if a <see cref="PartModule"/> of type <typeparamref name="T"/> is present on the <see cref="Part"/></returns>
        public static bool HasModuleImplementingFast<T>(this Part part) where T : class
        {
            part.cachedModules ??= new Dictionary<Type, PartModule>(10);

            Type moduleType = typeof(T);
            if (!part.cachedModules.TryGetValue(moduleType, out PartModule module))
            {
                if (part.modules == null)
                    return false;

                List<PartModule> modules = part.modules.modules;
                int moduleCount = modules.Count;
                for (int i = 0; i < moduleCount; i++)
                {
                    if (modules[i] is T)
                    {
                        module = modules[i];
                        break;
                    }
                }

                part.cachedModules[moduleType] = module;
            }

            return module.IsNotNullRef();
        }

        public static bool IsKerbalEVA(Part part)
        {
            part.cachedModules ??= new Dictionary<Type, PartModule>(10);

            if (!part.cachedModules.TryGetValue(typeof(KerbalEVA), out PartModule module))
            {
                if (part.modules == null)
                    return false;

                List<PartModule> modules = part.modules.modules;
                int moduleCount = modules.Count;
                for (int i = 0; i < moduleCount; i++)
                {
                    if (modules[i] is KerbalEVA)
                    {
                        module = modules[i];
                        break;
                    }
                }

                part.cachedModules[typeof(KerbalEVA)] = module;
            }

            return module.IsNotNullRef();
        }


        /// <summary>
        /// Return the cached list of <see cref="PartModule"/> instances of type <typeparamref name="T"/> present on the part.<para/>
        /// Do NOT modify the returned list, it is a direct reference to the cache.
        /// </summary>
        public static List<PartModule> FindModulesImplementingReadOnly<T>(this Part part) where T : class
        {
            part.cachedModuleLists ??= new Dictionary<Type, List<PartModule>>(10);

            Type moduleType = typeof(T);
            if (!part.cachedModuleLists.TryGetValue(moduleType, out List<PartModule> modules))
            {
                if (part.modules == null)
                    return new List<PartModule>(0);

                modules = new List<PartModule>(4);

                List<PartModule> partModules = part.modules.modules;
                int moduleCount = partModules.Count;

                for (int i = 0; i < moduleCount; i++)
                    if (partModules[i] is T)
                        modules.Add(partModules[i]);

                part.cachedModuleLists[moduleType] = modules;
            }

            return modules;
        }

        /// <summary>
        /// Return the top level <see cref="Transform"/> of the part model.
        /// </summary>
        public static Transform FindModelTransform(this Part part)
        {
            if (part.partTransform.IsNullOrDestroyed())
                return null;

            string modelName;
            if (part.HasModuleImplementing<KerbalEVA>())
                modelName = "model01";
            else if (part.HasModuleImplementing<ModuleAsteroid>())
                modelName = "Asteroid";
            else if (part.HasModuleImplementing<ModuleComet>())
                modelName = "Comet";
            else
                modelName = "model";

            return part.partTransform.Find(modelName);
        }

        /// <summary>
        /// Populate the provided <paramref name="renderers"/> list with all <see cref="MeshRenderer"/> and <see cref="SkinnedMeshRenderer"/> instances on the part model (including its childrens).
        /// </summary>
        /// <param name="renderers">The list to populate. Existing elements will be cleared.</param>
        /// <param name="includeInactive">If <see langword="false"/>, only <see cref="Renderer"/> on currently active <see cref="GameObject"/> will be returned.</param>
        public static void FindModelRenderersFast(this Part part, List<Renderer> renderers, bool includeInactive = true)
        {
            Transform modelTransform = FindModelTransform(part);
            if (modelTransform.IsNullRef())
            {
                renderers.Clear();
                return;
            }

            modelTransform.GetComponentsInChildren(includeInactive, renderers);
            for (int i = renderers.Count; i-- > 0;)
            {
                Renderer renderer = renderers[i];
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                    renderers.RemoveAt(i);
            }
        }

        /// <summary>
        /// Get a cached list of all the <see cref="MeshRenderer"/> and <see cref="SkinnedMeshRenderer"/> instances on the part model (including its childrens).<para/>
        /// Do NOT modify the returned list, it is a direct reference to the cache.
        /// </summary>
        public static List<Renderer> FindModelRenderersReadOnly(this Part part)
        {
            List<Renderer> cache = part.modelRenderersCache;
            if (cache != null)
            {
                for (int i = cache.Count; i-- > 0;)
                {
                    if (cache[i].IsDestroyed())
                    {
                        part.FindModelRenderersFast(cache, true);
                        break;
                    }
                }
            }
            else
            {
                cache = new List<Renderer>(0);
                part.FindModelRenderersFast(cache, true);
                part.modelRenderersCache = cache;
            }

            return cache;
        }

        /// <summary>
        /// Faster version of <see cref="Part.FindModelRenderersCached"/>
        /// Get an array of all the <see cref="MeshRenderer"/> and <see cref="SkinnedMeshRenderer"/> instances on the part model (including its childrens).
        /// </summary>
        public static Renderer[] FindModelRenderersCachedFast(this Part part)
        {
            return FindModelRenderersReadOnly(part).ToArray();
        }

        /// <summary>
        /// Populate the provided <paramref name="renderers"/> list with all <see cref="MeshRenderer"/> instances on the part model (including its childrens).
        /// </summary>
        /// <param name="renderers">The list to populate. Existing elements will be cleared.</param>
        /// <param name="includeInactive">If <see langword="false"/>, only components on currently active <see cref="GameObject"/>s will be returned.</param>
        public static void FindModelMeshRenderersFast(this Part part, List<MeshRenderer> renderers, bool includeInactive = true)
        {
            Transform modelTransform = FindModelTransform(part);
            if (modelTransform.IsNullRef())
            {
                renderers.Clear();
                return;
            }

            modelTransform.GetComponentsInChildren(includeInactive, renderers);
        }

        /// <summary>
        /// Get a cached list of all the <see cref="MeshRenderer"/> instances on the part model (including its childrens).<para/>
        /// Do NOT modify the returned list, it is a direct reference to the cache.
        /// </summary>
        public static List<MeshRenderer> FindModelMeshRenderersReadOnly(this Part part)
        {
            List<MeshRenderer> cache = part.modelMeshRenderersCache;
            if (cache != null)
            {
                for (int i = cache.Count; i-- > 0;)
                {
                    if (cache[i].IsDestroyed())
                    {
                        part.FindModelMeshRenderersFast(cache, true);
                        break;
                    }
                }
            }
            else
            {
                cache = new List<MeshRenderer>(0);
                part.FindModelMeshRenderersFast(cache, true);
                part.modelMeshRenderersCache = cache;
            }

            return cache;
        }

        /// <summary>
        /// Faster version of <see cref="Part.FindModelMeshRenderersCached"/>
        /// Get an array of all the <see cref="MeshRenderer"/> instances on the part model (including its childrens).
        /// </summary>
        public static MeshRenderer[] FindModelMeshRenderersCachedFast(this Part part)
        {
            return FindModelMeshRenderersReadOnly(part).ToArray();
        }

        /// <summary>
        /// Populate the provided <paramref name="renderers"/> list with all <see cref="SkinnedMeshRenderer"/> instances on the part model (including its childrens).
        /// </summary>
        /// <param name="renderers">The list to populate. Existing elements will be cleared.</param>
        /// <param name="includeInactive">If <see langword="false"/>, only components on currently active <see cref="GameObject"/>s will be returned.</param>
        public static void FindModelSkinnedMeshRenderersFast(this Part part, List<SkinnedMeshRenderer> renderers, bool includeInactive = true)
        {
            Transform modelTransform = FindModelTransform(part);
            if (modelTransform.IsNullRef())
            {
                renderers.Clear();
                return;
            }

            modelTransform.GetComponentsInChildren(includeInactive, renderers);
        }

        /// <summary>
        /// Get a cached list of all the <see cref="SkinnedMeshRenderer"/> instances on the part model (including its childrens).<para/>
        /// Do NOT modify the returned list, it is a direct reference to the cache.
        /// </summary>
        public static List<SkinnedMeshRenderer> FindModelSkinnedMeshRenderersReadOnly(this Part part)
        {
            List<SkinnedMeshRenderer> cache = part.modelSkinnedMeshRenderersCache;
            if (cache != null)
            {
                for (int i = cache.Count; i-- > 0;)
                {
                    if (cache[i].IsDestroyed())
                    {
                        part.FindModelSkinnedMeshRenderersFast(cache, true);
                        break;
                    }
                }
            }
            else
            {
                cache = new List<SkinnedMeshRenderer>(0);
                part.FindModelSkinnedMeshRenderersFast(cache, true);
                part.modelSkinnedMeshRenderersCache = cache;
            }

            return cache;
        }

        /// <summary>
        /// Faster version of <see cref="Part.FindModelSkinnedMeshRenderersCached"/>
        /// Get an array of all the <see cref="SkinnedMeshRenderer"/> instances on the part model (including its childrens).
        /// </summary>
        public static SkinnedMeshRenderer[] FindModelSkinnedMeshRenderersCachedFast(this Part part)
        {
            return FindModelSkinnedMeshRenderersReadOnly(part).ToArray();
        }
    }

#if PROFILE_KSPObjectsExtensions
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class MainMenuTester : MonoBehaviour
    {
        public static Stopwatch w1 = new Stopwatch();
        public static Stopwatch w2 = new Stopwatch();
        public static Stopwatch w3 = new Stopwatch();
        public static Stopwatch w4 = new Stopwatch();
        public static Stopwatch w5 = new Stopwatch();
        public static Stopwatch w6 = new Stopwatch();
        public static Stopwatch w7 = new Stopwatch();

        public IEnumerator Start()
        {
            for (int i = 0; i < 50; i++)
            {
                yield return null;
            }

            for (int i = 0; i < 50; i++)
            {
                Benchmark();
                yield return null;
            }
            
        }

        public static void Benchmark()
        {
            List<Renderer> renderers = new List<Renderer>();
            Renderer[] renderersArray;

            // warmup
            foreach (AvailablePart availablePart in PartLoader.LoadedPartsList)
            {
                availablePart.partPrefab.FindModelRenderersFast(renderers);
                renderers = availablePart.partPrefab.FindModelComponents<Renderer>();
                renderers = availablePart.partPrefab.FindModelRenderersCached();
                renderersArray = availablePart.partPrefab.FindModelRenderersCachedFast();
            }

            w1.Start();
            foreach (AvailablePart availablePart in PartLoader.LoadedPartsList)
            {
                availablePart.partPrefab.FindModelRenderersFast(renderers);
            }
            w1.Stop();
            Debug.Log($"FindModelRenderersFast : {w1.Elapsed.TotalMilliseconds:F3} ms");

            w2.Start();
            foreach (AvailablePart availablePart in PartLoader.LoadedPartsList)
            {
                renderers = availablePart.partPrefab.FindModelComponents<Renderer>();
            }
            w2.Stop();
            Debug.Log($"FindModelRenderersStock : {w2.Elapsed.TotalMilliseconds:F3} ms");

            w3.Start();
            foreach (AvailablePart availablePart in PartLoader.LoadedPartsList)
            {
                renderers = availablePart.partPrefab.FindModelRenderersCached();
            }
            w3.Stop();
            Debug.Log($"FindModelRenderersCached : {w3.Elapsed.TotalMilliseconds:F3} ms");

            w4.Start();
            foreach (AvailablePart availablePart in PartLoader.LoadedPartsList)
            {
                renderersArray = availablePart.partPrefab.FindModelRenderersCachedFast();
            }
            w4.Stop();
            Debug.Log($"FindModelRenderersCachedFast : {w4.Elapsed.TotalMilliseconds:F3} ms");
        }
    }
#endif
}
