// ModuleActiveRadiator.FixedUpdate is quite slow. It iterates over every
// single part on the current vessel, and each instance of it does a bunch
// of repeated work that could be shared.
//
// This patch is a full rewrite:
// * All shared state is stored in a per-vessel KSPCFRadiatorHeatIntegrator
//   component that does the computation for all active radiators at once,
//   just before FlightIntegrator runs and integrates the heat.
// * Individual ModuleActiveRadiator instances do their normal "am I active"
//   checks and then register themselves with the heat integrator.
// * We also remove all calls to FindModuleImplementing outside of cache
//   updates.

using System;
using System.Collections.Generic;
using Expansions.Missions.Adjusters;
using Radiators;
using UnityEngine;
using TimingStage = TimingManager.TimingStage;
using DeployState = ModuleDeployablePart.DeployState;
using Unity.Profiling;

namespace KSPCommunityFixes.Performance;

internal class KSPCFRadiatorHeatIntegrator : MonoBehaviour
{
    Vessel vessel;

    bool isCacheDirty = true;

    // radiators and their associated deployable radiator, if any
    readonly List<ModuleActiveRadiator> radiators = [];
    readonly List<ModuleDeployableRadiator> deployables = [];

    // ship parts, split into radiators and not-radiators the same way stock does
    public readonly List<Part> radiatorParts = [];
    public readonly List<Part> nonRadiatorParts = [];

    // radiators that are queued up to be processed in FixedUpdate, along with
    // the RadiatorData snapshot taken when they were queued (so we don't have
    // to recompute it during the batched cooling pass).
    readonly List<QueuedRadiator> active = [];

    // temporaries for use in FixedUpdate
    readonly List<RadiatorData> hotParts = [];
    readonly List<RadiatorData> coldParts = [];

    // cached value for the list of active radiators
    private int? activeRadiatorCount = null;

    static readonly ProfilerMarker FixedUpdateMarker = new(nameof(KSPCFRadiatorHeatIntegrator) + ".FixedUpdate");

    // Rebuild part caches if necessary
    public void CheckPartCaches()
    {
        if (!isCacheDirty)
            return;

        foreach (var part in vessel.parts)
        {
            var module = part.FindModuleImplementingFast<ModuleActiveRadiator>();
            if (!module.IsNullOrDestroyed())
            {
                radiators.Add(module);
                deployables.Add(module._depRad);
                radiatorParts.Add(part);
            }
            else
            {
                nonRadiatorParts.Add(part);
            }
        }

        isCacheDirty = false;
    }

    // Get the number of active radiators on the current vessel.
    //
    // This is meant to mirror stock, so active radiator parts are:
    // * those without a ModuleDeployableRadiator, or,
    // * those that are in the EXTENDED state.
    public int GetActiveCountCached()
    {
        if (activeRadiatorCount is int count)
            return count;

        CheckPartCaches();

        count = 0;
        foreach (var depRad in deployables)
        {
            if (depRad.IsNullOrDestroyed()
                || depRad.deployState == DeployState.EXTENDED)
            {
                count++;
            }
        }

        activeRadiatorCount = count;
        return count;
    }

    // Queue this radiator for later heat processing
    public void QueueRadiatorUpdate(ModuleActiveRadiator radiator, RadiatorData rad)
    {
        active.Add(new QueuedRadiator(radiator, rad));
    }

    void Awake()
    {
        vessel = gameObject.GetComponent<Vessel>();
    }

    void Start()
    {
        GameEvents.onVesselStandardModification.Add(OnVesselStandardModification);
        GameEvents.onVesselUnloaded.Add(OnVesselUnloaded);

        // By using TimingManager here we can ensure we run just before FlightIntegrator
        // and not need to worry about ordering too much.
        TimingManager.FixedUpdateAdd(TimingStage.FlightIntegrator, DoFixedUpdate);
    }

    void OnDestroy()
    {
        GameEvents.onVesselStandardModification.Remove(OnVesselStandardModification);
        GameEvents.onVesselUnloaded.Remove(OnVesselUnloaded);

        TimingManager.FixedUpdateRemove(TimingStage.FlightIntegrator, DoFixedUpdate);
    }

