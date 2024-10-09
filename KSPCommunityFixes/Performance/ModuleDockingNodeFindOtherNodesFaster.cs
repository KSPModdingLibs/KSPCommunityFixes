// ModuleDockingNode.FindNodeApproaches() is called n² times where n is the amount of loaded docking port modules in the scene.
// We optimize the method, mainly by avoiding going through the hashset of modules types if there is only one type defined
// which is seemingly always the case (I didn't found any stock or modded part having multiple ones).
// Test case with 20 docking ports : 1.6% of the frame time in stock, 0.8% with the patch

using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace KSPCommunityFixes.Performance
{
    internal class ModuleDockingNodeFindOtherNodesFaster : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(ModuleDockingNode), nameof(ModuleDockingNode.FindNodeApproaches));

            AddPatch(PatchType.Postfix, typeof(ModuleDockingNode), nameof(ModuleDockingNode.OnLoad));
        }

        // Very unlikely to be necessary, but in theory nodeType could differ from the string stored 
        // in the nodeTypes hashset due to space chars sanitizing.
        private static void ModuleDockingNode_OnLoad_Postfix(ModuleDockingNode __instance)
        {
            if (__instance.nodeTypes.Count == 1)
                __instance.nodeType = __instance.nodeTypes.First();
        }

        private static bool ModuleDockingNode_FindNodeApproaches_Prefix(ModuleDockingNode __instance, out ModuleDockingNode __result)
        {
            __result = null;
            if (__instance.part.packed)
                return false;

            for (int i = FlightGlobals.VesselsLoaded.Count; i-- > 0;)
            {
                Vessel vessel = FlightGlobals.VesselsLoaded[i];

                if (vessel.packed)
                    continue;

                for (int j = vessel.dockingPorts.Count; j-- > 0;)
                {
                    if (!(vessel.dockingPorts[j] is ModuleDockingNode other))
                        continue;

                    if (other.part.IsNullOrDestroyed()
                        || other.part.RefEquals(__instance.part)
                        || other.part.State == PartStates.DEAD
                        || other.state != __instance.st_ready.name
                        || other.gendered != __instance.gendered
                        || (__instance.gendered && other.genderFemale == __instance.genderFemale)
                        || other.snapRotation != __instance.snapRotation
                        || (__instance.snapRotation && other.snapOffset != __instance.snapOffset))
                    {
                        continue;
                    }

                    bool checkRequired = false;
                    // fast path when only one node type
                    if (__instance.nodeTypes.Count == 1)
                    {
                        if (other.nodeTypes.Count == 1)
                            checkRequired = __instance.nodeType == other.nodeType;
                        else
                            checkRequired = other.nodeTypes.Contains(__instance.nodeType);
                    }
                    // slow path checking the hashSet
                    else
                    {
                        foreach (string nodeType in __instance.nodeTypes)
                        {
                            if (other.nodeTypes.Count == 1)
                                checkRequired = nodeType == other.nodeType;
                            else
                                checkRequired = other.nodeTypes.Contains(nodeType);

                            if (checkRequired)
                                break;
                        }
                    }

                    if (checkRequired && __instance.CheckDockContact(__instance, other, __instance.acquireRange, __instance.acquireMinFwdDot, __instance.acquireMinRollDot))
                    {
                        __result = other;
                        return false;
                    }
                }
            }

            return false;
        }
    }
}
