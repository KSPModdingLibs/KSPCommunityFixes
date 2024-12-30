using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using static Highlighting.Highlighter.RendererCache;

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

        public static long FixedUpdateCount { get; private set; }

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

        public static bool cleanedDll
        {
            get
            {
                String dllPath = "";
                if (Application.platform == RuntimePlatform.WindowsPlayer)
                {
                    dllPath = KSPUtil.ApplicationRootPath + "KSP_x64_Data/Managed/Assembly-CSharp.dll";
                }
                else if (Application.platform == RuntimePlatform.OSXPlayer)
                {
                    dllPath = KSPUtil.ApplicationRootPath + "KSP.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll";
                }
                else if (Application.platform == RuntimePlatform.LinuxPlayer)
                {
                    dllPath = KSPUtil.ApplicationRootPath + "KSP_Data/Managed/Assembly-CSharp.dll";
                }
                if (File.Exists(dllPath))
                {
                    Byte[] data = File.ReadAllBytes(dllPath);
                    if ((data.Length < 10485760) && (KSPCommunityFixes.KspVersion >= new Version(1, 12, 0)))
                    {
                        return true; //certainly a home-cleaned dll, no official 1.12.x build of Assembly-CSharp is less than 10MBs.
                    }
                }
                return false;
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

            // Insert KSPCF as the first entry in the explicit callback list.
            // This guarantees that KSPCF will run before all other post load callbacks.
            // Note that this is cumbersome to access via publicizer because it references
            // assemblies via file name, and ModuleManager's dll is versioned.
            AccessTools
                .StaticFieldRefAccess<List<ModuleManager.ModuleManagerPostPatchCallback>>(typeof(ModuleManager.PostPatchLoader), "postPatchCallbacks")
                .Insert(0, MMPostLoadCallback);
        }

        public void MMPostLoadCallback()
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

        void FixedUpdate()
        {
            FixedUpdateCount++;
        }
    }
}
