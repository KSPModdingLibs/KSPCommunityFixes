using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    class DockingPortLocking : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.OnStart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.UpdateAlignmentRotation)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.UpdatePAWUI)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleDockingNode), nameof(ModuleDockingNode.ModifyLocked)),
                this));
        }

        #region rotation locked state fixes

        static void ModuleDockingNode_OnStart_Postfix(ModuleDockingNode __instance)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            if (__instance.nodeIsLocked)
            {
                __instance.Fields["targetAngle"].guiActive = false;
                __instance.Fields["inverted"].guiActive = false;
            }
        }

        static bool ModuleDockingNode_UpdateAlignmentRotation_Prefix(ModuleDockingNode __instance)
        {
            return !__instance.nodeIsLocked;
        }

        static void ModuleDockingNode_UpdatePAWUI_Postfix(ModuleDockingNode __instance)
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && __instance.canRotate)
            {
                bool isEnabled = __instance.otherNode != null && __instance.sameVesselDockJoint == null && !__instance.nodeIsLocked;
                __instance.Fields["targetAngle"].guiActive = isEnabled;
                __instance.Fields["inverted"].guiActive = isEnabled;
            }
        }

        static void ModuleDockingNode_ModifyLocked_Postfix(ModuleDockingNode __instance)
        {
            if (__instance.otherNode != null && __instance.otherNode.nodeIsLocked != __instance.nodeIsLocked)
            {
                __instance.otherNode.nodeIsLocked = __instance.nodeIsLocked;
                __instance.otherNode.ModifyLocked(null);
            }
        }

        #endregion

    }
}
