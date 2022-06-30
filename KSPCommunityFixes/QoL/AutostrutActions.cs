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
                AccessTools.Method(typeof(Part), nameof(Part.Start)),
                this));

            kspActionAutostrutOff = new KSPAction(Localizer.Format("#autoLOC_6001318"), KSPActionGroup.None, true); // Autostrut: Disabled
            kspActionAutostrutRoot = new KSPAction(Localizer.Format("#autoLOC_6001320"), KSPActionGroup.None, true); // Autostrut: Root Part
            kspActionAutostrutHeaviest = new KSPAction(Localizer.Format("#autoLOC_6001319"), KSPActionGroup.None, true); // Autostrut: Heaviest Part
            kspActionAutostrutGrandparent = new KSPAction(Localizer.Format("#autoLOC_6001321"), KSPActionGroup.None, true); // Autostrut: Grandparent Part
        }

        private static readonly Stack<BaseAction> actions = new Stack<BaseAction>(4);

        static void Part_Start_Postfix(Part __instance)
        {
            if (__instance.physicalSignificance != PhysicalSignificance.FULL)
                return;

            actions.Push(__instance.actions.Add("AutostrutOff", param => SetAutostrutMode(__instance, AutoStrutMode.Off), kspActionAutostrutOff));
            actions.Push(__instance.actions.Add("AutostrutRoot", param => SetAutostrutMode(__instance, AutoStrutMode.Root), kspActionAutostrutRoot));
            actions.Push(__instance.actions.Add("AutostrutHeaviest", param => SetAutostrutMode(__instance, AutoStrutMode.Heaviest), kspActionAutostrutHeaviest));
            actions.Push(__instance.actions.Add("AutostrutGrandparent", param => SetAutostrutMode(__instance, AutoStrutMode.Grandparent), kspActionAutostrutGrandparent));

            bool allowAutostruts = __instance.AllowAutoStruts();

            while (actions.TryPop(out BaseAction action))
            {
                action.active = allowAutostruts;
                action.activeEditor = allowAutostruts;
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