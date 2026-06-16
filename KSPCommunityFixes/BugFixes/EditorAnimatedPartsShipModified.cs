// The Engineer's report (and anything else that listens to GameEvents.onEditorShipModified) only
// run when it is fired. Deployable parts fire the event when their animation starts, but not when
// it finishes. This makes it look like the editor's report is delayed by one modification.
//
// We fix this by firing an additional onEditorShipModified event when the animation completes.
// This is done by adding an OnStop event handler to all IScalarModules during part Start. This
// should work even for modded animation modules.

using System;
using System.Collections;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes;

class EditorAnimatedPartsShipModified : BasePatch
{
    protected override void ApplyPatches()
    {
        AddPatch(PatchType.Postfix, typeof(Part), nameof(Part.ModulesOnStartFinished));
    }

    static void Part_ModulesOnStartFinished_Postfix(Part __instance)
    {
        if (!HighLogic.LoadedSceneIsEditor)
            return;

        ScalarModuleStopListener listener = null;
        foreach (var module in __instance.Modules)
        {
            if (module is not IScalarModule scalarModule)
                continue;
            if (scalarModule.OnStop is null)
                continue;

            if (listener == null)
            {
                listener = __instance.gameObject.AddComponent<ScalarModuleStopListener>();
                listener.part = __instance;
            }

            scalarModule.OnStop.Add(listener.OnScalarModuleStop);
        }
    }

    class ScalarModuleStopListener : MonoBehaviour
    {
        static Coroutine coroutine = null;
        static ScalarModuleStopListener host = null;

        // The part we are attached to.
        public Part part;

        public void OnScalarModuleStop(float position)
        {
            if (coroutine is not null)
                return;

            EditorLogic editor = EditorLogic.fetch;
            if (editor.IsNullOrDestroyed() || editor.ship is null)
                return;

            // We only really want to fire this once per frame, so launch a coroutine if not already
            // done. The coroutine will fire the event in the next Update.
            coroutine = StartCoroutine(DelayEmit());
            host = this;
        }

        // If we own the current coroutine and are being disabled or destroyed, then fire the event
        // immediately.
        void OnDisable()
        {
            if (coroutine is null || host != this)
                return;

            StopCoroutine(coroutine);
            coroutine = null;
            host = null;

            var editor = EditorLogic.fetch;
            if (editor.IsNullOrDestroyed() || editor.ship is null)
                return;

            GameEvents.onEditorShipModified.Fire(editor.ship);
        }

        void OnDestroy()
        {
            // Make sure to unsubscribe ourselves from the OnStop events.
            foreach (var module in part.Modules)
            {
                if (module is IScalarModule scalarModule)
                    scalarModule.OnStop?.Remove(OnScalarModuleStop);
            }
        }

        IEnumerator DelayEmit()
        {
            using var guard = new ClearCoroutineGuard();
            yield return null;

            var editor = EditorLogic.fetch;
            if (editor.IsNullOrDestroyed() || editor.ship is null)
                yield break;

            GameEvents.onEditorShipModified.Fire(editor.ship);
        }

        readonly struct ClearCoroutineGuard : IDisposable
        {
            public void Dispose()
            {
                coroutine = null;
                host = null;
            }
        }
    }
}
