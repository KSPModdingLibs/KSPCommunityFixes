using System;
using System.Collections.Generic;
using HarmonyLib;

namespace KSPCommunityFixes.Modding
{
    class DockingPortLockedEvents : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 2);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            // Priority.First because we have an overriding prefix in the DockingPortRotationDriftAndFixes patch
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), "ModifyLocked"),
                this, null, Priority.First));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDockingNode), "ModifyLocked"),
                this));
        }

        static void ModuleDockingNode_ModifyLocked_Prefix(ModuleDockingNode __instance)
        {
            GameEvents.onRoboticPartLockChanging.Fire(__instance.part, __instance.nodeIsLocked);
        }

        static void ModuleDockingNode_ModifyLocked_Postfix(ModuleDockingNode __instance)
        {
            GameEvents.onRoboticPartLockChanged.Fire(__instance.part, __instance.nodeIsLocked);
        }
    }
}
