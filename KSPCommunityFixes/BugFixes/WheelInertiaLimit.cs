using System;
using System.Collections.Generic;
using HarmonyLib;
using VehiclePhysics;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class WheelInertiaLimit : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(VehiclePhysics.Wheel), nameof(VehiclePhysics.Wheel.RecalculateConstants));
        }

        static void Wheel_RecalculateConstants_Postfix(VehiclePhysics.Wheel __instance)
        {
            __instance.I = __instance.mass * __instance.radius * __instance.radius * 0.5f;

            if (__instance.I < 0.00001f)
            {
                __instance.I = 0.00001f;
            }

            __instance.invI = 1f / __instance.I;
        }
    }
}
