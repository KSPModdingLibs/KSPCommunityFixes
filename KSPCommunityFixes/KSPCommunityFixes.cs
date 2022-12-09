using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KSPCommunityFixes
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPCommunityFixes : MonoBehaviour
    {
        public const string CONFIGNODE_NAME = "KSP_COMMUNITY_FIXES";

        public static string LOC_KSPCF_Title = "KSP Community Fixes";


        public static Harmony Harmony { get; private set; }

        public static HashSet<string> enabledPatches = new HashSet<string>();
        public static Dictionary<Type, BasePatch> patchInstances = new Dictionary<Type, BasePatch>();

        public static ConfigNode SettingsNode { get; private set; }

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

        private static Version kspVersion;
        public static Version KspVersion
        {
            get
            {
                if (kspVersion == null)
                    kspVersion = new Version(Versioning.version_major, Versioning.version_minor, Versioning.Revision);

                return kspVersion;
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
            if (Instance.IsNotNullOrDestroyed() && Instance != this)
            {
                Destroy(Instance);
                Instance = null;
            }
            
            if (Instance.IsNullOrDestroyed())
            {
                Instance = this;
                DontDestroyOnLoad(this);
            }

            Harmony = new Harmony("KSPCommunityFixes");

#if DEBUG
            Harmony.DEBUG = true;
#endif
            LocalizationUtils.GenerateLocTemplateIfRequested();
            LocalizationUtils.ParseLocalization();
        }

        public void ModuleManagerPostLoad()
        {
            if (Instance.IsNullOrDestroyed() || !Instance.RefEquals(this))
                return;

            UrlDir.UrlConfig[] featuresNodes = GameDatabase.Instance.GetConfigs(CONFIGNODE_NAME);

            if (featuresNodes != null && featuresNodes.Length == 1)
                SettingsNode = featuresNodes[0].config;
            else
                SettingsNode = new ConfigNode();

            foreach (ConfigNode.Value value in SettingsNode.values)
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
