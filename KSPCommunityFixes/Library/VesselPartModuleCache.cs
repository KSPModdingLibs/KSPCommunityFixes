// Vessel-wide PartModule lookup cache, the vessel level equivalent of the per-part
// caches used by KSPObjectsExtensions.FindModuleImplementingFast<T>().
//
// The stock Vessel.FindPartModuleImplementing<T>() and Vessel.FindPartModulesImplementing<T>()
// walk every module of every part on every call and cache nothing, so the common
// "does this vessel have a module of type T" check is O(parts * modules) every time,
// and it is the most expensive when the answer is "no", which is also the most
// common answer.
//
// The cache lives as a component on the vessel GameObject so it can't outlive the
// vessel and doesn't need a static registry that would keep dead vessels alive.
// Cached entries (including the "there is no such module" ones) are invalidated by :
//
// - GameEvents.onVesselStandardModification, which is the union of the various part
//   and vessel modification events (see FlightGlobals.HookVesselEvents()). The stock
//   code paths fire it after vessel.parts has been updated (Part.Die() calls
//   disconnect() before firing, Part.Couple() fires it after SetVessel()), which is
//   what the lazy rebuild done here requires. onPartCouple is fired before the
//   change, but that merely marks the cache dirty early and the post-change events
//   mark it dirty again.
//
// - a vessel.parts.Count comparison on every access, which catches the code paths
//   mutating vessel.parts without firing any event (ProtoVessel.Load(), a few stock
//   oddities, mods).
//
// - a Part.ClearModuleReferenceCache() postfix (see VesselPartModuleCacheInvalidation),
//   the method stock calls from every PartModuleList mutator, which catches modules
//   being added to or removed from a part at runtime.
//
// Not handled : swapping a part for another one with no event and an identical vessel
// part count, and mods mutating part.modules.modules directly without calling
// Part.ClearModuleReferenceCache(). Both already defeat the stock Part.cachedModules
// cache, so this isn't a new limitation.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes
{
    internal class VesselPartModuleCache : MonoBehaviour
    {
        // Vessel-wide lookups are usually repeated for the same vessel (every module of
        // a given type on the active vessel querying it in the same update), so memoize
        // the last used cache to avoid a GetComponent() call on every lookup.
        private static Vessel lastVessel;
        private static VesselPartModuleCache lastCache;

        private Vessel vessel;

        // typeof(T) => first PartModule of that type on the vessel, or null if there is none
        private readonly Dictionary<Type, PartModule> singleModules = new Dictionary<Type, PartModule>(10);

        // typeof(T) => List<T> of all the PartModule instances of that type on the vessel
        private readonly Dictionary<Type, object> moduleLists = new Dictionary<Type, object>(10);

        private int cachedPartCount = -1;
        private bool isDirty;

        /// <summary>
        /// Get the cache for <paramref name="vessel"/>, creating it if it doesn't exist yet.<para/>
        /// Returns <see langword="null"/> if the vessel is <see langword="null"/> or destroyed.
        /// </summary>
        internal static VesselPartModuleCache Get(Vessel vessel)
        {
            if (vessel.IsNullOrDestroyed())
                return null;

            if (vessel.RefEquals(lastVessel) && lastCache.IsNotNullOrDestroyed())
                return lastCache;

            VesselPartModuleCache cache = vessel.gameObject.AddOrGetComponent<VesselPartModuleCache>();
            lastVessel = vessel;
            lastCache = cache;
            return cache;
        }

        /// <summary>
        /// Invalidate the cache for <paramref name="vessel"/>, if it has one. Does not create it.
        /// </summary>
        internal static void SetDirty(Vessel vessel)
        {
            if (vessel.IsNullOrDestroyed())
                return;

            if (vessel.RefEquals(lastVessel) && lastCache.IsNotNullOrDestroyed())
            {
                lastCache.isDirty = true;
                return;
            }

            VesselPartModuleCache cache = vessel.gameObject.GetComponent<VesselPartModuleCache>();
            if (cache.IsNotNullRef())
                cache.isDirty = true;
        }

        /// <summary>
        /// The first <see cref="PartModule"/> of type <typeparamref name="T"/> on the vessel, or <see langword="null"/> if there is none.
        /// </summary>
        internal T FindModule<T>() where T : class
        {
            return GetCachedModule<T>() as T;
        }

        /// <summary>
        /// True if a <see cref="PartModule"/> of type <typeparamref name="T"/> is present on the vessel.
        /// </summary>
        internal bool HasModule<T>() where T : class
        {
            return GetCachedModule<T>().IsNotNullRef();
        }

        /// <summary>
        /// The cached list of all the <see cref="PartModule"/> instances of type <typeparamref name="T"/> on the vessel.<para/>
        /// Do NOT modify the returned list, it is a direct reference to the cache.
        /// </summary>
        internal List<T> FindModules<T>() where T : class
        {
            ValidateCache();

            Type moduleType = typeof(T);
            if (moduleLists.TryGetValue(moduleType, out object cachedList))
                return (List<T>)cachedList;

            List<T> modulesOfType = new List<T>();

            List<Part> parts = vessel.parts;
            if (parts != null)
            {
                for (int i = 0, partCount = parts.Count; i < partCount; i++)
                {
                    PartModuleList partModules = parts[i].modules;
                    if (partModules == null)
                        continue;

                    List<PartModule> modules = partModules.modules;
                    for (int j = 0, moduleCount = modules.Count; j < moduleCount; j++)
                        if (modules[j] is T module)
                            modulesOfType.Add(module);
                }
            }

            moduleLists[moduleType] = modulesOfType;

            // the first entry of the list is also the answer to the single module lookup
            singleModules[moduleType] = modulesOfType.Count > 0 ? modulesOfType[0] as PartModule : null;

            return modulesOfType;
        }

        private PartModule GetCachedModule<T>() where T : class
        {
            ValidateCache();

            Type moduleType = typeof(T);
            if (singleModules.TryGetValue(moduleType, out PartModule module))
                return module;

            if (moduleLists.TryGetValue(moduleType, out object cachedList))
            {
                // the full list for that type is already cached, no need to search again
                List<T> modulesOfType = (List<T>)cachedList;
                module = modulesOfType.Count > 0 ? modulesOfType[0] as PartModule : null;
            }
            else
            {
                module = FindFirstModule<T>();
            }

            singleModules[moduleType] = module;
            return module;
        }

        private PartModule FindFirstModule<T>() where T : class
        {
            List<Part> parts = vessel.parts;
            if (parts == null)
                return null;

            for (int i = 0, partCount = parts.Count; i < partCount; i++)
            {
                PartModuleList partModules = parts[i].modules;
                if (partModules == null)
                    continue;

                List<PartModule> modules = partModules.modules;
                for (int j = 0, moduleCount = modules.Count; j < moduleCount; j++)
                    if (modules[j] is T)
                        return modules[j];
            }

            return null;
        }

        private void ValidateCache()
        {
            List<Part> parts = vessel.parts;
            int partCount = parts == null ? 0 : parts.Count;

            if (!isDirty && partCount == cachedPartCount)
                return;

            singleModules.Clear();
            moduleLists.Clear();
            cachedPartCount = partCount;
            isDirty = false;
        }

        private void ClearCache()
        {
            singleModules.Clear();
            moduleLists.Clear();
            isDirty = true;
        }

        private void Awake()
        {
            vessel = gameObject.GetComponent<Vessel>();

            // Subscribed here and not in Start() because the cache is created on demand
            // and can be queried in the same frame, before Start() would have run.
            GameEvents.onVesselStandardModification.Add(OnVesselStandardModification);
            GameEvents.onVesselUnloaded.Add(OnVesselUnloaded);
            GameEvents.onVesselDestroy.Add(OnVesselDestroy);
        }

        private void OnDestroy()
        {
            GameEvents.onVesselStandardModification.Remove(OnVesselStandardModification);
            GameEvents.onVesselUnloaded.Remove(OnVesselUnloaded);
            GameEvents.onVesselDestroy.Remove(OnVesselDestroy);

            if (this.RefEquals(lastCache))
            {
                lastVessel = null;
                lastCache = null;
            }
        }

        private void OnVesselStandardModification(Vessel v)
        {
            if (v.RefEquals(vessel))
                isDirty = true;
        }

        private void OnVesselUnloaded(Vessel v)
        {
            // the parts are about to be destroyed, don't keep references to their modules around
            if (v.RefEquals(vessel))
                ClearCache();
        }

        private void OnVesselDestroy(Vessel v)
        {
            // the Vessel component can be destroyed without its GameObject being destroyed
            // (see Part.Couple()), in which case this component would be left dangling.
            if (v.RefEquals(vessel))
                Destroy(this);
        }
    }
}
