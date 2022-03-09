using System;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static ModuleReactionWheel;

namespace KSPCommunityFixes
{
    public class ReactionWheelsPotentialTorque : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleReactionWheel), nameof(ModuleReactionWheel.GetPotentialTorque)),
                this));
        }

        static bool ModuleReactionWheel_GetPotentialTorque_Prefix(ModuleReactionWheel __instance, out Vector3 pos, out Vector3 neg)
        {
            if (__instance.moduleIsEnabled && __instance.wheelState == WheelState.Active && __instance.actuatorModeCycle != 2)
            {
                float authorityLimiter = __instance.authorityLimiter * 0.01f;
                neg.x = (pos.x = __instance.PitchTorque * authorityLimiter);
                neg.y = (pos.y = __instance.RollTorque * authorityLimiter);
                neg.z = (pos.z = __instance.YawTorque * authorityLimiter);
                return false;
            }

            pos = (neg = Vector3.zero);
            return false;
        }
    }
}
