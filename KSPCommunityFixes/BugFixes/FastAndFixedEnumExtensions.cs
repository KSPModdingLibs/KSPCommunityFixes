using KSP.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace KSPCommunityFixes
{
    internal class FastAndFixedEnumExtensions : BasePatch
    {
        internal static Dictionary<Type, Dictionary<string, EnumMemberDescription>> enumMemberDescriptionCache = new Dictionary<Type, Dictionary<string, EnumMemberDescription>>();

        internal class EnumMemberDescription
        {
            public string description;
            public string localizedDescription;

            public EnumMemberDescription(string memberName, DescriptionAttribute descriptionAttribute = null)
            {
                description = descriptionAttribute?.Description ?? memberName;
                localizedDescription = Localizer.Format(description);
            }
        }

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(EnumExtensions), nameof(EnumExtensions.Description));
            AddPatch(PatchType.Override, typeof(EnumExtensions), nameof(EnumExtensions.displayDescription));
        }

        internal static bool TryGetEnumMemberDescription(Enum enumValue, out EnumMemberDescription enumMemberDescription)
        {
            Type enumType = enumValue.GetType();
            if (!enumMemberDescriptionCache.TryGetValue(enumType, out Dictionary<string, EnumMemberDescription> enumDescriptions))
            {
                enumDescriptions = new Dictionary<string, EnumMemberDescription>();
                string[] names = enumType.GetEnumNames();

                foreach (string enumMemberName in names)
                {
                    MemberInfo[] enumMembers = enumType.GetMember(enumMemberName);
                    if (enumMembers.Length == 0)
                        continue;

                    DescriptionAttribute descriptionAttribute = enumMembers[0].GetCustomAttribute<DescriptionAttribute>();
                    Enum enumMember = (Enum)Enum.Parse(enumType, enumMemberName);
                    enumDescriptions.Add(enumMemberName, new EnumMemberDescription(enumMemberName, descriptionAttribute));
                }

                enumMemberDescriptionCache.Add(enumType, enumDescriptions);
            }

            if (enumDescriptions.TryGetValue(enumValue.ToString(), out enumMemberDescription))
                return true;

            return false;
        }

        internal static string EnumExtensions_Description_Override(Enum e)
        {
            if (!TryGetEnumMemberDescription(e, out EnumMemberDescription enumMemberDescription))
                return e.ToString();

            return enumMemberDescription.description;
        }

        internal static string EnumExtensions_displayDescription_Override(Enum e)
        {
            if (!TryGetEnumMemberDescription(e, out EnumMemberDescription enumMemberDescription))
                return Localizer.Format(e.ToString());

            return enumMemberDescription.localizedDescription;
        }
    }
}
