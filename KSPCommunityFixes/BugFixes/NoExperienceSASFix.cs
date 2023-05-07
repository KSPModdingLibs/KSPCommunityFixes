using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    public class NoExperienceSASFix : BasePatch
    {
        protected override Version VersionMin => new Version(1, 11, 1);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(APSkillExtensions), nameof(APSkillExtensions.AvailableAtLevel), new Type[] { typeof(VesselAutopilot.AutopilotMode), typeof(Vessel)}),
                this));
        }

        private static bool APSkillExtensions_AvailableAtLevel_Prefix(VesselAutopilot.AutopilotMode mode, Vessel vessel, ref bool __result)
        {
            // Fixes the SAS inconsistency when the Kerbal experience setting is off (default in science and sandbox mode)
            // More info : https://wiki.kerbalspaceprogram.com/wiki/Specialization#No_experience_specialization_override
            // Therefore
            // - Only pilots can SAS
            // - Probe's SAS level is compared with the pilot's to see which is the best
            // - Manages the "Probes can fully SAS" in the difficulty settings
            // Doesn't fix the parachute repack and part repair which can be roleplayed easily

            //Debug.Log("AutopilotKerbal = " + vessel.VesselValues.AutopilotKerbalSkill.value);
            //Debug.Log("AutopilotSASSKill = " + vessel.VesselValues.AutopilotSASSkill.value);

            int pilotSkill = vessel.VesselValues.AutopilotKerbalSkill.value;
            int probeSkill = vessel.VesselValues.AutopilotSASSkill.value;
            
            // that's the name of the setting, but we'll use it for every non-mission mode
            bool fullSASInSandbox = HighLogic.CurrentGame.Parameters.CustomParams<GameParameters.AdvancedParams>().EnableFullSASInSandbox;
            // mission mode, don't know where to set it (except directly in the savefile) and ESA stock missions don't have this setting
            bool fullSASInMissions = HighLogic.CurrentGame.Parameters.CustomParams<GameParameters.AdvancedParams>().EnableFullSASInMissions;

            // checks if a probe is available on the ship
            // we can't rely on the ModuleSAS because the Stayputnik doesn't have that
            bool hasProbe = false;
            for(int i = 0; i < vessel.Parts.Count; i++)
            {
                var myPart = vessel.Parts[i];
                for(int j = 0; j < myPart.Modules.Count; j++)
                {
                    var myModule = myPart.Modules[j];
                    // a command module which doesn't require a kerbal, which is a probe
                    if (myModule.GetType() == typeof(ModuleCommand) && ((ModuleCommand)myModule).minimumCrew == 0)
                    {
                        hasProbe = true;
                        break;
                    }
                }
                if (hasProbe)
                {
                    break;
                }
            }
            
            // probes can fully SAS in sandbox setting (applied to all game modes except mission)
            if (fullSASInSandbox && HighLogic.CurrentGame.Mode != Game.Modes.MISSION && hasProbe)
            {
                probeSkill = 3;
            }
            // probes can fully SAS in missions
            if (fullSASInMissions && HighLogic.CurrentGame.Mode == Game.Modes.MISSION && hasProbe)
            {
                probeSkill = 3;
            }

            __result = Math.Max(pilotSkill, probeSkill) >= mode.GetRequiredSkill();

            return false;
        }
    }
}
