using System;
using System.Collections;
using System.Collections.Generic;
using KSP.UI.Screens;

namespace KSPCommunityFixes.BugFixes
{
    internal class DuplicateAppLauncherButtons : BasePatch
    {
        // The apps that are part way through adding themselves, by instance id. Keyed on the id
        // rather than the app because a destroyed UnityEngine.Object compares equal to null.
        static readonly HashSet<int> addsInProgress = new HashSet<int>();

        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(UIApp), "AddToAppLauncher");
            AddPatch(PatchType.Postfix, typeof(UIApp), "set_appLauncherButton",
                nameof(UIApp_SetAppLauncherButton_Postfix));
            AddPatch(PatchType.Postfix, typeof(UIApp), "OnDestroy");
        }

        static bool UIApp_AddToAppLauncher_Prefix(UIApp __instance, ref IEnumerator __result)
        {
            if (IsOnAppLauncher(__instance.appLauncherButton) ||
                !addsInProgress.Add(__instance.GetInstanceID()))
            {
                // Already on the toolbar, or on its way there. Going again would overwrite the only
                // reference to the button it ends up with, leaving the other one on the toolbar for
                // the rest of the session, and would run OnAppInitialized a second time.
                __result = Nothing();
                return false;
            }

            return true;
        }

        static void UIApp_SetAppLauncherButton_Postfix(UIApp __instance)
        {
            addsInProgress.Remove(__instance.GetInstanceID());
        }

        static void UIApp_OnDestroy_Postfix(UIApp __instance)
        {
            // The app can be destroyed part way through adding itself, including by
            // OnAppInitialized when it turns out to be a second instance.
            addsInProgress.Remove(__instance.GetInstanceID());
        }

        static bool IsOnAppLauncher(ApplicationLauncherButton button)
        {
            if (button.IsNullOrDestroyed())
                return false;

            // The button outlives the launcher being torn down and built again, so holding one is
            // not on its own enough to say the app is still on the toolbar.
            ApplicationLauncher launcher = ApplicationLauncher.Instance;
            if (launcher.IsNullOrDestroyed())
                return false;

            return launcher.appList.Contains(button) || launcher.appListHidden.Contains(button);
        }

        static IEnumerator Nothing()
        {
            yield break;
        }
    }
}
