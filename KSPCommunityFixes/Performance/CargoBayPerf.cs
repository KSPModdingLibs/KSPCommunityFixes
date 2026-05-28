// ModuleCargoBay.onVesselModified is called once per cargo bay per
// onVesselWasModified event. Each call (when the guard matches) triggers a
// full EnableShieldedVolume recompute, which iterates every part and every
// module on the vessel twice, does a Physics.OverlapSphere query, and
// raycasts every collider in the bay against every nearby part renderer.
//
// When a large ship breaks apart it fires onVesselWasModified many times in
// quick succession (once per joint break / vessel split). Every cargo bay
// reacts to every event, so the total work scales as
//     O(events × cargo_bays × (vessel_parts × modules + physics))
// and a 1000-part ship breakup can chew through entire frames inside
// EnableShieldedVolume even though the shielding result isn't consumed
// between joint-break events.
//
// Fix: defer EnableShieldedVolume to the end of the current physics step.
// On the first event we add the bay to a HashSet (for dedup) and start a
// coroutine that yields on WaitForFixedUpdate; subsequent events from the
// same physics step add to the same set without rescheduling. When the
// coroutine resumes — after the physics update completes, before the next
// Update / FixedUpdate pair starts — it runs ColliderListCleanUp +
// EnableShieldedVolume once per queued bay.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.Performance;

internal class CargoBayPerf : BasePatch
{
    protected override void ApplyPatches()
    {
        AddPatch(PatchType.Override, typeof(ModuleCargoBay), nameof(ModuleCargoBay.onVesselModified));
        AddPatch(PatchType.Override, typeof(ModuleCargoBay), nameof(ModuleCargoBay.ColliderListCleanUp));
    }

    // Replacement for ModuleCargoBay.onVesselModified that keeps the cheap
    // "this event concerns me" guard but routes the expensive recompute
    // through the per-frame batcher instead of running it inline.
    static void ModuleCargoBay_onVesselModified_Override(ModuleCargoBay __instance, Vessel v)
    {
        if (v != __instance.vessel)
        {
            Vector3 lookupWorld = __instance.part.partTransform.TransformPoint(__instance.lookupCenter);
            float radius = __instance.lookupRadius;
            if ((v.vesselTransform.position - lookupWorld).sqrMagnitude >= radius * radius)
                return;
        }

        var id = __instance.GetInstanceIDFast();
        if (!pending.Add(id))
            return;

        queue.Add(__instance);
        coroutine ??= KSPCommunityFixes.Instance.StartCoroutine(DelayedCargoBayOnVesselModified());
    }

    static void ModuleCargoBay_ColliderListCleanUp_Override(ModuleCargoBay __instance)
    {
        var colliders = __instance.ownColliders;
        int j = 0;
        int count = colliders.Count;
        for (int i = 0; i < count; ++i)
        {
            var collider = colliders[i];
            if (collider is null
                || collider.collider.IsNullOrDestroyed()
                || collider.owner.vessel != __instance.vessel)
            {
                continue;
            }

            colliders[j++] = collider;
        }

        colliders.RemoveRange(j, count - j);
    }

    static readonly WaitForFixedUpdate WaitForFixedUpdate = new();
    static readonly HashSet<int> pending = [];
    static readonly List<ModuleCargoBay> queue = [];
    static Coroutine coroutine = null;

    static IEnumerator DelayedCargoBayOnVesselModified()
    {
        using var guard = new ClearCoroutineGuard();
        yield return WaitForFixedUpdate;

        foreach (var module in queue)
        {
            if (module.IsNullOrDestroyed())
                continue;

            try
            {
                module.ColliderListCleanUp();
                module.EnableShieldedVolume();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    readonly struct ClearCoroutineGuard : IDisposable
    {
        public void Dispose()
        {
            coroutine = null;
            pending.Clear();
            queue.Clear();
        }

    }
}
