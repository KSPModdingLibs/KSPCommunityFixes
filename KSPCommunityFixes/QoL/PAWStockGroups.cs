using System;
using System.Collections.Generic;
using System.Linq;
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
        private static string generatorGroupTitle;

        protected override Version VersionMin => new Version(1, 10, 1);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(Part), nameof(Part.Start));

            AddPatch(PatchType.Postfix, typeof(ModuleDataTransmitter), nameof(ModuleDataTransmitter.OnStart));

            AddPatch(PatchType.Postfix, typeof(ModuleCommand), nameof(ModuleCommand.OnStart));

            AddPatch(PatchType.Postfix, typeof(ModuleReactionWheel), nameof(ModuleReactionWheel.OnStart));

            AddPatch(PatchType.Postfix, typeof(ModuleRCS), nameof(ModuleRCS.OnStart));

            AddPatch(PatchType.Postfix, typeof(ModuleGimbal), nameof(ModuleGimbal.OnStart));

            AddPatch(PatchType.Postfix, typeof(ModuleControlSurface), nameof(ModuleControlSurface.OnStart));

            AddPatch(PatchType.Postfix, typeof(ModuleGenerator), nameof(ModuleGenerator.OnStart));

            partGroupTitle = Localizer.Format("#autoLOC_6100048"); // Part
            commsGroupTitle = Localizer.Format("#autoLOC_453582"); // Communication
            commandGroupTitle = Localizer.Format("#autoLoc_6003031"); // Command
            attitudeControlGroupTitle = Localizer.Format("#autoLOC_6001695"); // Control
            generatorGroupTitle = Localizer.Format("#autoLOC_235532"); // Generator
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

        static void ModuleGenerator_OnStart_Postfix(ModuleGenerator __instance)
        {
            // if only one generator, don't create a group
            if (__instance.part.FindModulesImplementingReadOnly<ModuleGenerator>().Count == 1)
                return;

            string name = $"CF_Generator_{__instance.GetInstanceIDFast()}";

            var inputs = __instance.resHandler.inputResources;
            var outputs = __instance.resHandler.outputResources;
            string abbrs = "";

            if (inputs.Count + outputs.Count <= 4)
            {
                abbrs = ": ";
                abbrs += string.Join(" ", inputs.Select(r => r.resourceDef.abbreviation));
                if (inputs.Count !=0 && outputs.Count != 0) 
                    abbrs += " -> ";
                abbrs += string.Join(" ", outputs.Select(r => r.resourceDef.abbreviation));
            }

            BasePAWGroup pawGroup = new BasePAWGroup(name, generatorGroupTitle + abbrs, false);

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
