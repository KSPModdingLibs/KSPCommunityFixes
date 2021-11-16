using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using KSPCommunityFixes.BugFixes;
using KSPCommunityFixes.UI;
using UnityEngine;

namespace KSPCommunityFixes
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPCommunityFixes : MonoBehaviour
    {
        public static Version KspVersion { get; private set; }
        public static Harmony Harmony { get; private set; }
        public static string ModPath { get; private set; }

        public static HashSet<string> enabledPatches = new HashSet<string>();
        public static Dictionary<Type, BasePatch> patchInstances = new Dictionary<Type, BasePatch>();

        void Start()
        {
            KspVersion = new Version(Versioning.version_major, Versioning.version_minor, Versioning.Revision);
            Harmony = new Harmony("KSPCommunityFixes");
            ModPath = Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
#if DEBUG
            Harmony.DEBUG = true;
#endif
        }

        public void ModuleManagerPostLoad()
        {
            var featuresNodes = GameDatabase.Instance.GetConfigs("KSP_COMMUNITY_FIXES");

            ConfigNode cfg;
            if (featuresNodes.Length == 1)
                cfg = featuresNodes[0].config;
            else
                cfg = new ConfigNode();

            foreach (ConfigNode.Value value in cfg.values)
            {
                if (!Boolean.TryParse(value.value, out bool patchEnabled) || patchEnabled)
                {
                    enabledPatches.Add(value.name);
                }
            }

            // Bugfixes :
            BasePatch.Patch<RefundingOnRecovery>();
            BasePatch.Patch<DockingPortDrift>();
            BasePatch.Patch<ModuleIndexingMismatch>();
            BasePatch.Patch<StockAlarmCustomFormatterDate>();
            BasePatch.Patch<PAWGroupMemory>();
            BasePatch.Patch<KerbalInventoryPersistence>();
            BasePatch.Patch<PAWItemsOrder>();
            
            // QoL :
            BasePatch.Patch<AltimeterHorizontalPosition>();
            BasePatch.Patch<PAWCollapsedInventories>();
            BasePatch.Patch<PAWStockGroups>();
            BasePatch.Patch<TweakableWheelsAutostrut>();

            Destroy(this);
        }
    }
}
