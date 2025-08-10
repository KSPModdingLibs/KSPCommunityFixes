//#define DEBUG_PHYSICS_DT

using System;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    public class RestoreMaxPhysicsDT : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(TimeWarp), nameof(TimeWarp.updateRate));
        }

        // when you use physics warp, it increases Time.fixedDeltaTime
        // Unity will internally increase maximumDeltaTime to be at least as high as fixedDeltaTime
        // But nothing in the KSP code will ever return maximumDeltaTime to the value from the settings when time warp is over
        static void TimeWarp_updateRate_Postfix()
        {
            float fixedDT = Time.fixedDeltaTime;
            if (Time.maximumDeltaTime > fixedDT)
            {
                if (fixedDT > GameSettings.PHYSICS_FRAME_DT_LIMIT)
                    Time.maximumDeltaTime = fixedDT;
                else
                    Time.maximumDeltaTime = GameSettings.PHYSICS_FRAME_DT_LIMIT;
            }
        }
    }

#if DEBUG_PHYSICS_DT
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PhysicsDTDisplay : MonoBehaviour
    {
        void OnGUI()
        {
            GUI.Label(new Rect(0, 100, 200, 20), $"DT    : {Time.fixedDeltaTime}");
            GUI.Label(new Rect(0, 120, 200, 20), $"MaxDT : {Time.maximumDeltaTime}");
        }
    }
#endif
}
