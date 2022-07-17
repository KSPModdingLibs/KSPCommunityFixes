using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.UI.Screens;
using KSP.UI.Screens.Editor;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KSPCommunityFixes
{
    public class UpgradeBugs : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), nameof(PartListTooltip.SetupUpgradeInfo)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartListTooltip), nameof(PartListTooltip.Setup), new Type[] { typeof(AvailablePart), typeof(Callback<PartListTooltip>), typeof(RenderTexture) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), nameof(PartListTooltip.DisplayExtendedInfo)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), nameof(PartListTooltip.CreateExtendedInfo)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartModule), nameof(PartModule.UpgradesAvailable), new Type[] { typeof(Part), typeof(ConfigNode) }),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Transpiler,
                AccessTools.Method(typeof(PartModule), nameof(PartModule.FindUpgrades)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.OnPodSpawn)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.SpawnPart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.SpawnPart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.GetModuleCosts)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartUpgradeHandler.Upgrade), nameof(PartUpgradeHandler.Upgrade.GetUsedByStrings)),
                this,
                "GetUsedBy_Prefix"));
        }

        // This was doing weird stuff with PartStatsUpgradeModules where it only applied its upgrades' costs.
        // and added an extendedInfo entry for just that.
        // We'll handle everything in CreateExtendedInfo instead.
        static bool PartListTooltip_SetupUpgradeInfo_Prefix(PartListTooltip __instance, ref AvailablePart availablePart)
        {
            __instance.upgradeState = PartModule.UpgradesAvailable(__instance.partRef, __instance.upgradeNode);
            if (__instance.upgradeState != PartModule.PartUpgradeState.AVAILABLE)
                return false;

            foreach (PartModule pm in __instance.partRef.Modules)
            {
                if (pm.HasUpgrades())
                    pm.ApplyUpgrades(PartModule.StartState.Editor);

                foreach (string upgradeName in pm.upgradesApplied)
                {
                    PartUpgradeHandler.Upgrade upgrade = PartUpgradeManager.Handler.GetUpgrade(upgradeName);
                    if (upgrade == null)
                        continue;

                    if (upgrade.cost != 0f)
                    {
                        if (upgrade.cumulativeCost)
                            __instance.upgradeCost += upgrade.cost;
                        else
                            __instance.upgradeCost = upgrade.cost;
                    }
                }
            }
            return false;
        }

        // Null out upgrade field if we're setting up a regular part
        static void PartListTooltip_Setup_Postfix(PartListTooltip __instance, ref AvailablePart availablePart)
        {
            __instance.upgrade = null;
        }

        // This stock method was never using CreateExtendedUpgradeInfo,
        // which generates the extended info that upgrades were *supposed* to display.
        static bool PartListTooltip_DisplayExtendedInfo_Prefix(PartListTooltip __instance, ref bool display, ref string rmbHintText)
        {
            if (__instance.HasExtendedInfo)
            {
                if (display)
                {
                    if (!__instance.hasCreatedExtendedInfo)
                    {
                        if (__instance.upgrade != null)
                        {
                            PartListTooltipWidget resourceWidget = __instance.GetNewTooltipWidget(__instance.extInfoRscWidgePrefab);
                            resourceWidget.Setup(KSP.Localization.Localizer.Instance.ReplaceSingleTagIfFound("#autoLOC_140995"), " ");
                            resourceWidget.transform.SetParent(__instance.extInfoListContainer.transform, false);
                            __instance.CreateExtendedUpgradeInfo(__instance.requiresEntryPurchase);
                            __instance.extInfoListSpacer.gameObject.SetActive(false);
                        }
                        else
                        {
                            __instance.CreateExtendedInfo(__instance.requiresEntryPurchase);
                        }
                        __instance.hasCreatedExtendedInfo = true;
                    }
                    __instance.panelExtended.SetActive(__instance.extInfoListContainer.childCount > 1);
                }
                else
                    __instance.panelExtended.SetActive(false);
            }
            __instance.textRMBHint.text = rmbHintText;

            return false;
        }

        // This was doing weird things with PartStatsUpgradeModule *again*.
        // Fix it to properly deal with it, and to print upgrade text if desired
        // And to not delink the PartVariant moduleInfo.
        static bool PartListTooltip_CreateExtendedInfo_Prefix(PartListTooltip __instance, ref bool showPartCost)
        {
            AvailablePart.ModuleInfo variantInfo = null;
            if (showPartCost)
            {
                PartListTooltipWidget widget = __instance.GetNewTooltipWidget(__instance.extInfoRscWidgePrefab);
                widget.Setup(PartListTooltip.cacheAutoLOC_7003267, __instance.textCost.text);
                widget.transform.SetParent(__instance.extInfoListContainer.transform, false);
            }
            if (__instance.upgradeState == PartModule.PartUpgradeState.AVAILABLE)
            {
                // Handle PartStatsUpgradeModule differently
                var statsUpgradeModule = __instance.partRef.FindModuleImplementing<PartStatsUpgradeModule>();
                if (statsUpgradeModule != null)
                {
                    foreach (string upgradeName in statsUpgradeModule.upgradesApplied)
                    {
                        PartUpgradeHandler.Upgrade upgrade = PartUpgradeManager.Handler.GetUpgrade(upgradeName);
                        if (upgrade == null)
                            continue;

                        PartListTooltipWidget widget = __instance.GetNewTooltipWidget(__instance.extInfoModuleWidgetPrefab);
                        widget.Setup(statsUpgradeModule.GetModuleDisplayName(), upgrade.description);
                        widget.transform.SetParent(__instance.extInfoListContainer.transform, false);
                    }
                }

                foreach (PartModule partModule in __instance.partRef.modules)
                {
                    if (partModule is PartStatsUpgradeModule)
                        continue;

                    if (partModule is ModulePartVariants mpv)
                    {
                        variantInfo = new AvailablePart.ModuleInfo();
                        variantInfo.moduleName = mpv.GetModuleTitle();
                        variantInfo.moduleDisplayName = mpv.GetModuleDisplayName();
                        variantInfo.info = mpv.GetInfo().Trim();
                        if (mpv.showUpgradesInModuleInfo)
                            variantInfo.info += "\n" + mpv.PrintUpgrades();

                        continue;
                    }

                    string info;
                    try
                    {
                        info = partModule.GetInfo();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[KSPCommunityFixes] Exception during CreateExtendedInfo for partmodule {partModule.name}: {e}");
                        info = "This module threw during GetInfo";
                    }
                    if (string.IsNullOrEmpty(info))
                    {
                        if (partModule.upgradesApplied == null || partModule.upgradesApplied.Count == 0)
                            continue;

                        // We need to show upgrade info even without module info
                        info = partModule.GetModuleDisplayName();
                    }

                    foreach (string upgradeName in partModule.upgradesApplied)
                    {
                        PartUpgradeHandler.Upgrade upgrade = PartUpgradeManager.Handler.GetUpgrade(upgradeName);
                        if (upgrade == null)
                            continue;
                        info += $"\n\n{upgrade.title}\n{upgrade.description}";
                    }
                    if (partModule.showUpgradesInModuleInfo)
                    {
                        info += "\n" + partModule.PrintUpgrades();
                    }
                    PartListTooltipWidget widget = __instance.GetNewTooltipWidget(__instance.extInfoModuleWidgetPrefab);
                    widget.Setup(partModule.GetModuleDisplayName(), info);
                    widget.transform.SetParent(__instance.extInfoListContainer.transform, false);
                }
            }
            else
            {
                foreach (AvailablePart.ModuleInfo moduleInfo in __instance.partInfo.moduleInfos)
                {
                    if (moduleInfo.moduleName != ModulePartVariants.GetTitle())
                    {
                        PartListTooltipWidget widget = __instance.GetNewTooltipWidget(__instance.extInfoModuleWidgetPrefab);
                        widget.Setup(moduleInfo.moduleDisplayName, moduleInfo.info);
                        widget.transform.SetParent(__instance.extInfoListContainer.transform, false);
                    }
                    else
                    {
                        variantInfo = moduleInfo;
                    }
                }
            }
            if (__instance.extInfoListContainer.childCount > 2 && __instance.partInfo.resourceInfos.Count > 0)
            {
                __instance.extInfoListSpacer.gameObject.SetActive(true);
                __instance.extInfoListSpacer.SetSiblingIndex(__instance.extInfoListContainer.childCount - 1);
            }
            else
                __instance.extInfoListSpacer.gameObject.SetActive(false);

            foreach (var resourceInfo in __instance.partInfo.resourceInfos)
            {
                PartListTooltipWidget resourceWidget = __instance.GetNewTooltipWidget(__instance.extInfoRscWidgePrefab);
                resourceWidget.Setup(resourceInfo.displayName.LocalizeRemoveGender(), resourceInfo.info);
                resourceWidget.transform.SetParent(__instance.extInfoListContainer.transform, false);
            }

            // PartVariant support
            if (__instance.extInfoListContainer.childCount > 2 && variantInfo != null)
            {
                __instance.extInfoListSpacerVariants.gameObject.SetActive(true);
                __instance.extInfoListSpacerVariants.SetSiblingIndex(__instance.extInfoListContainer.childCount - 1);
            }
            else
                __instance.extInfoListSpacerVariants.gameObject.SetActive(false);

            if (variantInfo != null)
            {
                PartListTooltipWidget variantWidget = __instance.GetNewTooltipWidget(__instance.extInfoVariantsWidgePrefab);
                variantWidget.Setup(variantInfo.moduleDisplayName, variantInfo.info);
                variantWidget.transform.SetParent(__instance.extInfoListContainer.transform, false);
            }

            return false;
        }

        // Fix this inexplicably applying upgrades
        // and fix it breaking upgrades when PartStatsModule is present
        static bool PartModule_UpgradesAvailable_Prefix(ref Part part, ref ConfigNode node, ref PartModule.PartUpgradeState __result)
        {
            __result = PartModule.PartUpgradeState.NONE;
            foreach (PartModule partModule in part.Modules)
            {
                // Stock refinds the node for PartStatsUpgradeModules and passses that here, breaking upgrades
                // when that module is present on the part (because FindUpgrades with a node will try to
                // reload upgrades from that node, and if it doesn't find any, it clears the stored upgrades.
                // No idea why they added that and I think it's 100% wrong to do so. So we don't pass node.
                // In addition, this call APPLIES UPRADES (what the heck) in stock code by passing
                // ApplyUpgradesEditorAuto instead of false. So we're changing it to false, as it was before
                // it got messed up.
                if (partModule.FindUpgrades(false, null))
                {
                    __result = PartModule.PartUpgradeState.AVAILABLE;
                    return false;
                }
                if (partModule.upgrades.Count > 0)
                    __result = PartModule.PartUpgradeState.LOCKED;
            }

            return false;
        }

        // This requires a subtle change: we need to find *enabled* upgrades, not just unlocked ones,
        // when we are searching for existing upgrades and fillApplied is false.
        static IEnumerable<CodeInstruction> PartModule_FindUpgrades_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            for (int i = 0; i < code.Count - 2; i++)
            {
                // This occurs twice, so we need to break after the first one.
                if (code[i].opcode == OpCodes.Ldloc_S && code[i + 1].Calls(AccessTools.Method(typeof(PartUpgradeHandler), nameof(PartUpgradeHandler.IsUnlocked))))
                {
                    code[i + 1].operand = AccessTools.Method(typeof(PartUpgradeHandler), nameof(PartUpgradeHandler.IsEnabled));
                    break;
                }
            }
            return code;
        }


        // Fix this doing the same PartStatsModule weirdness
        // (and incidentally fix it not calling InitializeModules)
        static bool EditorLogic_OnPodSpawn_Prefix(EditorLogic __instance, ref AvailablePart pod)
        {
            __instance.rootPart = UnityEngine.Object.Instantiate(pod.partPrefab);
            __instance.rootPart.gameObject.SetActive(true);
            __instance.rootPart.name = pod.name;
            __instance.rootPart.partInfo = pod;
            __instance.rootPart.persistentId = FlightGlobals.CheckPartpersistentId(__instance.rootPart.persistentId, __instance.rootPart, false, true);
            if (__instance.rootPart.variants != null && pod.variant != null && pod.variant.Name != null)
                __instance.rootPart.variants.SetVariant(pod.variant.Name);
            ModuleInventoryPart moduleInventoryPart = __instance.rootPart.FindModuleImplementing<ModuleInventoryPart>();
            if (moduleInventoryPart != null)
                moduleInventoryPart.SetInventoryDefaults();

            // Stock code tries to reload all upgrades from the PartStatsUpgradeModule partConfig node (???)
            // which it shouldn't be doing. So we cut all that out (we'll do it whenever any part spawns).
            // It *also* fails to call InitializeModules, which we fix here.
            __instance.rootPart.InitializeModules();

            GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartCreated, __instance.rootPart);
            __instance.fsm.RunEvent(__instance.on_podSelect);

            return false;
        }

        // We catch the case where we're spawning a root part, or spawning a new part
        // by storing the selectedPart prior to execution (it remains unchanged when spawning a root part)
        static void EditorLogic_SpawnPart_Prefix(EditorLogic __instance, ref AvailablePart partInfo, out Part __state)
        {
            __state = __instance.selectedPart;
        }

        // We apply any relevant upgrades here, applying them either to the root part
        // (if this was a rootpart spawn) or to the fresh selected part.
        static void EditorLogic_SpawnPart_Postfix(EditorLogic __instance, ref AvailablePart partInfo, Part __state)
        {
            Part part = __state == __instance.selectedPart ? __instance.rootPart : __instance.selectedPart;
            foreach (var pm in part.Modules)
                pm.FindUpgrades(PartModule.ApplyUpgradesEditorAuto, null);
        }

        // Fix upgrade costs not being taken into account
        static void Part_GetModuleCosts_Postfix(Part __instance, ref float __result)
        {
            foreach (var pm in __instance.Modules)
            {
                float upCost = 0f;
                foreach (string upgradeName in pm.upgradesApplied)
                {
                    PartUpgradeHandler.Upgrade upgrade = PartUpgradeManager.Handler.GetUpgrade(upgradeName);
                    if (upgrade == null)
                        continue;

                    if (upgrade.cost != 0f)
                    {
                        if (upgrade.cumulativeCost)
                            upCost += upgrade.cost;
                        else
                            upCost = upgrade.cost;
                    }
                }
                __result += upCost;
            }
        }

        // Make this support localization
        static bool GetUsedBy_Prefix(PartUpgradeHandler.Upgrade __instance, ref List<string[]> __result)
        {
            __result = new List<string[]>();
            foreach (var kvp in __instance.instances)
            {
                string usedBy = string.Empty;
                foreach (var partModule in kvp.Value)
                {
                    string pmName = partModule.GetModuleDisplayName();
                    if (pmName == "")
                        pmName = partModule is IModuleInfo mI ? mI.GetModuleTitle() : KSPUtil.PrintModuleName(partModule.moduleName);

                    string descs = string.Empty;
                    bool found = false;
                    foreach (ConfigNode configNode in partModule.upgrades)
                    {
                        string upName = configNode.GetValue("name__");
                        if (string.IsNullOrEmpty(upName) || upName != __instance.name)
                            continue;

                        string upDesc = configNode.GetValue("description__");
                        if (string.IsNullOrEmpty(upDesc))
                            continue;

                        if (found)
                            descs += "  ";
                        descs += KSP.Localization.Localizer.Instance.ReplaceSingleTagIfFound(upDesc);
                        found = true;
                    }
                    if (descs != string.Empty)
                    {
                        if (usedBy != string.Empty)
                            usedBy += "\n";
                        usedBy += pmName + " " + descs;
                    }
                }
                __result.Add(new string[2] {
                    kvp.Key.partInfo.title,
                    usedBy
                });
            }

            return false;
        }
    }
}
