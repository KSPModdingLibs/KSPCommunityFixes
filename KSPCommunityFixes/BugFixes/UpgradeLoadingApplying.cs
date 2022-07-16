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
    public class UpgradeLoadingApplying : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PartListTooltip), "SetupUpgradeInfo"),
                this));
        }

        static bool PartListTooltip_SetupUpgradeInfo_Prefix(PartListTooltip __instance, ref AvailablePart availablePart)
        {
            foreach (ConfigNode n in availablePart.partConfig.nodes)
            {
                if (n.GetValue("name") == "PartStatsUpgradeModule")
                {
                    __instance.upgradeNode = n;
                    break;
                }
            }

            __instance.upgradeState = PartModule.UpgradesAvailable(__instance.partRef, __instance.upgradeNode);
            if (__instance.upgradeState != PartModule.PartUpgradeState.AVAILABLE)
                return false;

            foreach(PartModule pm in __instance.partRef.Modules)
            {
                if (pm.HasUpgrades())
                    pm.ApplyUpgrades(PartModule.StartState.Editor);

                if (pm.moduleName != "PartStatsUpgradeModule")
                    continue;

                pm.OnLoad(__instance.upgradeNode);
                foreach (var up in pm.upgradesApplied)
                {
                    PartUpgradeHandler.Upgrade upgrade = PartUpgradeManager.Handler.GetUpgrade(up);
                    if (upgrade == null)
                        continue;

                    if (upgrade.cost != 0f)
                    {
                        if (upgrade.cumulativeCost)
                            __instance.upgradeCost += upgrade.cost;
                        else
                            __instance.upgradeCost = upgrade.cost;
                    }
                    PartListTooltipWidget newTooltipWidget = __instance.GetNewTooltipWidget(__instance.extInfoModuleWidgetPrefab);
                    newTooltipWidget.Setup(pm.GetModuleDisplayName(), upgrade.description);
                    newTooltipWidget.transform.SetParent(__instance.extInfoListContainer.transform, false);
                }
            }
            return false;
        }
    }
}
