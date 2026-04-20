using System;
using System.Collections.Generic;
using System.Reflection;
using Contracts;
using Expansions.Missions;
using Expansions.Serenity;
using Expansions.Serenity.DeployedScience.Runtime;
using FinePrint;
using HarmonyLib;
using KSP.UI.Screens;
using KSP.UI.Screens.Settings.Controls;
using KSPAchievements;
using Unity.Profiling;
using Upgradeables;
using UnityEngine;
using UnityEngine.Profiling;
using static GameEvents;
using System.Linq.Expressions;
using System.Linq;
using System.Reflection.Emit;

namespace KSPCommunityFixes.Modding
{
    public class EventProfilerMarkers : BasePatch
    {
        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(EventVoid), nameof(EventVoid.Fire), nameof(EventVoid_Fire_Override));

            foreach (var type in ValueTypeEventData1)
                AddPatch(CreatePatch(type));

            foreach (var type in ValueTypeEventData2)
                AddPatch(CreatePatch(type));

            foreach (var type in ValueTypeEventData3)
                AddPatch(CreatePatch(type));

            foreach (var type in ValueTypeEventData4)
                AddPatch(CreatePatch(type));
        }

        #region CreatePatchOverride
        static readonly ModuleBuilder PatchModule;

        static EventProfilerMarkers()
        {
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("KSPCommunityFixes_EventProfilerPatches"),
                AssemblyBuilderAccess.Run);
            PatchModule = assemblyBuilder.DefineDynamicModule("PatchModule");
        }

        static int patchCounter;

        static string PatchName(string baseName, Type[] tparams)
        {
            return baseName + "<" + string.Join(", ", tparams.Select(t => t.Name)) + ">";
        }

        static PatchInfo CreatePatch(Type type)
        {
            var tparams = type.GenericTypeArguments;
            var instance = Expression.Parameter(type, "__instance");

            var fparams = new ParameterExpression[tparams.Length + 1];
            fparams[0] = instance;
            for (int i = 0; i < tparams.Length; ++i)
                fparams[i + 1] = Expression.Parameter(tparams[i], $"data{i}");

            MethodInfo generic = tparams.Length switch
            {
                1 => SymbolExtensions.GetMethodInfo(() => EventData1_Fire_Override<object>(null, null)),
                2 => SymbolExtensions.GetMethodInfo(() => EventData2_Fire_Override<object, object>(null, null, null)),
                3 => SymbolExtensions.GetMethodInfo(() => EventData3_Fire_Override<object, object, object>(null, null, null, null)),
                4 => SymbolExtensions.GetMethodInfo(() => EventData4_Fire_Override<object, object, object, object>(null, null, null, null, null)),
                _ => throw new NotImplementedException()
            };

            var method = generic
                .GetGenericMethodDefinition()
                .MakeGenericMethod(tparams);

            var body = Expression.Call(null, method, fparams);
            var lambda = Expression.Lambda(body, fparams);

            var patchName = PatchName(generic.Name, tparams);
            var typeBuilder = PatchModule.DefineType($"Patch_{patchCounter++}", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var paramTypes = fparams.Select(p => p.Type).ToArray();
            var methodBuilder = typeBuilder.DefineMethod(patchName, MethodAttributes.Public | MethodAttributes.Static, typeof(void), paramTypes);

            for (int i = 0; i < fparams.Length; i++)
                methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, fparams[i].Name);

            lambda.CompileToMethod(methodBuilder);
            var builtType = typeBuilder.CreateType();
            var compiled = builtType.GetMethod(patchName);

            return new PatchInfo(PatchType.Override, AccessTools.Method(type, "Fire"), patchName)
            {
                patchMethod = compiled
            };
        }
        #endregion

        static readonly Dictionary<Type, ProfilerMarker> EventMarkers = new Dictionary<Type, ProfilerMarker>();
        static readonly Dictionary<MethodInfo, ProfilerMarker> MethodMarkers = new Dictionary<MethodInfo, ProfilerMarker>();

        static readonly Dictionary<Type, MethodInfo> FireMethods = new Dictionary<Type, MethodInfo>();

        static ProfilerMarker GetTypeMarker(BaseGameEvent evt)
        {
            if (EventMarkers.TryGetValue(evt.GetType(), out var marker))
                return marker;

            marker = new ProfilerMarker(evt.EventName);
            EventMarkers.Add(evt.GetType(), marker);
            return marker;
        }

        static ProfilerMarker GetMethodMarker(Delegate del)
        {
            var method = del.Method;
            if (MethodMarkers.TryGetValue(method, out var marker))
                return marker;

            marker = new ProfilerMarker($"{method.DeclaringType}.{method.Name}");
            MethodMarkers.Add(method, marker);
            return marker;
        }

        static MethodInfo GetConcreteFireMethod(BaseGameEvent evt)
        {
            if (FireMethods.TryGetValue(evt.GetType(), out var method))
                return method;

            method = GetConcreteFireMethodSlow(evt);
            FireMethods.Add(evt.GetType(), method);
            return method;
        }

        static MethodInfo GetConcreteFireMethodSlow(BaseGameEvent evt)
        {
            var type = evt.GetType();

            if (type == typeof(EventVoid))
                return SymbolExtensions.GetMethodInfo<EventVoid>(evt => Fire0(evt));
            if (!type.IsGenericType)
                throw new Exception($"Unsupported event data type {type.Name}");

            var generic = type.GetGenericTypeDefinition();
            var tparams = type.GenericTypeArguments;

            if (generic == typeof(EventData<>))
            {
                return SymbolExtensions
                    .GetMethodInfo<EventData<object>>(evt => Fire1(evt, null))
                    .GetGenericMethodDefinition()
                    .MakeGenericMethod(tparams);
            }

            if (generic == typeof(EventData<,>))
            {
                return SymbolExtensions
                    .GetMethodInfo<EventData<object, object>>(evt => Fire2(evt, null, null))
                    .GetGenericMethodDefinition()
                    .MakeGenericMethod(tparams);
            }

            if (generic == typeof(EventData<,,>))
            {
                return SymbolExtensions
                    .GetMethodInfo<EventData<object, object, object>>(evt => Fire3(evt, null, null, null))
                    .GetGenericMethodDefinition()
                    .MakeGenericMethod(tparams);
            }

            if (generic == typeof(EventData<,,,>))
            {
                return SymbolExtensions
                    .GetMethodInfo<EventData<object, object, object, object>>(evt => Fire4(evt, null, null, null, null))
                    .GetGenericMethodDefinition()
                    .MakeGenericMethod(tparams);
            }

            throw new NotSupportedException($"Cannot get the correct method for event type {type.Name}");
        }

        static void Fire0(EventVoid instance)
        {
            using var scope = GetTypeMarker(instance).Auto();

            Profiler.BeginSample(instance.eventName);

            if (instance.debugEvent || GameEventsBase.debugEvents)
                Debug.Log("EventManager: Firing event '" + instance.eventName + "'");

            instance.numEventsFiring++;
            instance.eventsClone.Clear();
            instance.eventsClone.AddRange(instance.events);
            int count = instance.eventsClone.Count;

            while (count-- > 0)
            {
                var evt = instance.eventsClone[count];
                if (evt.originator is null)
                {
                    Debug.Log("EventManager: Removing event '" + instance.eventName + "'for object of type '" + instance.events[count].originatorType + "' as object is null.");
                    instance.events.Remove(instance.eventsClone[count]);
                    instance.eventsClone.RemoveAt(count);
                    continue;
                }

                try
                {
                    using var method = GetMethodMarker(evt.evt).Auto();
                    evt.evt();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception handling event {instance.eventName} in class {instance.eventsClone[count].originatorType}:" + ex);
                    Debug.LogException(ex);
                }
            }

            instance.numEventsFiring--;
            if (instance.numEventsFiring <= 0)
            {
                instance.eventsClone.Clear();
                instance.numEventsFiring = 0;
            }
        }

        static void Fire1<T>(EventData<T> instance, T data)
        {
            using var scope = GetTypeMarker(instance).Auto();

            Profiler.BeginSample(instance.eventName);

            if (instance.debugEvent || GameEventsBase.debugEvents)
                Debug.Log("EventManager: Firing event '" + instance.eventName + "'");

            instance.numEventsFiring++;
            instance.eventsClone.Clear();
            instance.eventsClone.AddRange(instance.events);
            int count = instance.eventsClone.Count;

            while (count-- > 0)
            {
                var evt = instance.eventsClone[count];
                if (evt.originator is null)
                {
                    Debug.Log("EventManager: Removing event '" + instance.eventName + "'for object of type '" + instance.events[count].originatorType + "' as object is null.");
                    instance.events.Remove(instance.eventsClone[count]);
                    instance.eventsClone.RemoveAt(count);
                    continue;
                }

                try
                {
                    using var method = GetMethodMarker(evt.evt).Auto();
                    evt.evt(data);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception handling event {instance.eventName} in class {instance.eventsClone[count].originatorType}:" + ex);
                    Debug.LogException(ex);
                }
            }

            instance.numEventsFiring--;
            if (instance.numEventsFiring <= 0)
            {
                instance.eventsClone.Clear();
                instance.numEventsFiring = 0;
            }
        }

        static void Fire2<T1, T2>(EventData<T1, T2> instance, T1 arg1, T2 arg2)
        {
            using var scope = GetTypeMarker(instance).Auto();

            Profiler.BeginSample(instance.eventName);

            if (instance.debugEvent || GameEventsBase.debugEvents)
                Debug.Log("EventManager: Firing event '" + instance.eventName + "'");

            instance.numEventsFiring++;
            instance.eventsClone.Clear();
            instance.eventsClone.AddRange(instance.events);
            int count = instance.eventsClone.Count;

            while (count-- > 0)
            {
                var evt = instance.eventsClone[count];
                if (evt.originator is null)
                {
                    Debug.Log("EventManager: Removing event '" + instance.eventName + "'for object of type '" + instance.events[count].originatorType + "' as object is null.");
                    instance.events.Remove(instance.eventsClone[count]);
                    instance.eventsClone.RemoveAt(count);
                    continue;
                }

                try
                {
                    using var method = GetMethodMarker(evt.evt).Auto();
                    evt.evt(arg1, arg2);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception handling event {instance.eventName} in class {instance.eventsClone[count].originatorType}:" + ex);
                    Debug.LogException(ex);
                }
            }

            instance.numEventsFiring--;
            if (instance.numEventsFiring <= 0)
            {
                instance.eventsClone.Clear();
                instance.numEventsFiring = 0;
            }
        }

        static void Fire3<T1, T2, T3>(EventData<T1, T2, T3> instance, T1 arg1, T2 arg2, T3 arg3)
        {
            using var scope = GetTypeMarker(instance).Auto();

            Profiler.BeginSample(instance.eventName);

            if (instance.debugEvent || GameEventsBase.debugEvents)
                Debug.Log("EventManager: Firing event '" + instance.eventName + "'");

            instance.numEventsFiring++;
            instance.eventsClone.Clear();
            instance.eventsClone.AddRange(instance.events);
            int count = instance.eventsClone.Count;

            while (count-- > 0)
            {
                var evt = instance.eventsClone[count];
                if (evt.originator is null)
                {
                    Debug.Log("EventManager: Removing event '" + instance.eventName + "'for object of type '" + instance.events[count].originatorType + "' as object is null.");
                    instance.events.Remove(instance.eventsClone[count]);
                    instance.eventsClone.RemoveAt(count);
                    continue;
                }

                try
                {
                    using var method = GetMethodMarker(evt.evt).Auto();
                    evt.evt(arg1, arg2, arg3);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception handling event {instance.eventName} in class {instance.eventsClone[count].originatorType}:" + ex);
                    Debug.LogException(ex);
                }
            }

            instance.numEventsFiring--;
            if (instance.numEventsFiring <= 0)
            {
                instance.eventsClone.Clear();
                instance.numEventsFiring = 0;
            }
        }

        static void Fire4<T1, T2, T3, T4>(EventData<T1, T2, T3, T4> instance, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            using var scope = GetTypeMarker(instance).Auto();

            Profiler.BeginSample(instance.eventName);

            if (instance.debugEvent || GameEventsBase.debugEvents)
                Debug.Log("EventManager: Firing event '" + instance.eventName + "'");

            instance.numEventsFiring++;
            instance.eventsClone.Clear();
            instance.eventsClone.AddRange(instance.events);
            int count = instance.eventsClone.Count;

            while (count-- > 0)
            {
                var evt = instance.eventsClone[count];
                if (evt.originator is null)
                {
                    Debug.Log("EventManager: Removing event '" + instance.eventName + "'for object of type '" + instance.events[count].originatorType + "' as object is null.");
                    instance.events.Remove(instance.eventsClone[count]);
                    instance.eventsClone.RemoveAt(count);
                    continue;
                }

                try
                {
                    using var method = GetMethodMarker(evt.evt).Auto();
                    evt.evt(arg1, arg2, arg3, arg4);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception handling event {instance.eventName} in class {instance.eventsClone[count].originatorType}:" + ex);
                    Debug.LogException(ex);
                }
            }

            instance.numEventsFiring--;
            if (instance.numEventsFiring <= 0)
            {
                instance.eventsClone.Clear();
                instance.numEventsFiring = 0;
            }
        }

        static void EventVoid_Fire_Override(EventVoid instance) => Fire0(instance);

        static readonly object[] Args1 = new object[2];
        public static void EventData1_Fire_Override<T>(EventData<T> instance, T data)
        {
            try
            {
                Args1[0] = instance;
                Args1[1] = data;

                var method = GetConcreteFireMethod(instance);
                method.Invoke(null, Args1);
            }
            finally
            {
                Array.Clear(Args1, 0, Args1.Length);
            }
        }

        static readonly object[] Args2 = new object[3];
        public static void EventData2_Fire_Override<T, U>(EventData<T, U> instance, T data0, U data1)
        {
            try
            {
                Args2[0] = instance;
                Args2[1] = data0;
                Args2[2] = data1;

                var method = GetConcreteFireMethod(instance);
                method.Invoke(null, Args2);
            }
            finally
            {
                Array.Clear(Args2, 0, Args2.Length);
            }
        }

        static readonly object[] Args3 = new object[4];
        public static void EventData3_Fire_Override<T, U, V>(EventData<T, U, V> instance, T data0, U data1, V data2)
        {
            try
            {
                Args3[0] = instance;
                Args3[1] = data0;
                Args3[2] = data1;
                Args3[3] = data2;

                var method = GetConcreteFireMethod(instance);
                method.Invoke(null, Args3);
            }
            finally
            {
                Array.Clear(Args3, 0, Args3.Length);
            }
        }

        static readonly object[] Args4 = new object[5];
        public static void EventData4_Fire_Override<T, U, V, W>(EventData<T, U, V, W> instance, T data0, U data1, V data2, W data3)
        {
            try
            {
                Args4[0] = instance;
                Args4[1] = data0;
                Args4[2] = data1;
                Args4[3] = data2;
                Args4[4] = data3;

                var method = GetConcreteFireMethod(instance);
                method.Invoke(null, Args4);
            }
            finally
            {
                Array.Clear(Args4, 0, Args4.Length);
            }
        }

        #region EventData Overloads
        static readonly Type[] ValueTypeEventData1 =
        {
            typeof(EventData<object>),
            typeof(EventData<bool>),
            typeof(EventData<int>),
            typeof(EventData<uint>),
            typeof(EventData<float>),
            typeof(EventData<AltimeterDisplayState>),
            typeof(EventData<CameraManager.CameraMode>),
            typeof(EventData<ConstructionMode>),
            typeof(EventData<Contracts.Contract.State>),
            typeof(EventData<Contracts.Contract.Viewed>),
            typeof(EventData<DeltaVSituationOptions>),
            typeof(EventData<EditorScreen>),
            typeof(EventData<FlightCamera.Modes>),
            typeof(EventData<FlightGlobals.SpeedDisplayModes>),
            typeof(EventData<FlightUIMode>),
            typeof(EventData<GameScenes>),
            typeof(EventData<MapViewFiltering.VesselTypeFilter>),
            typeof(EventData<MenuNavInput>),
            typeof(EventData<Space>),
            typeof(EventData<SymmetryMethod>),
            typeof(EventData<Vector3d>),
            typeof(EventData<VesselSpawnInfo>),
            typeof(EventData<FromToAction<CelestialBody, CelestialBody>>),
            typeof(EventData<FromToAction<ControlTypes, ControlTypes>>),
            typeof(EventData<FromToAction<GameScenes, GameScenes>>),
            typeof(EventData<FromToAction<MENode, MENode>>),
            typeof(EventData<FromToAction<ModuleDockingNode, ModuleDockingNode>>),
            typeof(EventData<FromToAction<Part, Part>>),
            typeof(EventData<FromToAction<ProgressNode, ConfigNode>>),
            typeof(EventData<FromToAction<ProtoCrewMember, ConfigNode>>),
            typeof(EventData<FromToAction<ProtoPartModuleSnapshot, ConfigNode>>),
            typeof(EventData<FromToAction<ProtoPartSnapshot, ConfigNode>>),
            typeof(EventData<FromToAction<ProtoVessel, ConfigNode>>),
            typeof(EventData<FromToAction<Waypoint, ConfigNode>>),
            typeof(EventData<HostedFromToAction<bool, Part>>),
            typeof(EventData<HostedFromToAction<IDiscoverable, DiscoveryLevels>>),
            typeof(EventData<HostedFromToAction<Part, List<Part>>>),
            typeof(EventData<HostedFromToAction<PartResource, bool>>),
            typeof(EventData<HostedFromToAction<PartResource, PartResource.FlowMode>>),
            typeof(EventData<HostedFromToAction<ProtoCrewMember, Part>>),
            typeof(EventData<HostedFromToAction<ShipConstruct, string>>),
            typeof(EventData<HostedFromToAction<Vessel, CelestialBody>>),
            typeof(EventData<HostedFromToAction<Vessel, string>>),
            typeof(EventData<HostedFromToAction<Vessel, Vessel.Situations>>),
            typeof(EventData<HostTargetAction<CelestialBody, bool>>),
            typeof(EventData<HostTargetAction<Part, Part>>),
            typeof(EventData<HostTargetAction<RDTech, RDTech.OperationResult>>),
        };

        static readonly Type[] ValueTypeEventData2 =
        {
            typeof(EventData<object, object>),
            typeof(EventData<ConstructionEventType, Part>),
            typeof(EventData<ContractParameter, ParameterState>),
            typeof(EventData<DifficultyOptionsMenu, bool>),
            typeof(EventData<double, TransactionReasons>),
            typeof(EventData<float, float>),
            typeof(EventData<float, TransactionReasons>),
            typeof(EventData<int, int>),
            typeof(EventData<KerbalEVA, bool>),
            typeof(EventData<ModuleAnimationGroup, bool>),
            typeof(EventData<ModuleInventoryPart, int>),
            typeof(EventData<ModuleRoboticController, ModuleRoboticController.SequenceDirectionOptions>),
            typeof(EventData<ModuleRoboticController, ModuleRoboticController.SequenceLoopOptions>),
            typeof(EventData<Part, bool>),
            typeof(EventData<PartJoint, float>),
            typeof(EventData<ProtoCrewMember, int>),
            typeof(EventData<ProtoVessel, bool>),
            typeof(EventData<uint, uint>),
            typeof(EventData<UpgradeableFacility, int>),
            typeof(EventData<UpgradeableObject, int>),
            typeof(EventData<Vector3d, Vector3d>),
            typeof(EventData<Vessel, bool>),
        };

        static readonly Type[] ValueTypeEventData3 =
        {
            typeof(EventData<object, object, object>),
            typeof(EventData<BaseConverter, Part, double>),
            typeof(EventData<KerbalEVA, bool, bool>),
            typeof(EventData<ModuleGroundExpControl, bool, List<ModuleGroundSciencePart>>),
            typeof(EventData<PartModule, string, double>),
            typeof(EventData<ProtoCrewMember, bool, bool>),
            typeof(EventData<ProtoCrewMember, ProtoCrewMember.KerbalType, ProtoCrewMember.KerbalType>),
            typeof(EventData<ProtoCrewMember, ProtoCrewMember.RosterStatus, ProtoCrewMember.RosterStatus>),
            typeof(EventData<ProtoVessel, MissionRecoveryDialog, float>),
            typeof(EventData<ScienceData, Vessel, bool>),
            typeof(EventData<uint, uint, uint>),
            typeof(EventData<Vessel, float, Vector3>),
            typeof(EventData<Vessel, string, ReturnFrom>),
        };

        static readonly Type[] ValueTypeEventData4 =
        {
            typeof(EventData<object, object, object, object>),
            typeof(EventData<DeployedScienceExperiment, DeployedSciencePart, DeployedScienceCluster, float>),
            typeof(EventData<float, ScienceSubject, ProtoVessel, bool>),
            typeof(EventData<MENode, Vessel, Part, ProtoPartSnapshot>),
            typeof(EventData<object, string, SettingsInputBinding.BindingType, SettingsInputBinding.BindingVariant>),
        };
        #endregion
    }
}
