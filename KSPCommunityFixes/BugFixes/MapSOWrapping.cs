using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class MapSOCorrectWrapping : BasePatch
    {
        protected override Version VersionMin => new Version(1, 10, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(MapSO), nameof(MapSO.ConstructBilinearCoords), new Type[] { typeof(float), typeof(float) }),
                this,
                "MapSO_ConstructBilinearCoords_Float"));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(MapSO), nameof(MapSO.ConstructBilinearCoords), new Type[] { typeof(double), typeof(double) }),
                this,
                "MapSO_ConstructBilinearCoords_Double"));
        }

        static bool MapSO_ConstructBilinearCoords_Float(MapSO __instance, float x, float y)
        {
            // X wraps around as it is longitude.
            x = Mathf.Abs(x - Mathf.Floor(x));
            __instance.centerX = x * __instance._width;
            __instance.minX = Mathf.FloorToInt(__instance.centerX);
            __instance.maxX = Mathf.CeilToInt(__instance.centerX);
            __instance.midX = __instance.centerX - __instance.minX;
            if (__instance.maxX == __instance._width)
                __instance.maxX = 0;

            // Y clamps as it is latitude and the poles don't wrap to each other.
            y = Mathf.Clamp(y, 0, 0.99999f);
            __instance.centerY = y * __instance._height;
            __instance.minY = Mathf.FloorToInt(__instance.centerY);
            __instance.maxY = Mathf.CeilToInt(__instance.centerY);
            __instance.midY = __instance.centerY - __instance.minY;
            if (__instance.maxY >= __instance._height)
                __instance.maxY = __instance._height - 1;

            return false;
        }

        static bool MapSO_ConstructBilinearCoords_Double(MapSO __instance, double x, double y)
        {
            // X wraps around as it is longitude.
            x = Math.Abs(x - Math.Floor(x));
            __instance.centerXD = x * __instance._width;
            __instance.minX = (int)Math.Floor(__instance.centerXD);
            __instance.maxX = (int)Math.Ceiling(__instance.centerXD);
            __instance.midX = (float)__instance.centerXD - __instance.minX;
            if (__instance.maxX == __instance._width)
                __instance.maxX = 0;

            // Y clamps as it is latitude and the poles don't wrap to each other.
            y = Math.Min(Math.Max(y, 0), 0.99999);
            __instance.centerYD = y * __instance._height;
            __instance.minY = (int)Math.Floor(__instance.centerYD);
            __instance.maxY = (int)Math.Ceiling(__instance.centerYD);
            __instance.midY = (float)__instance.centerYD - __instance.minY;
            if (__instance.maxY >= __instance._height)
                __instance.maxY = __instance._height - 1;

            return false;
        }
    }
}
