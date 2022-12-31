using HarmonyLib;
using System;
using System.Collections.Generic;
using static PartModule;

namespace KSPCommunityFixes.BugFixes
{
    internal class ChutePhantomSymmetry : BasePatch
    {
        protected override Version VersionMin => new Version(1, 10, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleParachute), nameof(ModuleParachute.OnStart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleParachute), nameof(ModuleParachute.OnDestroy)),
                this));
        }

        private static void ModuleParachute_OnStart_Postfix(ModuleParachute __instance, StartState state)
        {
            if (state == StartState.Editor)
                return;

            eventHandlers.Add(__instance, new EventHandler(__instance));
        }

        private static void ModuleParachute_OnDestroy_Postfix(ModuleParachute __instance)
        {
            if (eventHandlers.Remove(__instance, out EventHandler eventHandler))
                eventHandler.OnDestroy();
        }

        private static Dictionary<ModuleParachute, EventHandler> eventHandlers = new Dictionary<ModuleParachute, EventHandler>(16);

        private class EventHandler
        {
            private ModuleParachute instance;

            public EventHandler(ModuleParachute instance)
            {
                this.instance = instance;
                GameEvents.onPartDeCoupleNewVesselComplete.Add(OnEvent);
            }

            public void OnDestroy()
            {
                GameEvents.onPartDeCoupleNewVesselComplete.Remove(OnEvent);
            }

            private void OnEvent(Vessel oldVessel, Vessel newVessel)
            {
                if (newVessel.NotDestroyedRefNotEquals(instance.vessel))
                    return;

                int count = instance.part.symmetryCounterparts?.Count ?? 0;

                if (count > 0)
                    count++;

                instance.symmetryCount = count;
            }
        }
    }
}
