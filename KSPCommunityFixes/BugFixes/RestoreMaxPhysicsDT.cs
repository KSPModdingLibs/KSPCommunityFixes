using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    public class RestoreMaxPhysicsDT : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(TimeWarp), "updateRate"),
                this));
        }

        static void TimeWarp_updateRate_Prefix()
        {
            // when you use physics warp, it increases Time.fixedDeltaTime
            // Unity will internally increase maximumDeltaTime to be at least as high as fixedDeltaTime
            // But nothing in the KSP code will ever return maximumDeltaTime to the value from the settings when time warp is over
            // Setting it just before TimeWarp sets fixedDeltaTime guarantees that all points of code will have the correct value
            Time.maximumDeltaTime = GameSettings.PHYSICS_FRAME_DT_LIMIT;
        }
    }
}
