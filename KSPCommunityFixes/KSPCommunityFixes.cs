using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSPCommunityFixes.BugFixes;
using KSPCommunityFixes.UI;
using Smooth.Collections;
using UnityEngine;

namespace KSPCommunityFixes
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPCommunityFixes : MonoBehaviour
    {
        public static Version KspVersion { get; private set; }
        public static Harmony Harmony { get; private set; }

        public static HashSet<string> enabledPatches = new HashSet<string>();

        void Start()
        {
            KspVersion = new Version(Versioning.version_major, Versioning.version_minor, Versioning.Revision);
            Harmony = new Harmony("KSPCommunityFixes");
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
                if (!Boolean.TryParse(value.value, out bool enabled) || enabled)
                {
                    enabledPatches.Add(value.name);
                }
            }

            // QoL :
            BasePatch.Patch<PAWCollapsedInventories>();

            // Bugfixes :
            BasePatch.Patch<RefundingOnRecovery>();

            Destroy(this);
        }
    }
}
