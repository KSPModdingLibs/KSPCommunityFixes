using System;

namespace KSPCommunityFixes.Modding
{
    public class KSPFieldEnumDesc : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(BaseField), nameof(BaseField.GetStringValue));
        }

        internal static bool BaseField_GetStringValue_Prefix(BaseField __instance, object host, bool gui, ref string __result)
        {
            if (!gui) return true;

            Type fieldType = __instance.FieldInfo.FieldType;
            if (fieldType.IsEnum)
            {
                Enum val = (Enum)__instance.GetValue(host);
                __result = FastAndFixedEnumExtensions.EnumExtensions_displayDescription_Override(val);
                return false;
            }

            return true;
        }
    }
}
