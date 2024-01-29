using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KSPCommunityFixes.BugFixes
{
    class RespawnDeadKerbals : BasePatch
    {

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Constructor(typeof(Game), new Type[] { typeof(ConfigNode) }),
                this,
                nameof(Game_Constructor_Postfix)));
        }

        static void Game_Constructor_Postfix(Game __instance)
        {
            if (__instance.Parameters.Difficulty.MissingCrewsRespawn)
            {
                double respawnUTC = __instance.UniversalTime + __instance.Parameters.Difficulty.RespawnTimer;

                foreach (var protoCrewMember in __instance.CrewRoster.kerbals.ValuesList)
                {
                    if (protoCrewMember.type == ProtoCrewMember.KerbalType.Crew && protoCrewMember.rosterStatus == ProtoCrewMember.RosterStatus.Dead)
                    {
                        protoCrewMember.SetTimeForRespawn(respawnUTC);
                    }
                }
            }
        }
    }
}
