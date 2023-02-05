using HarmonyLib;
using System;
using System.Collections.Generic;
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

            // see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/121
            // The patched ConstructBilinearCoords() methods will have the side effect of removing the Mohole because Squad
            // choose that "this is not a bug, it's a feature".
            // To prevent it, we acquire references to the Moho MapSO instances (biome map and height map) and fall back
            // to the original stock implementation if the method is called on those instances.
            // Note that all stock bodies will actually get a significantly different terrain at the poles due to this patch,
            // but due to how performance sensitive those method are, checking all stock MapSO instances (there are 40 of them)
            // isn't really an option.
            // Also note that we are doing reference acquisition from a PSystemSetup.Awake() patch because that's the only way I
            // found to ensure we always run before Koperncius. Failing to do so will result in Kopernius itself calling the patched
            // methods without the bail-out on Moho active, resulting in incorrect placement of the anomaly marker, and potentially
            // other weird issues.
            // All this should really have been handled by Kopernicus itself...
            bool ignoreMoho = false;
            if (KSPCommunityFixes.SettingsNode.TryGetValue("MapSOCorrectWrappingIgnoreMoho", ref ignoreMoho) && ignoreMoho)
            {
                patches.Add(new PatchInfo(
                    PatchMethodType.Postfix,
                    AccessTools.Method(typeof(PSystemSetup), nameof(PSystemSetup.Awake)),
                    this));
            }
        }

        private static MapSO moho_biomes;
        private static MapSO moho_height;

        static void PSystemSetup_Awake_Postfix(PSystemSetup __instance)
        {
            if (__instance.IsDestroyed())
                return;

            MapSO[] so = Resources.FindObjectsOfTypeAll<MapSO>();

            foreach (MapSO mapSo in so)
            {
                if (mapSo.MapName == "moho_biomes"
                    && mapSo.Size == 6291456
                    && mapSo._data[0] == 216
                    && mapSo._data[1] == 178
                    && mapSo._data[2] == 144)
                {
                    moho_biomes = mapSo;
                }
                else if (mapSo.MapName == "moho_height"
                         && mapSo.Size == 2097152
                         && mapSo._data[1509101] == 146
                         && mapSo._data[1709108] == 162
                         && mapSo._data[1909008] == 216)
                {
                    moho_height = mapSo;
                }
            }
        }

        static bool MapSO_ConstructBilinearCoords_Float(MapSO __instance, float x, float y)
        {
            if (ReferenceEquals(__instance, moho_biomes) || ReferenceEquals(__instance, moho_height))
                return true;

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
            if (ReferenceEquals(__instance, moho_biomes) || ReferenceEquals(__instance, moho_height))
                return true;

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
