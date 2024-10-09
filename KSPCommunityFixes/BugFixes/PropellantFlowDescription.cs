using System;
using HarmonyLib;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    class PropellantFlowDescription : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Propellant), nameof(Propellant.GetFlowModeDescription))));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Propellant), nameof(Propellant.GetFlowModeDescription))));
        }

        // doing this this way rather than via __state
        // so we don't have to pass around structs or the like
        private static PartResourceDefinition _def;
        private static ResourceFlowMode _mode;

        static void Propellant_GetFlowModeDescription_Prefix(Propellant __instance)
        {
            if (__instance.flowMode == ResourceFlowMode.NULL)
                return;
            var resDef = PartResourceLibrary.Instance.GetDefinition(__instance.id);
            if (resDef == null)
                return;

            _def = resDef;
            _mode = _def.resourceFlowMode;
            _def._resourceFlowMode = __instance.flowMode;
        }

        static void Propellant_GetFlowModeDescription_Postfix(Propellant __instance)
        {
            if (_def != null)
                _def._resourceFlowMode = _mode;

            _def = null;
        }
    }
}
