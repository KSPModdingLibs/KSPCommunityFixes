using HarmonyLib;
using KSP.Localization;
using KSP.UI.Screens;
using KSP.UI.Screens.Editor;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.BugFixes
{
    // https://github.com/KSPModdingLibs/KSPCommunityFixes/issues/182

    class InventoryPartMass : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.UpdateCapacityValues)),
                this));

            var EditorPartIcon_Create_ArgTypes = new Type[]
            {
                typeof(EditorPartList),
                typeof(AvailablePart),
                typeof(StoredPart),
                typeof(float),
                typeof(float),
                typeof(float),
                typeof(Callback<EditorPartIcon>),
                typeof(bool),
                typeof(bool),
                typeof(PartVariant),
                typeof(bool),
                typeof(bool)
            };

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(KSP.UI.Screens.EditorPartIcon), nameof(KSP.UI.Screens.EditorPartIcon.Create), EditorPartIcon_Create_ArgTypes),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(InventoryPartListTooltip), nameof(InventoryPartListTooltip.CreateInfoWidgets)),
                this));

            // Making packedVolume persistent helps track what cargo modules *should* be if they were changed from the prefab before being added to the inventory
            StaticHelpers.EditPartModuleKSPFieldAttributes(typeof(ModuleCargoPart), nameof(ModuleCargoPart.packedVolume), field => field.isPersistant = true);
        }

        // the stock version of this function uses values from the prefab only, which is incorrect when mass modifiers are used (e.g. ModulePartVariants) or the packed volume is changed (e.g. TweakScale) or the resource levels are changed
        static bool ModuleInventoryPart_UpdateCapacityValues_Prefix(ModuleInventoryPart __instance)
        {
            __instance.volumeOccupied = 0.0f;
            __instance.massOccupied = 0.0f;
            foreach (StoredPart storedPart in __instance.storedParts.ValuesList)
            {
                if (storedPart != null && storedPart.snapshot != null)
                {
                    __instance.massOccupied += GetPartSnapshotMass(storedPart.snapshot) * storedPart.quantity; // This won't be correct if different parts in the stack have different mass modifiers, but really they shouldn't have been stacked in the first place
                    __instance.volumeOccupied += GetPartSnapshotVolume(storedPart.snapshot) * storedPart.quantity; // see above.
                }
            }
            __instance.UpdateMassVolumeDisplay(true, false);
            return false;
        }

        static float GetPartSnapshotMass(ProtoPartSnapshot partSnapshot)
        {
            double mass = partSnapshot.mass;

            foreach (var resource in partSnapshot.resources)
            {
                mass += resource.amount * resource.definition.density;
            }

            return (float)mass;
        }

        static float GetPartSnapshotVolume(ProtoPartSnapshot partSnapshot)
        {
            // fetch the volume from the cargo module snapshot
            foreach (var moduleSnapshot in partSnapshot.modules)
            {
                if (moduleSnapshot.moduleName != nameof(ModuleCargoPart)) continue;

                float packedVolume = 0;
                if (moduleSnapshot.moduleValues.TryGetValue(nameof(ModuleCargoPart.packedVolume), ref packedVolume))
                {
                    return packedVolume;
                }
            }

            // otherwise we have to fall back to the prefab volume (this is stock behavior)
            ModuleCargoPart moduleCargoPart = partSnapshot.partPrefab.FindModuleImplementing<ModuleCargoPart>();
            if (moduleCargoPart != null)
            {
                return moduleCargoPart.packedVolume;
            }
            return 0f;
        }

        // the game doesn't handle swapping variants very well for parts in inventories - mass and cost modifiers are not applied, etc.
        // It would be possible but messy and bug-prone to go modify the partsnapshot in the inventory when you swap variants
        // To sidestep the whole thing, just disallow changing variants for parts that have cost or mass modifiers while they're in inventory.
        static void EditorPartIcon_Create_Postfix(EditorPartIcon __instance, AvailablePart part, bool inInventory)
        {
            if (!inInventory || part.Variants == null || __instance.btnSwapTexture == null) return;

            foreach (var variant in part.Variants)
            {
                if (variant.cost != 0 || variant.mass != 0)
                {
                    __instance.btnSwapTexture.gameObject.SetActive(false);
                    return;
                }
            }
        }

        // The stock method gets the ModuleInfo strings from the prefab.  ModuleCargoPart reports the dry mass and packed volume of the part, and
        // swapping variants in the editor parts list will change this so that it doesn't reflect the state of the part that's actually in inventory.
        // We don't have a good way to get an updated moduleinfo from the part in inventory (it requires a live part, and it's not stored in the part snapshot)
        static void InventoryPartListTooltip_CreateInfoWidgets_Postfix(InventoryPartListTooltip __instance)
        {
            string moduleTitle = KSPUtil.PrintModuleName(nameof(ModuleCargoPart));

			// find the widget corresponding to ModuleCargoPart
			foreach (var moduleInfo in __instance.partInfo.moduleInfos)
            {
                if (moduleInfo.moduleName == moduleTitle)
                {
                    foreach (var widget in __instance.extInfoModules)
                    {
                        if (widget.gameObject.activeSelf && widget.textName.text == moduleInfo.moduleDisplayName)
                        {
                            widget.textInfo.text = GetModuleCargoPartInfo(__instance.inventoryStoredPart);
                            return;
                        }
                    }
                }
            }
        }

        // this is effectively ModuleCargoPart.GetInfo but can operate on a storedPart instead
        static string GetModuleCargoPartInfo(StoredPart storedPart)
        {
            float packedVolume = GetPartSnapshotVolume(storedPart.snapshot);
            int stackableQuantity = storedPart.snapshot.moduleCargoStackableQuantity;

            string text = "";
            text = ((!(packedVolume < 0f)) ? Localizer.Format("#autoLOC_8002220") : Localizer.Format("#autoLOC_6002641"));
            text += "\n\n";
            text = text + Localizer.Format("#autoLOC_8002186") + " " + storedPart.snapshot.mass.ToString("F3") + " t\\n";
            if (packedVolume > 0f)
            {
                text = text + Localizer.Format("#autoLOC_8004190", Localizer.Format("#autoLOC_8003414"), Localizer.Format("<<1>><<2>>", packedVolume.ToString("0.0"), "L")) + "\n";
            }
            if (stackableQuantity > 1)
            {
                text = text + Localizer.Format("#autoLOC_8004190", Localizer.Format("#autoLOC_8003418"), stackableQuantity.ToString("0")) + "\n";
            }
            return text;
        }
    }
}
