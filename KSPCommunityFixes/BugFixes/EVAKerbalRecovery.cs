// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/43

using HarmonyLib;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    public class EVAKerbalRecovery : BasePatch
    {
        protected override Version VersionMin => new Version(1, 11, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ProtoVessel), nameof(ProtoVessel.GetAllProtoPartsIncludingCargo)),
                this));
        }

        private static bool ProtoVessel_GetAllProtoPartsIncludingCargo_Prefix(ProtoVessel __instance, out List<ProtoPartSnapshot> __result)
        {
            int partCount = __instance.protoPartSnapshots.Count;
            __result = new List<ProtoPartSnapshot>(partCount * 2);
            for (int i = 0; i < partCount; i++)
            {
                ProtoPartSnapshot protoPartSnapshot = __instance.protoPartSnapshots[i];

                AvailablePart partInfoByName = PartLoader.getPartInfoByName(protoPartSnapshot.partInfo.name);
                if (partInfoByName == null)
                    continue;

                __result.Add(protoPartSnapshot);

                // if the part has an inventory module, instantiate protoparts for the cargo parts
                // this include EVA kerbal inventories
                for (int j = 0; j < protoPartSnapshot.modules.Count; j++)
                {
                    // note : this won't handle ModuleInventoryPart derivatives, but the same issue is present
                    // in many other places in the stock codebase, so we assume this is an unsupported scenario.
                    if (string.Equals(protoPartSnapshot.modules[j].moduleName, nameof(ModuleInventoryPart)))
                    {
                        AddProtoPartsFromInventoryNode(__result, __instance, protoPartSnapshot.modules[j].moduleValues);
                        break;
                    }
                }

                // if the part has some crew, add the cargo parts of their inventories,
                // unless the part is an EVA kerbal (its inventory was already handled in the above code)
                if (protoPartSnapshot.protoModuleCrew != null && protoPartSnapshot.protoModuleCrew.Count > 0 && !partInfoByName.name.StartsWith("kerbalEVA"))
                {
                    for (int j = 0; j < protoPartSnapshot.protoModuleCrew.Count; j++)
                    {
                        AddProtoPartsFromInventoryNode(__result, __instance, protoPartSnapshot.protoModuleCrew[j].InventoryNode);
                    }
                }
            }

            return false;
        }

        private static void AddProtoPartsFromInventoryNode(List<ProtoPartSnapshot> list, ProtoVessel pv, ConfigNode moduleInventoryNode)
        {
            if (moduleInventoryNode == null)
                return;

            ConfigNode storedPartsNode = null;
            if (!moduleInventoryNode.TryGetNode("STOREDPARTS", ref storedPartsNode))
                return;

            for (int k = 0; k < storedPartsNode.nodes.Count; k++)
            {
                ConfigNode storedPartNode = storedPartsNode.nodes[k];

                if (storedPartNode == null)
                    continue;

                int quantity = 1;
                storedPartNode.TryGetValue("quantity", ref quantity);
                ConfigNode protoPartSnapshotNode = null;

                if (!storedPartNode.TryGetNode("PART", ref protoPartSnapshotNode))
                    continue;

                ProtoPartSnapshot protoPartSnapshot = new ProtoPartSnapshot(protoPartSnapshotNode, pv, null);
                if (protoPartSnapshot != null)
                {
                    for (int l = 0; l < quantity; l++)
                    {
                        list.Add(protoPartSnapshot);
                    }
                }
            }
        }
    }
}
