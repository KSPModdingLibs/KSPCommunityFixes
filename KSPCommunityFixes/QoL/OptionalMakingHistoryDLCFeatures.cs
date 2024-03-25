using System;
using Expansions;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace KSPCommunityFixes.QoL
{
    internal class OptionalMakingHistoryDLCFeatures : BasePatch
    {
        private const string MH_EXPANSION_FILE = "makinghistory.kspexpansion";
        private const string SETTINGS_TOGGLE_VALUE_NAME = "OptionalMakingHistoryDLCFeaturesAlwaysDisable";
        internal static string LOC_MHDLC = "Making History features";
        internal static string LOC_SettingsTooltip = "Disable the Making History DLC mission editor and additional launch sites\nThe Making History parts will still be available\nWill reduce memory usage and increase loading speed\nChanges will take effect after restarting KSP";
        internal static bool isMHEnabled = true;
        internal static bool isMHDisabledFromConfig = false;

        protected override Version VersionMin => new Version(1, 12, 3);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            if (KSPCommunityFixes.SettingsNode.TryGetValue(SETTINGS_TOGGLE_VALUE_NAME, ref isMHDisabledFromConfig) && isMHDisabledFromConfig)
                isMHEnabled = false;

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ExpansionsLoader), nameof(ExpansionsLoader.InitializeExpansion)),
                this));
        }

        protected override void OnLoadData(ConfigNode node)
        {
            if (!isMHDisabledFromConfig)
                node.TryGetValue(nameof(isMHEnabled), ref isMHEnabled);
        }

        protected override bool CanApplyPatch(out string reason)
        {
            if (Directory.Exists(KSPExpansionsUtils.ExpansionsGameDataPath))
            {
                foreach (string fileName in Directory.GetFiles(KSPExpansionsUtils.ExpansionsGameDataPath, "*" + ExpansionsLoader.expansionsMasterExtension, SearchOption.AllDirectories))
                {
                    if (fileName.EndsWith(MH_EXPANSION_FILE))
                    {
                        reason = null;
                        return true;
                    }
                }
            }

            reason = "Making History DLC not installed";
            return false;
        }

        static bool ExpansionsLoader_InitializeExpansion_Prefix(string expansionFile, ref IEnumerator __result)
        {
            if (!isMHEnabled && expansionFile.EndsWith(MH_EXPANSION_FILE))
            {
                __result = EmptyEnumerator();
                return false;
            }

            return true;
        }

        static IEnumerator EmptyEnumerator()
        {
            yield break;
        }
    }
}
