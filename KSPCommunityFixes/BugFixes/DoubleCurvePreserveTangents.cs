using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Reflection.Emit;

namespace KSPCommunityFixes.BugFixes
{
    class DoubleCurvePreserveTangents : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(DoubleCurve), nameof(DoubleCurve.RecomputeTangents));
        }

        static bool DoubleCurve_RecomputeTangents_Prefix(DoubleCurve __instance)
        {
            int count = __instance.keys.Count;
            DoubleKeyframe doubleKeyframe;
            if (count == 1)
            {
                return false;
            }
            doubleKeyframe = __instance.keys[0];
            if (doubleKeyframe.autoTangent)
            {
                doubleKeyframe.inTangent = 0.0;
                doubleKeyframe.outTangent = (__instance.keys[1].value - doubleKeyframe.value) / (__instance.keys[1].time - doubleKeyframe.time);
                __instance.keys[0] = doubleKeyframe;
            }
            int num3 = count - 1;
            doubleKeyframe = __instance.keys[num3];
            if (doubleKeyframe.autoTangent)
            {
                doubleKeyframe.inTangent = (doubleKeyframe.value - __instance.keys[num3 - 1].value) / (doubleKeyframe.time - __instance.keys[num3 - 1].value);
                doubleKeyframe.outTangent = 0.0;
                __instance.keys[num3] = doubleKeyframe;
            }
            if (count > 2)
            {
                for (int i = 1; i < num3; i++)
                {
                    doubleKeyframe = __instance.keys[i];
                    if (doubleKeyframe.autoTangent)
                    {
                        double num4 = (doubleKeyframe.value - __instance.keys[i - 1].value) / (doubleKeyframe.time - __instance.keys[i - 1].value);
                        double num5 = (__instance.keys[i + 1].value - doubleKeyframe.value) / (__instance.keys[i + 1].time - doubleKeyframe.time);
                        doubleKeyframe.inTangent = (doubleKeyframe.outTangent = (num4 + num5) * 0.5);
                    }
                }
            }
            return false;
        }
    }
}
