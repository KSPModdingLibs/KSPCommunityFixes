using System.Collections.Generic;
using HarmonyLib;
using KSP.Localization;

namespace KSPCommunityFixes
{
    class PAWStockGroups : BasePatch
    {
        private static bool partGroup;
        private static bool commsGroup;
        private static bool commandGroup;
        private static bool attitudeControlGroup;

        private static string partGroupTitle;
        private static string commsGroupTitle;
        private static string commandGroupTitle;
        private static string attitudeControlGroupTitle;


        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), "Start"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDataTransmitter), "OnStart"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleCommand), "OnStart"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleReactionWheel), "OnStart"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleRCS), "OnStart"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleGimbal), "OnStart"),
                GetType()));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleControlSurface), "OnStart"),
                GetType()));

            partGroupTitle = Localizer.Format("#autoLOC_6100048"); // Part
            commsGroupTitle = Localizer.Format("#autoLOC_453582"); // Communication
            commandGroupTitle = Localizer.Format("#autoLoc_6003031"); // Command
            attitudeControlGroupTitle = Localizer.Format("#autoLOC_6001695"); // Control
        }

        static void Part_Start_Postfix(Part __instance)
        {
            BasePAWGroup pawGroup = new BasePAWGroup("CF_Part", partGroupTitle, true);

            __instance.Fields[nameof(Part.sameVesselCollision)].group = pawGroup;
            __instance.Events[nameof(Part.AimCamera)].group = pawGroup;
            __instance.Events[nameof(Part.ResetCamera)].group = pawGroup;
            __instance.Events[nameof(Part.ToggleAutoStrut)].group = pawGroup;
            __instance.Events[nameof(Part.ToggleRigidAttachment)].group = pawGroup;
            __instance.Events[nameof(Part.ShowUpgradeStats)].group = pawGroup;

            if (__instance.Events.Contains(nameof(Part.SetVesselNaming)))
            {
                __instance.Events[nameof(Part.SetVesselNaming)].group = pawGroup;
            }
        }

        static void ModuleDataTransmitter_OnStart_Postfix(ModuleDataTransmitter __instance)
        {
            BasePAWGroup pawGroup = new BasePAWGroup("CF_Comms", commsGroupTitle, __instance.part.Modules.Count > 3);

            foreach (BaseField baseField in __instance.Fields)
            {
                baseField.group = pawGroup;
            }

            foreach (BaseEvent baseEvent in __instance.Events)
            {
                baseEvent.group = pawGroup;
            }
        }

        static void ModuleCommand_OnStart_Postfix(ModuleCommand __instance)
        {
            BasePAWGroup pawGroup = new BasePAWGroup("CF_Command", commandGroupTitle, __instance.part.Modules.Count > 3);

            foreach (BaseField baseField in __instance.Fields)
            {
                baseField.group = pawGroup;
            }

            foreach (BaseEvent baseEvent in __instance.Events)
            {
                baseEvent.group = pawGroup;
            }
        }

        static void ModuleReactionWheel_OnStart_Postfix(ModuleReactionWheel __instance)
        {
            BasePAWGroup pawGroup = new BasePAWGroup("CF_Attitude", attitudeControlGroupTitle, __instance.part.Modules.Count > 3);

            foreach (BaseField baseField in __instance.Fields)
            {
                baseField.group = pawGroup;
            }

            foreach (BaseEvent baseEvent in __instance.Events)
            {
                baseEvent.group = pawGroup;
            }
        }

        static void ModuleRCS_OnStart_Postfix(ModuleRCS __instance)
        {
            BasePAWGroup pawGroup = new BasePAWGroup("CF_Attitude", attitudeControlGroupTitle, __instance.part.Modules.Count > 3);

            foreach (BaseField baseField in __instance.Fields)
            {
                baseField.group = pawGroup;
            }

            foreach (BaseEvent baseEvent in __instance.Events)
            {
                baseEvent.group = pawGroup;
            }
        }

        static void ModuleGimbal_OnStart_Postfix(ModuleGimbal __instance)
        {
            BasePAWGroup pawGroup = new BasePAWGroup("CF_Attitude", attitudeControlGroupTitle, __instance.part.Modules.Count > 3);

            foreach (BaseField baseField in __instance.Fields)
            {
                baseField.group = pawGroup;
            }

            foreach (BaseEvent baseEvent in __instance.Events)
            {
                baseEvent.group = pawGroup;
            }
        }

        static void ModuleControlSurface_OnStart_Postfix(ModuleControlSurface __instance)
        {
            BasePAWGroup pawGroup = new BasePAWGroup("CF_Attitude", attitudeControlGroupTitle, __instance.part.Modules.Count > 3);

            foreach (BaseField baseField in __instance.Fields)
            {
                baseField.group = pawGroup;
            }

            foreach (BaseEvent baseEvent in __instance.Events)
            {
                baseEvent.group = pawGroup;
            }
        }
    }
}