    void OnVesselStandardModification(Vessel v)
    {
        if (v != vessel)
            return;

        radiators.Clear();
        deployables.Clear();
        radiatorParts.Clear();
        nonRadiatorParts.Clear();
        isCacheDirty = true;
    }

    void OnVesselUnloaded(Vessel v)
    {
        if (v != vessel)
            return;

        Destroy(this);
    }

    void DoFixedUpdate()
    {
        activeRadiatorCount = null;
        if (active.Count == 0)
            return;

        using var scope = FixedUpdateMarker.Auto();
        using var guard = new ClearActiveOnDispose(active);

        int radCount = GetActiveCountCached();
        if (radCount == 0)
            return;

        hotParts.Clear();
        double externalTemp = vessel.externalTemperature;
        foreach (var part in nonRadiatorParts)
        {
            if (part.temperature > part.maxTemp * part.radiatorMax
                && part.temperature > externalTemp)
            {
                hotParts.Add(RadiatorUtilities.GetThermalData(part));
            }
        }
        if (hotParts.Count == 0)
            return;

        float fdt = TimeWarp.fixedDeltaTime;
        bool firstFrame = vessel.IsFirstFrame();
        bool debug = PhysicsGlobals.ThermalDataDisplay;

        foreach (var (radiator, radData) in active)
        {
            var radPart = radiator.part;
            bool parentOnly = radiator.parentCoolingOnly;
            double overcool = radiator.overcoolFactor;
            double maxXfer = radiator.maxEnergyTransfer;
            double xferScale = Math.Min(1.0, radiator.energyTransferScale);
            double overcoolThreshold = radPart.temperature * overcool;

            double radHeadroom = Math.Min(radData.EnergyCap - radData.Energy, maxXfer) / fdt;

            coldParts.Clear();
            foreach (var hotData in hotParts)
            {
                var hotPart = hotData.Part;
                if (hotPart.temperature <= overcoolThreshold)
                    continue;
                if (hotData.Energy - hotData.MaxEnergy <= 0.0)
                    continue;
                if (parentOnly && !radiator.IsSibling(hotPart))
                    continue;
                coldParts.Add(hotData);
            }

            int coldCount = coldParts.Count;
            if (debug)
            {
                radiator.D_RadCount = radCount.ToString();
                radiator.D_CoolParts = StringBuilderCache.Format("{0}/{1}", coldCount, hotParts.Count);
            }

            if (coldCount == 0)
                continue;

            double divisor = radCount + coldCount;

            foreach (var cold in coldParts)
            {
                double excess = (cold.Energy - cold.MaxEnergy) / fdt / divisor;
                double xferBase = Math.Min(radHeadroom, excess);
                double xfer = xferBase * xferScale;
                if (xfer > 0.0 && !firstFrame)
                {
                    cold.Part.AddThermalFlux(-xfer);
                    radPart.AddThermalFlux(xfer);
                }

                if (debug)
                {
                    radiator.D_XferBase = xferBase.ToString();
                    radiator.D_Excess = excess.ToString();
                    radiator.D_HeadRoom = radHeadroom.ToString();
                    radiator.D_XferFin = xfer.ToString();
                }
            }
        }
    }

    public readonly struct QueuedRadiator(ModuleActiveRadiator module, RadiatorData rad)
    {
        public readonly ModuleActiveRadiator Module = module;
        public readonly RadiatorData Rad = rad;

        public void Deconstruct(out ModuleActiveRadiator module, out RadiatorData rad)
        {
            module = Module;
            rad = Rad;
        }
    }

    private readonly struct ClearActiveOnDispose(List<QueuedRadiator> active) : IDisposable
    {
        private readonly List<QueuedRadiator> active = active;

        public void Dispose() => active.Clear();
    }
}

internal class ActiveRadiatorPerf : BasePatch
{
    protected override void ApplyPatches()
    {
        AddPatch(PatchType.Postfix, typeof(ModuleActiveRadiator), nameof(ModuleActiveRadiator.OnAwake));
        AddPatch(PatchType.Postfix, typeof(ModuleActiveRadiator), nameof(ModuleActiveRadiator.Start));
        AddPatch(PatchType.Override, typeof(ModuleActiveRadiator), nameof(ModuleActiveRadiator.FixedUpdate));
        AddPatch(PatchType.Override, typeof(ModuleActiveRadiator), nameof(ModuleActiveRadiator.CheckPartCaches));
        AddPatch(PatchType.Override, typeof(ModuleActiveRadiator), nameof(ModuleActiveRadiator.InternalCooling));
        AddPatch(PatchType.Override, typeof(ModuleActiveRadiator), nameof(ModuleActiveRadiator.CheckResources));
    }

