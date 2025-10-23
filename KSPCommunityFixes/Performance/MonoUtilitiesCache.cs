using System.Collections.Generic;

namespace KSPCommunityFixes
{
    // MonoUtilites.RefreshContextWindows calls Object.FindObjectsOfType.
    // This is quite slow but doesn't show up in stock because the method isn't
    // used anywhere in the KSP codebase. Several mods, however, do make use of
    // it and this can account for a notable chunk of scene switch times.
    //
    // We patch UIPartActionWindow to indpendently track live instances and
    // then patch RefreshContextWindows to just use the list we are tracking.
    class MonoUtilitiesCache : BasePatch
    {
        static readonly List<UIPartActionWindow> PartActionWindows = new List<UIPartActionWindow>();

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Postfix, typeof(UIPartActionWindow), nameof(UIPartActionWindow.Awake));
            AddPatch(PatchType.Postfix, typeof(UIPartActionWindow), nameof(UIPartActionWindow.OnDestroy));
            AddPatch(PatchType.Override, typeof(MonoUtilities), nameof(MonoUtilities.RefreshContextWindows));
        }

        static void UIPartActionWindow_Awake_Postfix(UIPartActionWindow __instance)
        {
            PartActionWindows.Add(__instance);
        }

        static void UIPartActionWindow_OnDestroy_Postfix(UIPartActionWindow __instance)
        {
            int i = 0;
            int j = 0;
            var paws = PartActionWindows;
            int count = paws.Count;

            for (; i < count; ++i)
            {
                var paw = paws[i];
                if (ReferenceEquals(paw, __instance) || paw == null)
                    continue;

                paws[j++] = paw;
            }

            paws.RemoveRange(j, count - j);
        }

        static void MonoUtilities_RefreshContextWindows_Override(Part part)
        {
            var paws = PartActionWindows;
            int count = paws.Count;

            for (int i = 0; i < count; ++i)
            {
                var paw = paws[i];
                if (paw == null)
                    continue;
                if (paw.part != part)
                    continue;
                paw.displayDirty = true;
            }
        }
    }
}