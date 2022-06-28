using CommNet;
using HarmonyLib;
using KSP.UI.Screens;
using RUI.Algorithms;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.Performance
{
    class MemoryLeaks : BasePatch
    {
        private static readonly Stopwatch watch = new Stopwatch();

        private static bool logGameEventsDestroyedObjectCallbacks = false;

        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            KSPCommunityFixes.SettingsNode.TryGetValue("LogGameEventsLeaks", ref logGameEventsDestroyedObjectCallbacks);
#if DEBUG
            logGameEventsDestroyedObjectCallbacks = true;
#endif
            // removing PartSet finalizers

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartSet), nameof(PartSet.HookEvents)),
                this));

            // Specific GameEvents leaks

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(CommNetVessel), nameof(CommNetVessel.OnDestroy)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(StageGroup), nameof(StageGroup.OnDestroy)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleProceduralFairing), nameof(ModuleProceduralFairing.OnDestroy)),
                this));

            // EffectList dictionary enumerator leaks

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EffectList), nameof(EffectList.Initialize)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EffectList), nameof(EffectList.OnSave)),
                this));

            // various static fields keeping deep reference stacks around...

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(CraftSearch), nameof(CraftSearch.OnDestroy)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EngineersReport), nameof(EngineersReport.OnAppDestroy)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.OnDestroy)),
                this));

            // Singleton Monobehaviours holding tons of part references...

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(InventoryPanelController), nameof(InventoryPanelController.OnDestroy)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EVAConstructionModeController), nameof(EVAConstructionModeController.OnDestroy)),
                this));

            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            
        }

        private enum KSPScene
        {
            Loading = 0,
            LoadingBuffer = 1,
            MainMenu = 2,
            Settings = 3,
            Credits = 4,
            SpaceCenter = 5,
            Editor = 6,
            Flight = 7,
            TrackingStation = 8,
            PSystemSpawn = 9,
            VABscenery = 10,
            SPHscenery = 11,
            SPHlvl1 = 12,
            SPHlvl2 = 13,
            SPHmodern = 14,
            VABlvl1 = 15,
            VABlvl2 = 16,
            VABmodern = 17,
            MissionBuilder = 21
        }

        private void OnSceneUnloaded(Scene scene)
        {
            KSPScene currentScene = (KSPScene)scene.buildIndex;
            if (currentScene != KSPScene.SpaceCenter 
                && currentScene != KSPScene.Editor 
                && currentScene != KSPScene.Flight 
                && currentScene != KSPScene.TrackingStation)
                return;

            watch.Restart();
            int leakCount = 0;
            int gameEventsCallbacksCount = 0;

            // PartSet doesn't derive from UnityEngine.Object and suscribes to the onPartResourceFlowStateChange and
            // onPartResourceFlowModeChange GameEvents. Instances can be owned by a bunch of classes, including Vessel,
            // Part, ShipConstruct... Whoever implemented this though that he could avoid having to manage the GameEvents
            // removal by doing it in a PartSet finalizer. This doesn't work as the GameEvent subscription will prevent the
            // instance from ever be eligible for GC.
            // Since PartSet lifecycle seems impossible to track reliably, we implement a specific check in the relevant GameEvents to
            // at least clean the mess on scene switch. We also implement a PartSet.HookEvents() postfix patch to supress the
            // finalizer, since apart from slowing GC this won't do anything.
            for (int i = GameEvents.onPartResourceFlowStateChange.events.Count; i-- > 0;)
            {
                if (GameEvents.onPartResourceFlowStateChange.events[i].originator is PartSet)
                {
                    GameEvents.onPartResourceFlowStateChange.events.RemoveAt(i);
                    leakCount++;
                }
            }

            for (int i = GameEvents.onPartResourceFlowModeChange.events.Count; i-- > 0;)
            {
                if (GameEvents.onPartResourceFlowModeChange.events[i].originator is PartSet)
                {
                    GameEvents.onPartResourceFlowModeChange.events.RemoveAt(i);
                    leakCount++;
                }
            }

            // This is a general catch-all patch that attempt to remove all GameEvents delegates owned by destroyed unity objects
            // after a scene switch.
            // While we are fixing some GameEvent induced leaks manually, there are simply too many of them and this has the added
            // benefit of also taking care of mod induced GameEvents leaks.
            // Implementation is relying on reflection because of generic GameEvents types, as well as the fact that the same
            // EvtDelegate class is reimplemented as a nested class in every GE type, despite each class using the same exact layout...
            foreach (BaseGameEvent gameEvent in BaseGameEvent.eventsByName.Values)
            {
                // "fast" path for EventVoid
                if (gameEvent is EventVoid eventVoid)
                {
                    gameEventsCallbacksCount += eventVoid.events.Count;
                    for (int i = eventVoid.events.Count; i-- > 0;)
                    {
                        if (eventVoid.events[i].originator is UnityEngine.Object unityObject && unityObject.IsDestroyed())
                        {
                            if (logGameEventsDestroyedObjectCallbacks)
                            {
                                Debug.Log($"[KSPCF:MemoryLeaks] Removed a {gameEvent.EventName} GameEvents callback owned by a destroyed {unityObject.GetType().FullName} instance");
                            }
                            eventVoid.events.RemoveAt(i);
                            leakCount++;
                            gameEventsCallbacksCount--;
                        }
                    }

                    continue;
                }

                // general case for all others GameEvent types
                try
                {
                    IList list = (IList)gameEvent.GetType().GetField(nameof(EventVoid.events), BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gameEvent);
                    int count = list.Count;
                    if (count == 0)
                        continue;

                    gameEventsCallbacksCount += count;
                    FieldInfo originatorField = list[0].GetType().GetField(nameof(EventVoid.EvtDelegate.originator));
                    while (count-- > 0)
                    {
                        if (originatorField.GetValue(list[count]) is UnityEngine.Object unityObject && unityObject.IsDestroyed())
                        {
                            if (logGameEventsDestroyedObjectCallbacks)
                            {
                                Debug.Log($"[KSPCF:MemoryLeaks] Removed a {gameEvent.EventName} GameEvents callback owned by a destroyed {unityObject.GetType().FullName} instance");
                            }

                            list.RemoveAt(count);
                            leakCount++;
                            gameEventsCallbacksCount--;
                        }
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }

            // MapView is instantiated on scene loads and is registering an anonymous delegate in the TimingManager,
            // but never removes it. Since MapView hold indirect references to every vessel, this is a major leak.
            if (TimingManager.Instance.IsNotNullOrDestroyed() && TimingManager.Instance.timing5.onLateUpdate != null)
            {
                foreach (Delegate del in TimingManager.Instance.timing5.onLateUpdate.GetInvocationList())
                {
                    if (del.Target is MapView mapview && mapview.IsDestroyed())
                    {
                        TimingManager.Instance.timing5.onLateUpdate -= (TimingManager.UpdateAction)del;
                        leakCount++;
                    }
                }
            }

            // This is part of the resource flow graph mess, and will keep around tons of references
            // to individual parts. Safe to clear on scene switches.
            StronglyConnectedComponentFinder.index = 0;

            if (StronglyConnectedComponentFinder.stack != null)
                StronglyConnectedComponentFinder.stack.Clear();

            if (StronglyConnectedComponentFinder.stronglyConnectedComponentSets != null)
                StronglyConnectedComponentFinder.stronglyConnectedComponentSets.Clear();

            if (StronglyConnectedComponentFinder.stronglyConnectedComponents != null)
                StronglyConnectedComponentFinder.stronglyConnectedComponents.Clear();

            // More stuff stored in static fields for zero good reason...
            AnalyticsUtil.vesselCrew = null;

            string heapAlloc = Utils.HumanReadableBytes(Profiler.GetMonoUsedSizeLong());
            string unityAlloc = Utils.HumanReadableBytes(Profiler.GetTotalAllocatedMemoryLong());
            watch.Stop();

            Debug.Log($"[KSPCF:MemoryLeaks] Cleaned {leakCount} memory leaks in {watch.Elapsed.TotalSeconds:F3}s. GameEvents callbacks : {gameEventsCallbacksCount}. Allocated memory : {heapAlloc} (managed heap), {unityAlloc} (unmanaged)");
        }

        // FlightStateCache is essentially a snapshot of the whole game, and is used for reverting to launch.
        // There is no need to keep it around in other scenes than flight.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            KSPScene currentScene = (KSPScene)scene.buildIndex;
            if (currentScene == KSPScene.MainMenu || currentScene == KSPScene.SpaceCenter || currentScene == KSPScene.TrackingStation || currentScene == KSPScene.Editor)
                FlightDriver.FlightStateCache = null;
        }

        // HookEvents() is called by all ctors. We want to always suppress the PartSet finalizer
        // see the sceneloaded callback for an explaination
        static void PartSet_HookEvents_Postfix(PartSet __instance)
        {
            GC.SuppressFinalize(__instance);
        }

        // Fixing GameEvents.onPlanetariumTargetChanged delegate leak due to a typo
        static bool CommNetVessel_OnDestroy_Prefix(CommNetVessel __instance)
        {
            CommNetNetwork.Remove(__instance.comm);
            GameEvents.CommNet.OnNetworkInitialized.Remove(__instance.OnNetworkInitialized);
            GameEvents.onPlanetariumTargetChanged.Remove(__instance.OnMapFocusChange); // was "Add", typo I guess...
            return false;
        }

        // They just forgot to remove those
        static void StageGroup_OnDestroy_Postfix(StageGroup __instance)
        {
            GameEvents.onDeltaVAppAtmosphereChanged.Remove(__instance.DeltaVAppAtmosphereChanged);
            GameEvents.onDeltaVAppInfoItemsChanged.Remove(__instance.DeltaVCalcsCompleted);
        }

        // They just forgot to remove that one too
        static void ModuleProceduralFairing_OnDestroy_Postfix(ModuleProceduralFairing __instance)
        {
            GameEvents.onVariantApplied.Remove(__instance.onVariantApplied);
        }

        // EffectList is leaking Part references by keeping around this static enumerator
        static void EffectList_Initialize_Postfix()
        {
            EffectList.fxEnumerator = default;
        }

        static void EffectList_OnSave_Postfix()
        {
            EffectList.fxEnumerator = default;
        }

        // CraftSearch is keeping around an indirect reference to the last EditorLogic instance.
        static void CraftSearch_OnDestroy_Postfix()
        {
            CraftSearch.craftBrowserContent = null;
            CraftSearch.craftBrowserDialog = null;
        }

        // More resource flow graph stuff keeping around tons of Part references...
        static void EngineersReport_OnAppDestroy_Postfix()
        {
            EngineersReport.sccFlowGraphUCFinder = null;
        }

        // FlightIntegrator keeping a static reference to the last loaded vessel
        static void FlightIntegrator_OnDestroy_Postfix(FlightIntegrator __instance)
        {
            if (FlightIntegrator.ActiveVesselFI.RefEquals(__instance))
            {
                FlightIntegrator.ActiveVesselFI = null;
            }
        }

        // inventory UI panels controllers are singletons, the destroyed instance is 
        // keeping tons of part references around.

        static void InventoryPanelController_OnDestroy_Postfix()
        {
            InventoryPanelController.Instance = null;
        }

        static void EVAConstructionModeController_OnDestroy_Postfix()
        {
            EVAConstructionModeController.Instance = null;
        }
    }
}
