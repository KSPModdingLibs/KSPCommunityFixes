using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.Modding
{
    public class KSPFieldEnumDesc : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseField), "GetStringValue"),
                this));
        }

        internal static bool BaseField_GetStringValue_Prefix(BaseField __instance, object host, bool gui, ref string __result)
        {
            if (!gui) return true;

            Type fieldType = __instance.FieldInfo.FieldType;
            if (fieldType.IsEnum)
            {
                var val = (Enum)__instance.GetValue(host);
                __result = val.displayDescription();
                return false;
            }

            return true;
        }
    }
}
