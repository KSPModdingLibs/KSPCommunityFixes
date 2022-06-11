using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.Modding
{
    public class OnSymmetryFieldChanged : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(UIPartActionFieldItem), nameof(UIPartActionFieldItem.SetSymCounterpartValue)),
                this));
        }

        static bool UIPartActionFieldItem_SetSymCounterpartValue_Prefix(UIPartActionFieldItem __instance, object value, out bool __result)
        {
            __result = false;
            if (__instance.isModule)
            {
                if (__instance.part.IsNotNullOrDestroyed() && __instance.partModule.IsNotNullOrDestroyed())
                {
                    int moduleIndex = __instance.part.Modules.IndexOf(__instance.partModule);
                    int symPartCount = __instance.part.symmetryCounterparts.Count;
                    for (int i = 0; i < symPartCount; i++)
                    {
                        Part part = __instance.part.symmetryCounterparts[i];
                        PartModule partModule;
                        if (moduleIndex < part.Modules.Count && part.Modules[moduleIndex].IsNotNullOrDestroyed() && __instance.partModule.GetType() == part.Modules[moduleIndex].GetType())
                        {
                            partModule = part.Modules[moduleIndex];
                            object oldValue = partModule.Fields.GetValue(__instance.field.name);
                            bool changed = object.Equals(oldValue, value);
                            __result = __result || !changed;
                            if (changed)
                            {
                                partModule.Fields.SetValue(__instance.field.name, value);
                                __instance.FireSymmetryEvents(partModule.Fields[__instance.field.name], oldValue);
                            }
                            continue;
                        }
                        partModule = part.Modules[__instance.partModule.ClassName];
                        if (partModule.IsNotNullOrDestroyed())
                        {
                            object oldValue = partModule.Fields.GetValue(__instance.field.name);
                            bool changed = object.Equals(oldValue, value);
                            __result = __result || !changed;
                            if (changed)
                            {
                                partModule.Fields.SetValue(__instance.field.name, value);
                                __instance.FireSymmetryEvents(partModule.Fields[__instance.field.name], oldValue);
                            }
                        }
                    }
                }
            }
            else if (__instance.part.IsNotNullOrDestroyed())
            {
                int partCount = __instance.part.symmetryCounterparts.Count;
                for (int j = 0; j < partCount; j++)
                {
                    Part part = __instance.part.symmetryCounterparts[j];
                    object oldValue = part.Fields.GetValue(__instance.field.name);
                    bool changed = object.Equals(oldValue, value);
                    __result = __result || !changed;
                    if (changed)
                    {
                        part.Fields.SetValue(__instance.field.name, value);
                    }
                }
            }

            return false;
        }
    }
}
