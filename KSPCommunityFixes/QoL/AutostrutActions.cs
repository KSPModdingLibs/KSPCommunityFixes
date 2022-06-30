using HarmonyLib;
using KSP.Localization;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Part;

namespace KSPCommunityFixes.QoL
{
    class AutostrutActions : BasePatch
    {
        private static KSPAction kspActionAutostrutOff;
        private static KSPAction kspActionAutostrutRoot;
        private static KSPAction kspActionAutostrutHeaviest;
        private static KSPAction kspActionAutostrutGrandparent;

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.Awake)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(Part), nameof(Part.ModulesOnStartFinished)),
                this));

            kspActionAutostrutOff = new KSPAction(Localizer.Format("#autoLOC_6001318"), KSPActionGroup.None, true); // Autostrut: Disabled
            kspActionAutostrutRoot = new KSPAction(Localizer.Format("#autoLOC_6001320"), KSPActionGroup.None, true); // Autostrut: Root Part
            kspActionAutostrutHeaviest = new KSPAction(Localizer.Format("#autoLOC_6001319"), KSPActionGroup.None, true); // Autostrut: Heaviest Part
            kspActionAutostrutGrandparent = new KSPAction(Localizer.Format("#autoLOC_6001321"), KSPActionGroup.None, true); // Autostrut: Grandparent Part
        }

        // The actions need to be added right away, otherwise stock will nullref when copying action group settings on part duplication.
        static void Part_Awake_Postfix(Part __instance)
        {
            __instance.actions.Add("AutostrutOff", param => SetAutostrutMode(__instance, AutoStrutMode.Off), kspActionAutostrutOff);
            __instance.actions.Add("AutostrutRoot", param => SetAutostrutMode(__instance, AutoStrutMode.Root), kspActionAutostrutRoot);
            __instance.actions.Add("AutostrutHeaviest", param => SetAutostrutMode(__instance, AutoStrutMode.Heaviest), kspActionAutostrutHeaviest);
            __instance.actions.Add("AutostrutGrandparent", param => SetAutostrutMode(__instance, AutoStrutMode.Grandparent), kspActionAutostrutGrandparent);
        }

        // We want to run during Part.Start(), after physicalSignificance has been set so the AllowAutoStruts() check works correctly
        static void Part_ModulesOnStartFinished_Prefix(Part __instance)
        {
            bool allowAutostruts = __instance.AllowAutoStruts();

            foreach (BaseAction action in __instance.actions)
            {
                if (action.name == "AutostrutOff" || action.name == "AutostrutRoot" || action.name == "AutostrutHeaviest" || action.name == "AutostrutGrandparent")
                {
                    action.active = allowAutostruts;
                    action.activeEditor = allowAutostruts;
                }
            }
        }

        private static void SetAutostrutMode(Part part, AutoStrutMode mode)
        {
            if (part.autoStrutMode == mode)
                return;

            if (part.autoStrutMode == AutoStrutMode.ForceRoot || part.autoStrutMode == AutoStrutMode.ForceHeaviest || part.autoStrutMode == AutoStrutMode.ForceGrandparent)
                return;

            if (!part.AllowAutoStruts())
                return;

            part.autoStrutMode = mode;

            if (GameSettings.AUTOSTRUT_SYMMETRY)
            {
                int count = part.symmetryCounterparts.Count;
                while (count-- > 0)
                {
                    Part symPart = part.symmetryCounterparts[count];
                    symPart.autoStrutMode = part.autoStrutMode;
                    symPart.UpdateAutoStrut();
                    if (HighLogic.LoadedSceneIsEditor)
                    {
                        symPart.autoStrutQuickViz = true;
                        symPart.autoStrutQuickVizStart = Time.time;
                    }
                }
            }
            part.UpdateAutoStrut();
            if (HighLogic.LoadedSceneIsEditor)
            {
                part.autoStrutQuickViz = true;
                part.autoStrutQuickVizStart = Time.time;
            }
        }
    }
}