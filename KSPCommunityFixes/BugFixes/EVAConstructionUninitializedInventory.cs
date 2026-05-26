// see https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/378

using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes;

internal class EVAConstructionUninitializedInventory : BasePatch
{
    protected override Version VersionMin => new(1, 12, 3);

    protected override void ApplyPatches()
    {
        AddPatch(PatchType.Override, typeof(EVAConstructionModeController), nameof(EVAConstructionModeController.SearchForInventoryParts));
    }

    // This is called every frame whenever the construction panel is open.
    // The stock version registers any ModuleInventoryPart it finds, without
    // checking whether that part is fully initialized.
    //
    // We modify it here to wait until `part.started` is true before yielding the part.
    private static void EVAConstructionModeController_SearchForInventoryParts_Override(EVAConstructionModeController instance)
    {
        Dictionary<uint, ModuleInventoryPart> loadedModuleInventoryPart = instance.loadedModuleInventoryPart;
        List<Vessel> vesselsLoaded = FlightGlobals.VesselsLoaded;
        foreach (Vessel vessel in vesselsLoaded)
        {
            List<Part> parts = vessel.parts;
            foreach (Part part in parts)
            {
                // KSPCF: don't consider parts that haven't finished initializing yet.
                if (!part.started)
                    continue;

                ModuleInventoryPart moduleInventoryPart = part.FindModuleImplementing<ModuleInventoryPart>();
                bool isEVAKerbal = part.isKerbalEVA() && parts.Count > 1;
                if (moduleInventoryPart.IsNotNullOrDestroyed()
                    && !isEVAKerbal
                    && !loadedModuleInventoryPart.ContainsKey(part.persistentId))
                {
                    loadedModuleInventoryPart.Add(part.persistentId, moduleInventoryPart);
                }

                if (vessel.isEVA || part.protoModuleCrew == null)
                    continue;

                foreach (ProtoCrewMember crewMember in part.protoModuleCrew)
                {
                    ModuleInventoryPart kerbalInventoryModule = crewMember.KerbalInventoryModule;
                    if (kerbalInventoryModule.IsNullOrDestroyed())
                        continue;

                    kerbalInventoryModule.transform.position = part.transform.position;
                    loadedModuleInventoryPart.TryAdd(crewMember.persistentID, kerbalInventoryModule);
                }
            }
        }
    }
}
