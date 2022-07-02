// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/52

using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    class EnginePlateAirstreamShieldedTopPart : BasePatch
    {
        protected override Version VersionMin => new Version(1, 11, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDecouplerBase), nameof(ModuleDecouplerBase.SetAirstream)),
                this));
        }

        static bool ModuleDecouplerBase_SetAirstream_Prefix(ModuleDecouplerBase __instance, bool enclosed)
        {
            __instance.shroudOn = enclosed;

            if (__instance.jettisonModule.IsNullOrDestroyed())
                return false;

            List<AttachNode> attachNodes = __instance.part.attachNodes;
            AttachNode bottomNode = attachNodes.Find(node => node.id == __instance.jettisonModule.bottomNodeName);

            foreach (AttachNode attachNode in attachNodes)
            {
                if (attachNode.attachedPart.IsNullOrDestroyed())
                    continue;

                if (attachNode == bottomNode)
                    continue;

                if (Vector3.Angle(bottomNode.orientation, attachNode.orientation) < 30f)
                {
                    attachNode.attachedPart.ShieldedFromAirstream = enclosed;
                    if (enclosed)
                        attachNode.attachedPart.AddShield(__instance);
                    else
                        attachNode.attachedPart.RemoveShield(__instance);
                }
            }

            return false;
        }
    }
}
