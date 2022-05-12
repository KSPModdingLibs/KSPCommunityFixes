using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using KSP.Localization;
using UnityEngine;

namespace KSPCommunityFixes
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPCommunityFixes : MonoBehaviour
    {
        public static string LOC_KSPCF_Title = "KSP Community Fixes";

        public static Version KspVersion { get; private set; }
        public static Harmony Harmony { get; private set; }

        public static HashSet<string> enabledPatches = new HashSet<string>();
        public static Dictionary<Type, BasePatch> patchInstances = new Dictionary<Type, BasePatch>();

        public static KSPCommunityFixes Instance { get; private set; }

        private static string modPath;
        public static string ModPath
        {
            get
            {
                if (modPath == null)
                    modPath = Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

                return modPath;
            }
        }

        public static T GetPatchInstance<T>() where T : BasePatch
        {
            if (!patchInstances.TryGetValue(typeof(T), out BasePatch instance))
                return null;

            return (T)instance;
        }

        void Start()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(Instance);
                Instance = null;
            }
            
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(this);
            }

            KspVersion = new Version(Versioning.version_major, Versioning.version_minor, Versioning.Revision);
            Harmony = new Harmony("KSPCommunityFixes");

            LocalizationUtils.ParseLocalization();
#if DEBUG
            Harmony.DEBUG = true;
#endif
        }

        public void ModuleManagerPostLoad()
        {
            if (Instance == null || Instance != this)
                return;

            var featuresNodes = GameDatabase.Instance.GetConfigs("KSP_COMMUNITY_FIXES");

            ConfigNode cfg;
            if (featuresNodes.Length == 1)
                cfg = featuresNodes[0].config;
            else
                cfg = new ConfigNode();

            foreach (ConfigNode.Value value in cfg.values)
            {
                if (!bool.TryParse(value.value, out bool patchEnabled) || patchEnabled)
                {
                    enabledPatches.Add(value.name);
                }
            }

            Type basePatchType = typeof(BasePatch);
            List<Type> patchesTypes = new List<Type>();
            foreach (Type type in Assembly.GetAssembly(basePatchType).GetTypes())
            {
                if (!type.IsAbstract && type.IsSubclassOf(basePatchType))
                {
                    patchesTypes.Add(type);
                    
                }
            }

            patchesTypes.Sort((x, y) => (x.GetCustomAttribute<PatchPriority>()?.Order ?? 0).CompareTo(y.GetCustomAttribute<PatchPriority>()?.Order ?? 0));

            foreach (Type patchesType in patchesTypes)
            {
                BasePatch.Patch(patchesType);
            }
        }
    }
}
