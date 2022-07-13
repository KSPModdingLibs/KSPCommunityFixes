using HarmonyLib;
using KSP.Localization;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KSPCommunityFixes.QoL
{
    class ResourceLockActions : BasePatch
    {
        private const string ACTION_NAME_ENABLE_FLOW = "ResourcesEnableFlow";
        private const string ACTION_NAME_DISABLE_FLOW = "ResourcesDisableFlow";

        private static KSPAction kspActionResourcesEnableFlow;
        private static KSPAction kspActionResourcesDisableFlow;

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.Awake)),
                this));

            kspActionResourcesEnableFlow = new KSPAction(Localizer.Format("#autoLOC_444646") + ": " + Localizer.Format("#autoLOC_6001423"), KSPActionGroup.None, true); // Resources: Flow
            kspActionResourcesDisableFlow = new KSPAction(Localizer.Format("#autoLOC_444646") + ": " + Localizer.Format("#autoLOC_215362"), KSPActionGroup.None, true); // Resources: Locked

            GameEvents.onPartResourceListChange.Add(OnPartResourceListChange);
        }

        // The actions need to be added right away, otherwise stock will nullref when copying action group settings on part duplication.
        static void Part_Awake_Postfix(Part __instance)
        {
            BaseAction enableAction = __instance.actions.Add(ACTION_NAME_ENABLE_FLOW, param => SetResourcesFlowState(__instance, true), kspActionResourcesEnableFlow);
            BaseAction disableAction = __instance.actions.Add(ACTION_NAME_DISABLE_FLOW, param => SetResourcesFlowState(__instance, false), kspActionResourcesDisableFlow);

            bool hasResources = HasFlowToggleableResources(__instance);
            enableAction.active = hasResources;
            enableAction.activeEditor = hasResources;
            disableAction.active = hasResources;
            disableAction.activeEditor = hasResources;
        }

        private void OnPartResourceListChange(Part part)
        {
            bool hasResources = HasFlowToggleableResources(part);

            for (int i = part.actions.Count; i-- > 0;)
            {
                BaseAction partAction = part.actions[i];
                if (partAction.name == ACTION_NAME_ENABLE_FLOW || partAction.name == ACTION_NAME_DISABLE_FLOW)
                {
                    partAction.active = hasResources;
                    partAction.activeEditor = hasResources;
                }
            }
        }

        static void SetResourcesFlowState(Part part, bool flowState)
        {
            bool toggled = false;
            for (int i = part.Resources.Count; i-- > 0;)
            {
                PartResource resource = part.Resources[i];
                if (CanToggleFlow(resource) && resource._flowState != flowState)
                {
                    toggled = true;
                    resource.flowState = flowState;
                }
            }

            if (toggled && part.PartActionWindow.IsNotNullOrDestroyed() && part.PartActionWindow.isActiveAndEnabled)
            {
                part.PartActionWindow.displayDirty = true;
            }
        }

        private static bool HasFlowToggleableResources(Part part)
        {
            for (int i = part.Resources.Count; i-- > 0;)
                if (CanToggleFlow(part.Resources[i]))
                    return true;

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanToggleFlow(PartResource resource)
        {
            return resource.isVisible && !resource.hideFlow && resource.info.resourceFlowMode != ResourceFlowMode.NO_FLOW;
        }
    }
}
