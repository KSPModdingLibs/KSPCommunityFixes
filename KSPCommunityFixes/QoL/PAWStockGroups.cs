using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.Localization;

namespace KSPCommunityFixes
{
    class PAWStockGroups : BasePatch
    {
        private static string partGroupTitle;
        private static string commsGroupTitle;
        private static string commandGroupTitle;
        private static string attitudeControlGroupTitle;

        protected override Version VersionMin => new Version(1, 10, 1);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), "Start"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDataTransmitter), "OnStart"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleCommand), "OnStart"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleReactionWheel), "OnStart"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleRCS), "OnStart"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleGimbal), "OnStart"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleControlSurface), "OnStart"),
                this));

            partGroupTitle = Localizer.Format("#autoLOC_6100048"); // Part
            commsGroupTitle = Localizer.Format("#autoLOC_453582"); // Communication
            commandGroupTitle = Localizer.Format("#autoLoc_6003031"); // Command
            attitudeControlGroupTitle = Localizer.Format("#autoLOC_6001695"); // Control
        }

        static void Part_Start_Postfix(Part __instance)
        {
            BasePAWGroup pawGroup = new BasePAWGroup("CF_Part", partGroupTitle, true);

            BaseField sameVesselCollision = __instance.Fields["sameVesselCollision"]; // 1.11.1
            if (sameVesselCollision != null)
                sameVesselCollision.group = pawGroup;

            BaseEvent setVesselNaming = __instance.Events["SetVesselNaming"]; // 1.11.0
            if (setVesselNaming != null)
                setVesselNaming.group = pawGroup;

            __instance.Events[nameof(Part.AimCamera)].group = pawGroup;
            __instance.Events[nameof(Part.ResetCamera)].group = pawGroup;
            __instance.Events[nameof(Part.ToggleAutoStrut)].group = pawGroup;
            __instance.Events[nameof(Part.ToggleRigidAttachment)].group = pawGroup;
            __instance.Events[nameof(Part.ShowUpgradeStats)].group = pawGroup;
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
