using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
            // To prevent it, we acquire references to the Moho MapSO heightmap instance and fall back to the original stock
            // implementation if the method is called on those instances.
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

        private static MapSO moho_height;
        private static readonly double lessThanOneDouble = StaticHelpers.BitDecrement(1.0);
        private static readonly float lessThanOneFloat = StaticHelpers.BitDecrement(1f);

        static void PSystemSetup_Awake_Postfix(PSystemSetup __instance)
        {
            if (__instance.IsDestroyed())
                return;

            MapSO[] so = Resources.FindObjectsOfTypeAll<MapSO>();

            foreach (MapSO mapSo in so)
            {
                if (mapSo.MapName == "moho_height"
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
            // X wraps around as it is longitude.
            x = Mathf.Abs(x - Mathf.Floor(x));

            __instance.centerX = x * __instance._width;
            __instance.minX = (int)__instance.centerX;
            __instance.maxX = __instance.minX + 1;
            __instance.midX = __instance.centerX - __instance.minX;
            if (__instance.maxX == __instance._width)
                __instance.maxX = 0;

            // Y clamps as it is latitude and the poles don't wrap to each other.
            if (y >= 1f) 
                y = lessThanOneFloat;
            else if (y < 0f) 
                y = 0f;

            __instance.centerY = y * __instance._height;
            __instance.minY = (int)__instance.centerY;
            __instance.maxY = __instance.minY + 1;
            __instance.midY = __instance.centerY - __instance.minY;
            if (__instance.maxY == __instance._height)
            {
                if (ReferenceEquals(__instance, moho_height))
                    __instance.maxY = 0; // use incorrect wrapping for moho
                else
                    __instance.maxY = __instance._height - 1;
            }
            
            return false;
        }

        static bool MapSO_ConstructBilinearCoords_Double(MapSO __instance, double x, double y)
        {
            // X wraps around as it is longitude.
            x = Math.Abs(x - Math.Floor(x));

            __instance.centerXD = x * __instance._width;
            __instance.minX = (int)__instance.centerXD;
            __instance.maxX = __instance.minX + 1;
            __instance.midX = (float)__instance.centerXD - __instance.minX;
            if (__instance.maxX == __instance._width)
                __instance.maxX = 0;

            // Y clamps as it is latitude and the poles don't wrap to each other.
            if (y >= 1.0) 
                y = lessThanOneDouble;
            else if (y < 0.0) 
                y = 0.0;

            __instance.centerYD = y * __instance._height;
            __instance.minY = (int)__instance.centerYD;
            __instance.maxY = __instance.minY + 1;
            __instance.midY = (float)__instance.centerYD - __instance.minY;
            if (__instance.maxY == __instance._height)
            {
                if (ReferenceEquals(__instance, moho_height))
                    __instance.maxY = 0; // use incorrect wrapping for moho
                else
                    __instance.maxY = __instance._height - 1;
            }

            return false;
        }


    }
}
