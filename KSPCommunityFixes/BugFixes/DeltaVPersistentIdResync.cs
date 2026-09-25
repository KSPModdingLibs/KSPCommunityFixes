using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    // DeltaVStageInfo tracks the parts of its stage twice : in the `parts` list and in
    // `partInfoDictionary`, keyed by the part persistentId read at the time the part was added.
    // ProcessParts() only adds a part to the stage if the dictionary doesn't contain its *current*
    // persistentId. If the persistentId of a part changes while the part is still on the vessel
    // (FlightGlobals.CheckPartpersistentId() resolving a collision, or mods reassigning it, ScrapYard
    // does it every time it applies its inventory in the editor and on rollout), the stale entry is
    // never removed and the same DeltaVPartInfo is added again. Every persistentId change adds one more
    // copy. The duplicated parts are then fed to SCCFlowGraph, causing the
    // "[FlowGraph]: Graph already contains item!" log spam, and are counted more than once in the stage
    // mass, resulting in wrong (usually much too high) start mass and wrong delta-v in the stage UI.
    // We resync the dictionary keys with the current persistentIds before ProcessParts() runs, and
    // remove duplicated entries if some were already added.
    public class DeltaVPersistentIdResync : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Prefix, typeof(DeltaVStageInfo), nameof(DeltaVStageInfo.ProcessParts));
        }

        private static readonly HashSet<Part> partsBuffer = new HashSet<Part>();
        private static readonly List<KeyValuePair<uint, DeltaVPartInfo>> deadEntriesBuffer = new List<KeyValuePair<uint, DeltaVPartInfo>>();

        static void DeltaVStageInfo_ProcessParts_Prefix(DeltaVStageInfo __instance)
        {
            List<DeltaVPartInfo> parts = __instance.parts;
            Dictionary<uint, DeltaVPartInfo> partInfoDictionary = __instance.partInfoDictionary;
            if (parts == null || partInfoDictionary == null)
                return;

            // Fast path : every entry still keyed by the current persistentId of its part.
            // Entries whose part is gone are left alone, ProcessParts() removes them.
            bool keysInSync = true;
            foreach (KeyValuePair<uint, DeltaVPartInfo> entry in partInfoDictionary)
            {
                Part part = entry.Value?.part;
                if (part.IsNotNullOrDestroyed() && part.persistentId != entry.Key)
                {
                    keysInSync = false;
                    break;
                }
            }

            if (keysInSync)
            {
                if (parts.Count == partInfoDictionary.Count)
                    return;

                // The counts can also differ because two different parts share a persistentId, which
                // we don't handle (stock behavior). Only go further if a part is actually duplicated.
                bool hasDuplicates = false;
                for (int i = 0; i < parts.Count; i++)
                {
                    Part part = parts[i]?.part;
                    if (part.IsNotNullOrDestroyed() && !partsBuffer.Add(part))
                    {
                        hasDuplicates = true;
                        break;
                    }
                }
                partsBuffer.Clear();

                if (!hasDuplicates)
                    return;
            }

            try
            {
                foreach (KeyValuePair<uint, DeltaVPartInfo> entry in partInfoDictionary)
                    if (entry.Value?.part.IsNullOrDestroyed() ?? true)
                        deadEntriesBuffer.Add(entry);

                partInfoDictionary.Clear();

                for (int i = 0; i < parts.Count; i++)
                {
                    Part part = parts[i]?.part;
                    if (part.IsNullOrDestroyed())
                        continue;

                    if (!partsBuffer.Add(part))
                    {
                        parts.RemoveAt(i);
                        i--;
                        continue;
                    }

                    // Two different parts sharing a persistentId can't both be in the dictionary,
                    // this is the stock behavior too. Keep the first one.
                    if (!partInfoDictionary.ContainsKey(part.persistentId))
                        partInfoDictionary.Add(part.persistentId, parts[i]);
                }

                // keep the stale key of entries whose part is gone, so ProcessParts() removes them as usual
                foreach (KeyValuePair<uint, DeltaVPartInfo> entry in deadEntriesBuffer)
                    if (!partInfoDictionary.ContainsKey(entry.Key))
                        partInfoDictionary.Add(entry.Key, entry.Value);
            }
            finally
            {
                partsBuffer.Clear();
                deadEntriesBuffer.Clear();
            }

            // the cached part lists used by the simulation are derived from `parts`
            __instance.ResetPartCaches();
        }
    }
}
