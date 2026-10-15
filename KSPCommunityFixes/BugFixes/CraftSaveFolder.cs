using System;
using System.IO;
using KSP.UI;
using KSP.UI.Screens;

namespace KSPCommunityFixes.BugFixes
{
    class CraftSaveFolder : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 5);
        protected override Version VersionMax => new Version(1, 12, 5);

        private static UICraftSaveFlyoutController saveFlyout;
        private static EditorLogic folderOwner;

        private enum FlightTransition { None, Launch, RevertLaunch, RevertEditor }
        private static FlightTransition flightTransition;
        private static string launchFolder;
        private static string launchSave;
        private static EditorFacility launchFacility;
        private static string restoreFolder;
        private static EditorLogic restoreEditor;
        private static bool restoreEditorLoaded;

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(EditorLogic), nameof(EditorLogic.StartEditor));
            AddPatch(PatchType.Postfix, typeof(EditorLogic), nameof(EditorLogic.StartEditor));
            AddPatch(PatchType.Postfix, typeof(CraftBrowserDialog), nameof(CraftBrowserDialog.onPipelineFinished));
            AddPatch(PatchType.Prefix, typeof(UICraftSaveFlyoutController), "Start");
            AddPatch(PatchType.Postfix, typeof(UICraftSaveFlyoutController), "Start");
            AddPatch(PatchType.Postfix, typeof(UICraftSaveFlyoutController), "OnDestroy");
            AddPatch(PatchType.Prefix, typeof(EditorLogic), nameof(EditorLogic.OnNewShipConfirm));
            AddPatch(PatchType.Prefix, typeof(EditorDriver), nameof(EditorDriver.SwitchEditor));
            AddPatch(PatchType.Prefix, typeof(FlightDriver), nameof(FlightDriver.StartWithNewLaunch));
            AddPatch(PatchType.Prefix, typeof(FlightDriver), nameof(FlightDriver.RevertToLaunch));
            AddPatch(PatchType.Prefix, typeof(FlightDriver), nameof(FlightDriver.RevertToPrelaunch));
            AddPatch(PatchType.Prefix, typeof(HighLogic), nameof(HighLogic.SetLoadSceneEventsAndFlags));
            AddPatch(PatchType.Finalizer, typeof(FlightDriver), nameof(FlightDriver.StartWithNewLaunch), nameof(FlightTransition_Finalizer));
            AddPatch(PatchType.Finalizer, typeof(FlightDriver), nameof(FlightDriver.RevertToLaunch), nameof(FlightTransition_Finalizer));
            AddPatch(PatchType.Finalizer, typeof(FlightDriver), nameof(FlightDriver.RevertToPrelaunch), nameof(FlightTransition_Finalizer));
        }

        static void EditorLogic_StartEditor_Prefix(EditorLogic __instance, bool isRestart)
        {
            folderOwner = null;
            // A revert token belongs only to the initial scene entry. A new/load/switch
            // restart must not let a late-starting flyout resurrect the reverted folder.
            if (restoreFolder == null)
                return;
            if (isRestart || (!ReferenceEquals(restoreEditor, null) && !ReferenceEquals(restoreEditor, __instance)))
                ClearLaunchFolder();
            else
                restoreEditor = __instance;
        }

        static void EditorLogic_StartEditor_Postfix(EditorLogic __instance)
        {
            if (restoreFolder != null)
            {
                if (IsRestoreContext() && ReferenceEquals(restoreEditor, __instance)
                    && __instance.ship != null && __instance.ship.parts.Count > 0
                    && SetFolder(restoreFolder))
                {
                    restoreEditorLoaded = true;
                    if (!ReferenceEquals(saveFlyout, null) && saveFlyout.saveFolderItems != null)
                        ConsumeRestore();
                }
                else
                    ClearLaunchFolder();
                return;
            }

            // Cache loads have no filename. Never reuse stale filePathToLoad except
            // for a real file load; a same-craft revert has its explicit token above.
            if (EditorDriver.StartupBehaviour == EditorDriver.StartupBehaviours.LOAD_FROM_FILE)
                SetLoadedFolder(__instance.ship, EditorDriver.filePathToLoad);
        }

        static void CraftBrowserDialog_onPipelineFinished_Postfix(CraftBrowserDialog __instance, ConfigNode n, CraftEntry sItem, CraftBrowserDialog.LoadType loadType)
        {
            // The editor browser loads a ConfigNode, not a file. Commit its source
            // directory after the upgrade pipeline AND the editor callback complete.
            // Other consumers of this shared dialog and merge operations are untouched.
            if (loadType == CraftBrowserDialog.LoadType.Normal && n != null
                && __instance.OnConfigNodeSelected?.Target is EditorLogic editor
                && ReferenceEquals(editor, EditorLogic.fetch))
                SetLoadedFolder(editor.ship, sItem.fullFilePath);
        }

        static void UICraftSaveFlyoutController_Start_Prefix(out string __state)
        {
            // The flyout can stay inactive until first opened. Its stock Start must
            // not erase a browser/config load or explicit default already committed
            // in this editor, nor inherit a different editor's stale static path.
            __state = ReferenceEquals(folderOwner, EditorLogic.fetch) && !ReferenceEquals(folderOwner, null)
                && Directory.Exists(EditorDriver.DefaultCraftSavePath)
                ? EditorDriver.DefaultCraftSavePath : null;
        }

        static void UICraftSaveFlyoutController_Start_Postfix(UICraftSaveFlyoutController __instance, string __state)
        {
            saveFlyout = __instance;
            if (restoreFolder != null)
            {
                if (!IsRestoreContext())
                    ClearLaunchFolder();
                else if (restoreEditorLoaded)
                {
                    if (SetFolder(restoreFolder))
                        ConsumeRestore();
                    else
                        ClearLaunchFolder();
                }
                return;
            }
            if (__state != null)
                SetFolder(__state);
        }

        static void UICraftSaveFlyoutController_OnDestroy_Postfix(UICraftSaveFlyoutController __instance)
        {
            if (ReferenceEquals(saveFlyout, __instance))
                saveFlyout = null;
        }

        private static void SetLoadedFolder(ShipConstruct ship, string path)
        {
            // Missions have their own save/overwrite paths and no normal folder flyout.
            if (HighLogic.CurrentGame?.IsMissionMode == true)
                return;

            // Failed stock loads fall back to an empty construct; a non-null ship alone
            // is not evidence of success. Never change state for a failed/canceled load.
            if (ship == null || ship.parts.Count == 0 || !File.Exists(path)
                || !string.Equals(Path.GetExtension(path), ".craft", StringComparison.OrdinalIgnoreCase))
                return;

            SetFolder(new FileInfo(path).DirectoryName);
        }

        private static bool SetFolder(string folder)
        {
            if (HighLogic.CurrentGame?.IsMissionMode == true || !EditorDriver.SetDefaultSaveFolder(folder))
                return false;
            folderOwner = EditorLogic.fetch;
            SynchronizeFolderSelection();
            return true;
        }

        private static void SynchronizeFolderSelection()
        {
            if (ReferenceEquals(saveFlyout, null) || saveFlyout.saveFolderItems == null)
                return;

            string path = EditorDriver.DefaultCraftSavePath;
            EditorSaveFolderItem selected = null;
            foreach (EditorSaveFolderItem item in saveFlyout.saveFolderItems)
            {
                if (!item.IsOpenFileBrowserRequest && SameFolder(item.saveFolderPath, path))
                {
                    selected = item;
                    break;
                }
            }

            if (ReferenceEquals(selected, null))
                selected = saveFlyout.CreateFolderSaveItem(path, 0);

            // Keep stock's exact path spelling, including the root's trailing separator.
            EditorDriver.SetDefaultSaveFolder(selected.saveFolderPath);
            if (!ReferenceEquals(selected.toggleSetDefault.group, saveFlyout.selectedToggleGroup))
            {
                selected.toggleSetDefault.group = saveFlyout.selectedToggleGroup;
                saveFlyout.selectedToggleGroup.RegisterToggle(selected.toggleSetDefault);
            }

            // Stock only turns the matching toggle on. An inactive flyout doesn't
            // notify ToggleGroup, so the old root can stay on and win when reopened.
            // Set every bit explicitly, without firing folder-selection callbacks.
            selected.toggleSetDefault.SetIsOnWithoutNotify(true);
            foreach (EditorSaveFolderItem item in saveFlyout.saveFolderItems)
            {
                if (!ReferenceEquals(item, selected))
                    item.toggleSetDefault.SetIsOnWithoutNotify(false);
            }
            saveFlyout.groupSet = true;
        }

        static void FlightDriver_StartWithNewLaunch_Prefix(string fullFilePath)
        {
            ClearLaunchFolder();
            EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || ReferenceEquals(editor, null)
                || editor.ship == null || editor.ship.parts.Count == 0
                || fullFilePath != editor.savedCraftPath || HighLogic.CurrentGame?.IsMissionMode == true
                || !Directory.Exists(EditorDriver.DefaultCraftSavePath))
                return;

            // Capture the effective UI choice at the actual launch, not the load path
            // or the earlier launch button/preflight checks, which can be canceled.
            launchFolder = EditorDriver.DefaultCraftSavePath;
            launchSave = HighLogic.SaveFolder;
            launchFacility = EditorDriver.editorFacility;
            flightTransition = FlightTransition.Launch;
        }

        static void FlightDriver_RevertToLaunch_Prefix()
        {
            if (HighLogic.LoadedSceneIsFlight && IsLaunchContext())
                flightTransition = FlightTransition.RevertLaunch;
            else
                ClearLaunchFolder();
        }

        static void FlightDriver_RevertToPrelaunch_Prefix(EditorFacility facility)
        {
            ConsumeRestore();
            if (HighLogic.LoadedSceneIsFlight && IsLaunchContext() && facility == launchFacility
                && ShipConstruction.ShipConfig != null && FlightDriver.PreLaunchState != null)
                flightTransition = FlightTransition.RevertEditor;
            else
                ClearLaunchFolder();
        }

        static void HighLogic_SetLoadSceneEventsAndFlags_Prefix(GameScenes scene)
        {
            folderOwner = null;
            // Consume a narrow transition permit before stock fires scene events.
            // Every unrelated scene request invalidates the launch snapshot.
            FlightTransition transition = flightTransition;
            flightTransition = FlightTransition.None;
            if (launchFolder == null)
                return;

            if ((transition == FlightTransition.Launch && HighLogic.LoadedSceneIsEditor && scene == GameScenes.FLIGHT)
                || (transition == FlightTransition.RevertLaunch && HighLogic.LoadedSceneIsFlight && scene == GameScenes.FLIGHT))
                ConsumeRestore();
            else if (transition == FlightTransition.RevertEditor && HighLogic.LoadedSceneIsFlight && scene == GameScenes.EDITOR)
            {
                restoreFolder = launchFolder;
                restoreEditor = null;
                restoreEditorLoaded = false;
            }
            else
                ClearLaunchFolder();
        }

        static Exception FlightTransition_Finalizer(Exception __exception)
        {
            // A failed/canceled entry must not authorize a later unrelated scene load.
            if (__exception != null || flightTransition != FlightTransition.None)
                ClearLaunchFolder();
            return __exception;
        }

        static void EditorLogic_OnNewShipConfirm_Prefix()
        {
            ClearLaunchFolder();
        }

        static void EditorDriver_SwitchEditor_Prefix(EditorFacility facility)
        {
            if (!ReferenceEquals(EditorDriver.fetch, null) && facility != EditorDriver.editorFacility)
                ClearLaunchFolder();
        }

        private static bool IsLaunchContext()
        {
            return launchFolder != null && launchSave == HighLogic.SaveFolder
                && HighLogic.CurrentGame?.IsMissionMode != true;
        }

        private static bool IsRestoreContext()
        {
            return IsLaunchContext() && EditorDriver.editorFacility == launchFacility
                && EditorDriver.StartupBehaviour == EditorDriver.StartupBehaviours.LOAD_FROM_CACHE;
        }

        private static void ConsumeRestore()
        {
            restoreFolder = null;
            restoreEditor = null;
            restoreEditorLoaded = false;
        }

        private static void ClearLaunchFolder()
        {
            launchFolder = null;
            launchSave = null;
            launchFacility = EditorFacility.None;
            flightTransition = FlightTransition.None;
            ConsumeRestore();
        }

        private static bool SameFolder(string left, string right)
        {
            int leftLength = left.Length;
            int rightLength = right.Length;
            while (leftLength > 0 && IsDirectorySeparator(left[leftLength - 1]))
                leftLength--;
            while (rightLength > 0 && IsDirectorySeparator(right[rightLength - 1]))
                rightLength--;

            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return leftLength == rightLength
                && string.Compare(left, 0, right, 0, leftLength, comparison) == 0;
        }

        private static bool IsDirectorySeparator(char value)
        {
            return value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
        }
    }
}
