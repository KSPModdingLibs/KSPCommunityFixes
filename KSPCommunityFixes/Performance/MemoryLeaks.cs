﻿//#define LOG_PART_UPWARD_CACHE_LEAKS

using CommNet;
using HarmonyLib;
using KSP.UI;
using KSP.UI.Screens;
using RUI.Algorithms;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.Performance
{
    class MemoryLeaks : BasePatch
    {
        private static Assembly assemblyCSharp;
        private static readonly Stopwatch watch = new Stopwatch();

        private static bool logDestroyedUnityObjectGameEventsLeaks = false;
        private static bool logGameEventsSubscribers = false;
        private static bool advancedGameEventsLeakDetection = false;
        private static bool forceGCCollect = false;

        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            ConfigNode settingsNode = KSPCommunityFixes.SettingsNode.GetNode("MEMORY_LEAKS_DEBUGGING");
            if (settingsNode != null)
            {
                settingsNode.TryGetValue("ForceGCCollect", ref forceGCCollect);
                settingsNode.TryGetValue("LogDestroyedUnityObjectGameEventsLeaks", ref logDestroyedUnityObjectGameEventsLeaks);
                settingsNode.TryGetValue("AdvancedGameEventsLeakDetection", ref advancedGameEventsLeakDetection);
                settingsNode.TryGetValue("LogGameEventsSubscribers", ref logGameEventsSubscribers);
            }

#if DEBUG
            logDestroyedUnityObjectGameEventsLeaks = true;
            advancedGameEventsLeakDetection = true;
            forceGCCollect = true;
#endif

            // removing PartSet finalizers

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartSet), nameof(PartSet.HookEvents))));

            // Specific GameEvents leaks

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(CommNetVessel), nameof(CommNetVessel.OnDestroy))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(StageGroup), nameof(StageGroup.OnDestroy))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleProceduralFairing), nameof(ModuleProceduralFairing.OnDestroy))));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleScienceExperiment), nameof(ModuleScienceExperiment.OnAwake))));

            // protopartsnapshot leaks a reference to a copy of the root part because of how it's created

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ProtoPartSnapshot), nameof(ProtoPartSnapshot.Load))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ProtoPartSnapshot), nameof(ProtoPartSnapshot.ConfigurePart))));

            // Parts and vessels don't unhook themselves from things when they're destroyed or unloaded

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.OnDestroy))));
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.ReleaseAutoStruts))));
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Vessel), nameof(Vessel.Unload))));

            // Kerbals

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Kerbal), nameof(Kerbal.OnDestroy))));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(InternalSeat), nameof(InternalSeat.DespawnCrew))));

            // EffectList dictionary enumerator leaks

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EffectList), nameof(EffectList.Initialize))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EffectList), nameof(EffectList.OnSave))));

            // Pooled Audio onRelease delegate leak

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(AudioMultiPooledFX.PooledAudioSource), nameof(AudioMultiPooledFX.PooledAudioSource.Reset))));

            // various static fields keeping deep reference stacks around...

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(CraftSearch), nameof(CraftSearch.OnDestroy))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EngineersReport), nameof(EngineersReport.OnAppDestroy))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(FlightIntegrator), nameof(FlightIntegrator.OnDestroy))));

            // Various singleton MonoBehaviours are scene-specific. They are using a static accessor that isn't nulled
            // when the instance is destroyed, causing anything still referenced to never be GCed.
            // There are way too many of them t fix them all, but we try to patch the worst offenders here.

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(InventoryPanelController), nameof(InventoryPanelController.OnDestroy))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EVAConstructionModeController), nameof(EVAConstructionModeController.OnDestroy))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(FuelFlowOverlay), nameof(FuelFlowOverlay.OnDestroy))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.RemoveApplication))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ApplicationLauncher), nameof(ApplicationLauncher.RemoveModApplication)),
                nameof(ApplicationLauncher_RemoveApplication_Postfix)));

            // KSC Vessel Markers inherit from this class. they don't get cleaned up properly
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(AnchoredDialog), nameof(AnchoredDialog.Terminate))));

            // general cleanup on scene switches

            assemblyCSharp = Assembly.GetAssembly(typeof(Part));
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            // fix some previously silently failing code that was refering to dead instances, 
            // and is throwing because we actually remove references to those dead instances

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(UIPartActionControllerInventory), nameof(UIPartActionControllerInventory.UpdateCursorOverPAWs))));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(UIPartActionInventory), nameof(UIPartActionInventory.Update))));
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

            // these are big static dictionaries that track a lot of parts and gameobjects.  Reset them here since all of them must have been destroyed
            FlightGlobals.ResetObjectPartPointerUpwardsCache();
            FlightGlobals.ResetObjectPartUpwardsCache();
            Part.allParts.Clear();

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

            // another leak with a non-unity-derived class, AlarmTypeManeuver...
            for (int i = GameEvents.onManeuverAdded.events.Count; i-- > 0;)
            {
                if (GameEvents.onManeuverAdded.events[i].originator is AlarmTypeManeuver)
                {
                    GameEvents.onManeuverAdded.events.RemoveAt(i);
                    leakCount++;
                }
            }
            for (int i = GameEvents.onManeuverRemoved.events.Count; i-- > 0;)
            {
                if (GameEvents.onManeuverRemoved.events[i].originator is AlarmTypeManeuver)
                {
                    GameEvents.onManeuverRemoved.events.RemoveAt(i);
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
                            Type originType = unityObject.GetType();
                            bool remove = ShouldCleanType(originType);
                            LogUnityObjectGameEventLeak(gameEvent.EventName, originType, remove);

                            if (remove)
                            {
                                eventVoid.events.RemoveAt(i);
                                leakCount++;
                                gameEventsCallbacksCount--;
                            }
                        }
                    }

                    continue;
                }

                // ScenarioUpgradeableFacilities is using a pattern where it adds a onLevelWasLoaded callback from its OnDestroy() to clean up some
                // static stuff on the next scene load, and it also remove the delegate from the event while doing so. So it's not actually a leak
                // and we add a specific case to exclude it.
                if (gameEvent is EventData<GameScenes> onLevelWasLoaded && onLevelWasLoaded.eventName == "onNewGameLevelLoadRequestWasSanctionedAndActioned")
                {
                    gameEventsCallbacksCount += onLevelWasLoaded.events.Count;
                    for (int i = onLevelWasLoaded.events.Count; i-- > 0;)
                    {
                        if (onLevelWasLoaded.events[i].originator is UnityEngine.Object unityObject && unityObject.IsDestroyed() && !(unityObject is ScenarioUpgradeableFacilities))
                        {
                            Type originType = unityObject.GetType();
                            bool remove = ShouldCleanType(originType);
                            LogUnityObjectGameEventLeak(gameEvent.EventName, originType, remove);
                            if (remove)
                            {
                                onLevelWasLoaded.events.RemoveAt(i);
                                leakCount++;
                                gameEventsCallbacksCount--;
                            }
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
                            Type originType = unityObject.GetType();
                            bool remove = ShouldCleanType(originType);
                            LogUnityObjectGameEventLeak(gameEvent.EventName, originType, remove);
                            if (remove)
                            {
                                list.RemoveAt(count);
                                leakCount++;
                                gameEventsCallbacksCount--;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }

            // remove destroyed MapView event handlers
            leakCount += CleanDelegateHandlers("MapView.OnEnterMapView", ref MapView.OnEnterMapView);
            leakCount += CleanDelegateHandlers("MapView.OnExitMapView", ref MapView.OnExitMapView);

            // MapView is instantiated on scene loads and is registering an anonymous delegate in the TimingManager,
            // but never removes it. Since MapView hold indirect references to every vessel, this is a major leak.
            if (TimingManager.Instance.IsNotNullOrDestroyed())
            {
                // since the timing manager's delegates are properties, we can't directly modify them except through the set method...
                leakCount += TimingManagerCleanObject("TimingManager.Instance", TimingManager.Instance);
                leakCount += TimingManagerCleanObject("TimingManager.Instance.timing0", TimingManager.Instance.timing0);
                leakCount += TimingManagerCleanObject("TimingManager.Instance.timing1", TimingManager.Instance.timing1);
                leakCount += TimingManagerCleanObject("TimingManager.Instance.timing2", TimingManager.Instance.timing2);
                leakCount += TimingManagerCleanObject("TimingManager.Instance.timing3", TimingManager.Instance.timing3);
                leakCount += TimingManagerCleanObject("TimingManager.Instance.timing4", TimingManager.Instance.timing4);
                leakCount += TimingManagerCleanObject("TimingManager.Instance.timing5", TimingManager.Instance.timing5);
                leakCount += TimingManagerCleanObject("TimingManager.Instance.timingPre", TimingManager.Instance.timingPre);
                leakCount += TimingManagerCleanObject("TimingManager.Instance.timingFI", TimingManager.Instance.timingFI);
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

            if (advancedGameEventsLeakDetection)
            {
                AdvancedLeakDetection(currentScene);
            }

            if (logGameEventsSubscribers)
            {
                LogGameEventsSuscribers();
            }

            if (forceGCCollect)
            {
                GC.Collect();
            }

            string heapAlloc = StaticHelpers.HumanReadableBytes(Profiler.GetMonoUsedSizeLong());
            string unityAlloc = StaticHelpers.HumanReadableBytes(Profiler.GetTotalAllocatedMemoryLong());
            watch.Stop();

            Debug.Log($"[KSPCF:MemoryLeaks] Leaving scene \"{currentScene}\", cleaned {leakCount} memory leaks in {watch.Elapsed.TotalSeconds:F3}s. GameEvents callbacks : {gameEventsCallbacksCount}. Allocated memory : {heapAlloc} (managed heap), {unityAlloc} (unmanaged)");
        }

        // Note : we only remove objects if they are declared by the stock assembly, or a PartModule, or a VesselModule.
        // Some mods are relying on a singleton pattern by instantiating a KSPAddon once, registering GameEvents there and
        // relying on those being called on the dead instance to manipulate static data... 
        // Questionable at best, but since it is functionally valid and seems relatively common, we can't take the risk to remove them.
        // Those cases will still be logged when the LogDestroyedUnityObjectGameEventsLeaks flag is set in settings.
        static bool ShouldCleanType(Type type)
        {
            return type.Assembly == assemblyCSharp || typeof(PartModule).IsAssignableFrom(type) || typeof(VesselModule).IsAssignableFrom(type);
        }

        static int TimingManagerCleanObject(string name, object obj)
        {
            int leakCount = 0;
            var delegateProperties = obj.GetType().GetProperties();
            foreach (var property in delegateProperties)
            {
                if (property.PropertyType == typeof(TimingManager.UpdateAction))
                {
                    var del = (TimingManager.UpdateAction)property.GetValue(obj);
                    leakCount += CleanDelegateHandlers(name + "." + property.Name, ref del);
                    property.SetValue(obj, del);
                }
            }

            return leakCount;
        }

        static int CleanDelegateHandlers<T>(string eventName, ref T del) where T : Delegate
        {
            int leakCount = 0;
            if (del == null) return 0;
            foreach (Delegate handler in del.GetInvocationList())
            {
                if (handler.Target is UnityEngine.Object obj && obj.IsDestroyed())
                {
                    bool remove = ShouldCleanType(obj.GetType());

                    LogUnityObjectGameEventLeak(eventName, obj.GetType(), remove);

                    if (remove)
                    {
                        del = (T)Delegate.Remove(del, handler);
                        leakCount++;
                    }
                }
            }
            return leakCount;
        }

        static string TypeNameForLog(Type origin)
        {
            string typeName = origin.Assembly.GetName().Name + ":";
            if (origin.IsNested)
                typeName += origin.DeclaringType.Name + "." + origin.Name;
            else
                typeName += origin.Name;

            return typeName;
        }

        static void LogUnityObjectGameEventLeak(string eventName, Type origin, bool removed)
        {
            if (!logDestroyedUnityObjectGameEventsLeaks)
                return;

            string typeName = TypeNameForLog(origin);

            if (removed)
                Debug.Log($"[KSPCF:MemoryLeaks] Removed a {eventName} callback owned by a destroyed {typeName} instance");
            else
                Debug.Log($"[KSPCF:MemoryLeaks] A destroyed {typeName} instance is owning a {eventName} callback. No action has been taken, but unless this mod is relying on this pattern, this is likely a memory leak.");
        }

        static void LogGameEventsSuscribers()
        {
            Dictionary<Type, int> eventSubscribers = new Dictionary<Type, int>();
            StringBuilder sb = new StringBuilder();

            sb.Append("[KSPCF:MemoryLeaks] Logging GameEvents delegates :");

            foreach (BaseGameEvent gameEvent in BaseGameEvent.eventsByName.Values)
            {
                try
                {
                    IList list = (IList)gameEvent.GetType().GetField(nameof(EventVoid.events), BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gameEvent);
                    int count = list.Count;
                    if (count == 0)
                        continue;

                    sb.Append($"\n- \"{gameEvent.EventName}\" : {count} subscribers");

                    FieldInfo originatorField = list[0].GetType().GetField(nameof(EventVoid.EvtDelegate.originator));
                    while (count-- > 0)
                    {
                        Type originatorType = originatorField.GetValue(list[count]).GetType();

                        if (eventSubscribers.TryGetValue(originatorType, out int subscriberCount))
                        {
                            eventSubscribers[originatorType] = subscriberCount + 1;
                        }
                        else
                        {
                            eventSubscribers.Add(originatorType, 1);
                        }
                    }

                    foreach (KeyValuePair<Type, int> subscriber in eventSubscribers)
                    {
                        Type type = subscriber.Key;
                        sb.Append($"\n  - {subscriber.Value} from {type.Assembly.GetName().Name}:");
                        if (type.IsNested)
                            sb.Append($"{type.DeclaringType.Name}.{type.Name}");
                        else
                            sb.Append(type.Name);
                    }

                    eventSubscribers.Clear();
                }
                catch (Exception)
                {
                    eventSubscribers.Clear();
                    continue;
                }
            }

            Debug.Log(sb.ToString());
        }

        private static Dictionary<KSPScene, Dictionary<string, Dictionary<Type, int>>> advancedLeakDetection;

        static void AdvancedLeakDetection(KSPScene exitedScene)
        {
            if (advancedLeakDetection == null)
                advancedLeakDetection = new Dictionary<KSPScene, Dictionary<string, Dictionary<Type, int>>>();

            Dictionary<string, Dictionary<Type, int>> currentSceneGE = new Dictionary<string, Dictionary<Type, int>>(BaseGameEvent.eventsByName.Count);

            foreach (BaseGameEvent gameEvent in BaseGameEvent.eventsByName.Values)
            {
                try
                {
                    IList list = (IList)gameEvent.GetType().GetField(nameof(EventVoid.events), BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gameEvent);
                    int count = list.Count;
                    if (count == 0)
                        continue;

                    Dictionary<Type, int> subscribers = new Dictionary<Type, int>(count);
                    currentSceneGE.Add(gameEvent.EventName, subscribers);

                    FieldInfo originatorField = list[0].GetType().GetField(nameof(EventVoid.EvtDelegate.originator));
                    while (count-- > 0)
                    {
                        Type originatorType = originatorField.GetValue(list[count]).GetType();

                        if (subscribers.TryGetValue(originatorType, out int subscriberCount))
                            subscribers[originatorType] = subscriberCount + 1;
                        else
                            subscribers.Add(originatorType, 1);
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }

            if (advancedLeakDetection.TryGetValue(exitedScene, out Dictionary<string, Dictionary<Type, int>> lastSceneGE))
            {
                StringBuilder sb = new StringBuilder();
                bool leaksDetected = false;
                sb.Append($"[KSPCF:MemoryLeaks] Potential GameEvents leaks for scene {exitedScene} :");

                foreach (KeyValuePair<string, Dictionary<Type, int>> currentGE in currentSceneGE)
                {
                    bool gameEventLogged = false;
                    if (lastSceneGE.TryGetValue(currentGE.Key, out Dictionary<Type, int> lastSuscribers))
                    {
                        foreach (KeyValuePair<Type, int> subscriber in currentGE.Value)
                        {
                            if (lastSuscribers.TryGetValue(subscriber.Key, out int lastSubscriptionCount) && lastSubscriptionCount < subscriber.Value)
                            {
                                leaksDetected = true;

                                if (!gameEventLogged)
                                {
                                    int lastSubscribersCount = lastSuscribers.Sum(x => x.Value);
                                    int newSubscribersCount = currentGE.Value.Sum(x => x.Value);
                                    sb.Append($"\n- \"{currentGE.Key}\" had {lastSubscribersCount} subscribers on last scene switch, now it has {newSubscribersCount}. Those subscribers are likely leaking :");
                                    gameEventLogged = true;
                                }

                                Type type = subscriber.Key;
                                sb.Append($"\n  - {type.Assembly.GetName().Name}:");
                                if (type.IsNested)
                                    sb.Append($"{type.DeclaringType.Name}.{type.Name}");
                                else
                                    sb.Append(type.Name);

                                sb.Append($" - Subscriptions : {lastSubscriptionCount}/{subscriber.Value} (last/now)");
                            }
                        }
                    }
                }

                if (!leaksDetected)
                    sb.Append(" no leaks detected !");

                Debug.Log(sb.ToString());
            }

            advancedLeakDetection[exitedScene] = currentSceneGE;
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
            GameEvents.onVariantsAdded.Remove(__instance.onVariantsAdded);
        }

        // Not really a leak, but prevent a bunch of flight related events to be registered from the prefabs.
        // see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/153
        static bool ModuleScienceExperiment_OnAwake_Prefix()
        {
            return HighLogic.LoadedSceneIsFlight;
        }

        // Because root parts are added directly to the Vessel gameobject, they're not just a clone of a part prefab
        // instead, ProtoPartSnapshot.Load will clone the prefab, then copy the components and properties over to the Vessel GameObject
        // Then it deletes the clone it made.
        // This leaves a few references pointing to the now-deleted clone, because unlike regular serialization copying the properties won't fix the references to point to the real live part

        static void ProtoPartSnapshot_Load_Postfix(Part __result, bool loadAsRootPart)
        {
            if (loadAsRootPart)
            {
                foreach (var attachNode in __result.attachNodes)
                {
                    attachNode.owner = __result;
                }
            }
        }

        static void ProtoPartSnapshot_ConfigurePart_Postfix(ProtoPartSnapshot __instance)
        {
            // ConfigurePart destroys this gameobject but doesn't clear the reference
            __instance.rootPartPrefab = null;
        }

        // When a part is destroyed, we need to clean up all the references that are pointing back to this part, mostly from the ProtoPartSnapshot because that will live longer than this part
        static void Part_OnDestroy_Postfix(Part __instance)
        {
            // if our protopartsnapshot is still pointing at this part, we need to clear all of its references
            if (__instance.protoPartSnapshot != null && ReferenceEquals(__instance.protoPartSnapshot.partRef, __instance))
            {
                foreach (var moduleSnapshot in __instance.protoPartSnapshot.modules)
                {
                    moduleSnapshot.moduleRef = null;
                }

                foreach (var partResourceSnapshot in __instance.protoPartSnapshot.resources)
                {
                    partResourceSnapshot.resourceRef = null;
                }

                __instance.protoPartSnapshot.partRef = null;
            }

            // clear some additional containers to limit the impact of leaks and make reports less noisy
            __instance.modules?.modules?.Clear();
            __instance.children?.Clear();
            __instance.parent = __instance.potentialParent = null;
            __instance.internalModel = null;

#if LOG_PART_UPWARD_CACHE_LEAKS
            if (FlightGlobals.objectToPartUpwardsCache.Values.Contains(__instance))
            {
                Debug.LogError($"Destroying part {__instance.GetInstanceID()} while it's still in the objectToPartUpwardsCache");
            }
#endif
        }

        static void Part_ReleaseAutoStruts_Postfix(Part __instance)
        {
            __instance.autoStrutCacheVessel = null;
            __instance.autoStrutVessel = null;
        }

        // PartSets are awful as noted in OnSceneUnloaded.  But vessel unloading is also a good time to clean these up instead of waiting for scene switch

        // general unhooking of stuff when unloading a vessel.  Note that vessel gameobjects are not usually destroyed until the vessel itself is
        static void Vessel_Unload_Postfix(Vessel __instance)
        {
            __instance.rootPart = null;
            __instance.referenceTransformPart = null;
            __instance.referenceTransformPartRecall = null;
            __instance.GroundContacts?.Clear();
            __instance._suspensionLoadBalancer?.suspensionModules?.Clear();
            __instance.dockingPorts?.Clear();

            // the flight integrator holds onto a lot of references, which will be repopulated when the vessel is loaded again
            // clear them all now

            if (__instance._fi != null)
            {
                __instance._fi.partRef = null;
                __instance._fi.partThermalDataList?.Clear();
                __instance._fi.partThermalDataListSkin?.Clear();
                __instance._fi.partThermalDataCount = 0;
                __instance._fi.partCount = 0;
                __instance._fi.compoundParts?.Clear();
                __instance._fi.occlusionConv?.Clear();
                __instance._fi.occlusionSun?.Clear();
                __instance._fi.occlusionBody?.Clear();
                __instance._fi.overheatModules?.Clear();
                __instance._fi.previewModules?.Clear();
                __instance._fi.occludersConvection = null;
                __instance._fi.occludersSun = null;
                __instance._fi.occludersBody = null;
                __instance._fi.occludersBodyCount = 0;
                __instance._fi.occludersConvectionCount = 0;
                __instance._fi.occludersSunCount = 0;
            }

            // not sure about
            // objectUnderVessel

            FlightGlobals.ResetObjectPartUpwardsCache();
            FlightGlobals.ResetObjectPartPointerUpwardsCache();

            for (int i = GameEvents.onPartResourceFlowStateChange.events.Count; i-- > 0;)
            {
                if (GameEvents.onPartResourceFlowStateChange.events[i].originator is PartSet partSet)
                {
                    if (partSet.vessel == __instance)
                    {
                        GameEvents.onPartResourceFlowStateChange.events.RemoveAt(i);
                    }
                }
            }

            for (int i = GameEvents.onPartResourceFlowModeChange.events.Count; i-- > 0;)
            {
                if (GameEvents.onPartResourceFlowModeChange.events[i].originator is PartSet partSet)
                {
                    if (partSet.vessel == __instance)
                    {
                        GameEvents.onPartResourceFlowModeChange.events.RemoveAt(i);
                    }
                }
            }

            __instance.resourcePartSet = null;
            __instance.simulationResourcePartSet = null;
            __instance.crossfeedSets.Clear();
            __instance.simulationCrossfeedSets.Clear();
        }

        static void Kerbal_OnDestroy_Postfix(Kerbal __instance)
        {
            if (ReferenceEquals(__instance.protoCrewMember.KerbalRef, __instance))
            {
                __instance.protoCrewMember.KerbalRef = null;
            }
        }

        static void InternalSeat_DespawnCrew_Prefix(InternalSeat __instance)
        {
            // DespawnCrew merely destroys the kerbal, but it doesn't unhook the protocrewmember from this seat
            // ProtoCrewMembers will live until the next save game is loaded, so this can leak parts and vessels
            if (__instance.kerbalRef.IsNotNullRef() && __instance.kerbalRef.protoCrewMember.seat.RefEquals(__instance))
            {
                __instance.kerbalRef.protoCrewMember.seat = null;
            }
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

        // PooledAudioSource leaks references to parts through its onRelease delegate
        static void PooledAudioSource_Reset_Postfix(AudioMultiPooledFX.PooledAudioSource pooledAudioSource)
        {
            pooledAudioSource.onRelease = null;
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

        // patch singletons keeping around ton of dead stuff

        static void InventoryPanelController_OnDestroy_Postfix()
        {
            InventoryPanelController.Instance = null;
        }

        static void EVAConstructionModeController_OnDestroy_Postfix()
        {
            EVAConstructionModeController.Instance = null;
        }

        static void FuelFlowOverlay_OnDestroy_Postfix()
        {
            FuelFlowOverlay.instance = null;
        }

        static void ApplicationLauncher_RemoveApplication_Postfix(ApplicationLauncherButton button)
        {
            if (ApplicationLauncher.Instance.IsNotNullRef() && object.ReferenceEquals(ApplicationLauncher.Instance.lastPinnedButton, button.toggleButton))
            {
                ApplicationLauncher.Instance.lastPinnedButton = null;
            }
        }

        // KSC Vessel Marker - it seems like the logic in this has an `else` where it shouldn't.
        static void AnchoredDialog_Terminate_Postfix(AnchoredDialog __instance)
        {
            __instance.gameObject.DestroyGameObject();
        }

        // patch previously silently failing code that now "properly" throw exceptions when attempting to access dead instances
        static bool UIPartActionControllerInventory_UpdateCursorOverPAWs_Prefix(UIPartActionControllerInventory __instance)
        {
            bool flag = false;
            for (int i = 0; i < __instance.pawController.windows.Count; i++)
            {
                flag |= __instance.pawController.windows[i].Hover;
            }
            if (HighLogic.LoadedSceneIsFlight && EVAConstructionModeController.Instance.IsNotNullOrDestroyed())
            {
                flag |= EVAConstructionModeController.Instance.Hover;
            }
            else if (HighLogic.LoadedSceneIsEditor && InventoryPanelController.Instance.IsNotNullOrDestroyed())
            {
                flag |= InventoryPanelController.Instance.Hover;
            }
            __instance.IsCursorOverAnyPAWOrCargoPane = flag;
            return false;
        }

        static bool UIPartActionInventory_Update_Prefix()
        {
            switch (HighLogic.LoadedScene)
            {
                case GameScenes.EDITOR:
                    if (InventoryPanelController.Instance.IsNullOrDestroyed())
                        return false;
                    break;
                case GameScenes.FLIGHT:
                    if (EVAConstructionModeController.Instance.IsNullOrDestroyed())
                        return false;
                    break;
            }

            return true;
        }
    }
}