    // We need somewhere to store the extra data and the only private class
    // field is the adjuster cache list.
    class ExtraModuleData : List<AdjusterActiveRadiatorBase>
    {
        public KSPCFRadiatorHeatIntegrator integrator;
    }

    static void ModuleActiveRadiator_OnAwake_Postfix(ModuleActiveRadiator __instance)
    {
        var cache = __instance.adjusterCache;
        __instance.adjusterCache = new ExtraModuleData();
        __instance.adjusterCache.AddRange(cache);
    }

    static void ModuleActiveRadiator_Start_Postfix(ModuleActiveRadiator __instance)
    {
        var data = (ExtraModuleData)__instance.adjusterCache;
        data.integrator = __instance.vessel.gameObject
            .AddOrGetComponent<KSPCFRadiatorHeatIntegrator>();
    }

    static void ModuleActiveRadiator_FixedUpdate_Override(ModuleActiveRadiator __instance)
    {
        if (!HighLogic.LoadedSceneIsFlight || __instance.vessel.IsNullOrDestroyed())
            return;

        __instance.CheckPartCaches();

        if (!__instance._depRad.IsNullOrDestroyed() && !__instance.IsAdjusterBlockingCooling())
            __instance.IsCooling = __instance._depRad.deployState == DeployState.EXTENDED;

        if (!__instance.IsCooling)
        {
            __instance.status = ModuleActiveRadiator.cacheAutoLOC_6001415;
            __instance.statusValue = -1.0;
            return;
        }

        if (!__instance.CheckResources())
            return;

        __instance.maxEnergyTransfer = __instance.ApplyMaxEnergyTransferAdjustments(__instance.originalMaxEnergyTranfer);

        var tempPct = Math.Round(100.0 * __instance.part.temperature / (__instance.part.maxTemp * __instance.part.radiatorHeadroom), 2);
        if (tempPct != __instance.statusValue)
        {
            __instance.status = StringBuilderCache.Format("{0:0.00}%", tempPct);
            __instance.statusValue = tempPct;
        }

        __instance.CheckDebugFields();

        if (__instance._depRad.IsNotNullOrDestroyed() && __instance._depRad.deployState != DeployState.EXTENDED)
            return;

        var data = (ExtraModuleData)__instance.adjusterCache;
        var thermalData = RadiatorUtilities.GetThermalData(__instance.part);
        var radiatorCount = data.integrator.GetActiveCountCached();
        __instance.InternalCooling(thermalData, radiatorCount);
    }

    static void ModuleActiveRadiator_CheckPartCaches_Override(ModuleActiveRadiator __instance)
    {
        if (!__instance.partCachesDirty)
            return;

        var data = (ExtraModuleData)__instance.adjusterCache;
        data.integrator.CheckPartCaches();

        __instance.activeRadiatorParts = data.integrator.radiatorParts;
        __instance.nonRadiatorParts = data.integrator.nonRadiatorParts;
        __instance.partCachesDirty = false;
    }

    static void ModuleActiveRadiator_InternalCooling_Override(
        ModuleActiveRadiator __instance,
        RadiatorData rad,
        int radCount)
    {
        var data = (ExtraModuleData)__instance.adjusterCache;
        data.integrator.QueueRadiatorUpdate(__instance, rad);
    }

    static bool ModuleActiveRadiator_CheckResources_Override(ModuleActiveRadiator __instance)
    {
        var radiator = __instance._depRad;
        if (radiator != null && radiator.deployState != DeployState.EXTENDED)
        {
            __instance.status = ModuleActiveRadiator.cacheAutoLOC_232021;
            __instance.statusValue = -1.0;
            return false;
        }

        return __instance.resHandler.UpdateModuleResourceInputs(
            ref __instance.status,
            1.0, 
            0.9,
            returnOnFirstLack: true,
            stringOps: __instance.part.IsPAWOpen()
        );
    }
}
