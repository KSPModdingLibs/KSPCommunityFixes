// #define DEBUG_TEXTURE_CACHE

using DDSHeaders;
using Expansions;
using HarmonyLib;
using KSP.Localization;
using KSPAssets;
using KSPAssets.Loaders;
using KSPCommunityFixes.Library.Buffers;
using KSPCommunityFixes.Library.Collections;
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using KSPCommunityFixes.Library;
using TMPro;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Networking;
using UnityEngine.UI;
using static GameDatabase;
using static UrlDir;
using Debug = UnityEngine.Debug;
using UnityEngine.Profiling;
using System.Threading.Tasks;
using KSP.UI;
using System.Security.Cryptography;
using UnityEngine.Rendering;

namespace KSPCommunityFixes.Performance
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    internal class KSPCFFastLoaderReport : MonoBehaviour
    {
        internal static float initialConfigLoadTime;
        internal static Stopwatch wSecondConfigLoad = new Stopwatch();
        internal static Stopwatch wConfigTranslate = new Stopwatch();
        internal static Stopwatch wAssetsLoading = new Stopwatch();
        internal static Stopwatch wAudioLoading = new Stopwatch();
        internal static Stopwatch wTextureLoading = new Stopwatch();
        internal static Stopwatch wModelLoading = new Stopwatch();
        internal static Stopwatch wAssetBundleLoading = new Stopwatch();
        internal static Stopwatch wGamedatabaseLoading = new Stopwatch();
        internal static Stopwatch wBuiltInPartsCopy = new Stopwatch();
        internal static Stopwatch wPartConfigExtraction = new Stopwatch();
        internal static Stopwatch wPartCompilationLoading = new Stopwatch();
        internal static Stopwatch wInternalCompilationLoading = new Stopwatch();
        internal static Stopwatch wExpansionLoading = new Stopwatch();
        internal static Stopwatch wPSystemSetup = new Stopwatch();

        internal static long audioBytesLoaded;
        internal static int texturesLoaded;
        internal static long texturesBytesLoaded;
        internal static int modelsLoaded;
        internal static long modelsBytesLoaded;

        void Start()
        {
            float totalLoadingTime = Time.realtimeSinceStartup;
            int totalPartsLoaded = 0;
            int totalModulesLoaded = 0;
            foreach (AvailablePart availablePart in PartLoader.Instance.loadedParts)
            {
                if (availablePart.partPrefab.IsNotNullOrDestroyed())
                {
                    totalPartsLoaded++;
                    totalModulesLoaded += availablePart.partPrefab.modules.Count;
                }
            }

            int totalInternalsLoaded = PartLoader.Instance.internalParts.Count;
            int totalInternalPropsLoaded = PartLoader.Instance.internalProps.Count;

            string log =
                $"[KSPCF:FastLoader] {SystemInfo.processorType} | {SystemInfo.systemMemorySize} MB | {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)\n" +
                $"Total loading time to main menu : {totalLoadingTime:F3}s\n" +
                $"- Configs and assemblies loaded in {initialConfigLoadTime:F3}s\n" +
                $"- Configs reload done in {wSecondConfigLoad.Elapsed.TotalSeconds:F3}s\n" +
                $"- Configs translated in {wConfigTranslate.Elapsed.TotalSeconds:F3}s\n" +
                $"- {KSPCFFastLoader.loadedAssetCount} assets loaded in {wAssetsLoading.Elapsed.TotalSeconds:F3}s :\n" +
                $"  - {KSPCFFastLoader.audioFilesLoaded} audio assets ({StaticHelpers.HumanReadableBytes(audioBytesLoaded)}) in {wAudioLoading.Elapsed.TotalSeconds:F3}s, {StaticHelpers.HumanReadableBytes((long)(audioBytesLoaded / wAudioLoading.Elapsed.TotalSeconds))}/s\n" +
                $"  - {texturesLoaded} texture assets ({StaticHelpers.HumanReadableBytes(texturesBytesLoaded)}) in {wTextureLoading.Elapsed.TotalSeconds:F3}s, {StaticHelpers.HumanReadableBytes((long)(texturesBytesLoaded / wTextureLoading.Elapsed.TotalSeconds))}/s\n" +
                $"  - {modelsLoaded} model assets ({StaticHelpers.HumanReadableBytes(modelsBytesLoaded)}) in {wModelLoading.Elapsed.TotalSeconds:F3}s, {StaticHelpers.HumanReadableBytes((long)(modelsBytesLoaded / wModelLoading.Elapsed.TotalSeconds))}/s\n" +
                $"- Asset bundles loaded in {wAssetBundleLoading.Elapsed.TotalSeconds:F3}s\n" +
                $"- GameDatabase (configs, resources, traits, upgrades...) loaded in {wGamedatabaseLoading.Elapsed.TotalSeconds:F3}s\n" +
                $"- Built-in parts copied in {wBuiltInPartsCopy.Elapsed.TotalSeconds:F3}s\n" +
                $"- Part and internal configs extracted in {wPartConfigExtraction.Elapsed.TotalSeconds:F3}s\n" +
                $"- {totalPartsLoaded} parts and {totalModulesLoaded} modules compiled in {wPartCompilationLoading.Elapsed.TotalSeconds:F3}s\n" +
                $"  - {totalModulesLoaded / (float)totalPartsLoaded:F1} modules/part, {wPartCompilationLoading.Elapsed.TotalMilliseconds / totalPartsLoaded:F3} ms/part, {wPartCompilationLoading.Elapsed.TotalMilliseconds / totalModulesLoaded:F3} ms/module\n" +
                $"  - PartIcon compilation : {PartParsingPerf.iconCompilationWatch.Elapsed.TotalSeconds:F3}s\n" +
                $"- {totalInternalsLoaded} internal spaces and {totalInternalPropsLoaded} props compiled in {wInternalCompilationLoading.Elapsed.TotalSeconds:F3}s\n";

            if (ExpansionsLoader.expansionsInfo.Count > 0)
                log += $"- {ExpansionsLoader.expansionsInfo.Count} DLC ({ExpansionsLoader.expansionsInfo.Values.Join(info => info.DisplayName)}) loaded in {wExpansionLoading.Elapsed.TotalSeconds:F3}s\n";

            log +=
                $"- Planetary system loaded in {wPSystemSetup.Elapsed.TotalSeconds:F3}s";

            Debug.Log(log);
            Debug.Log($"Texture queries : {GameDatabasePerf.txcallCount}, slow path : {GameDatabasePerf.txMissCount} ({GameDatabasePerf.txMissCount / (float)GameDatabasePerf.txcallCount:P2})");
            Destroy(gameObject);
        }
    }

    [KSPAddon(KSPAddon.Startup.PSystemSpawn, true)]
    internal class KSPCFFastLoaderPSystemSetup : MonoBehaviour
    {
        internal static void PSystemManager_Awake_Prefix()
        {
            KSPCFFastLoaderReport.wPSystemSetup.Start();
        }

        void OnDestroy()
        {
            KSPCFFastLoaderReport.wPSystemSetup.Stop();
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class KSPCFFastLoader : MonoBehaviour
    {
        public static string LOC_PopupL1 =
            "KSPCommunityFixes can cache converted PNG textures on disk to speed up loading time.";
        public static string LOC_F_PopupL2 =
            "In your current install, this should reduce future loading time by about <b><color=#FF8000><<1>> seconds</color></b>.";
        public static string LOC_F_PopupL3 =
            "However, this will use about <b><color=#FF8000><<1>> MB</color></b> of additional disk space, and potentially much more if you install additional mods.";
        public static string LOC_PopupL4 =
            "You can change this setting later in the in-game settings menu";
        public static string LOC_PopupL5 =
            "Do you want to enable this optimization ?";

        // approximate max FPS during asset loading and part parsing
        private const int maxFPS = 30;
        private const float minFrameTime = 1f / maxFPS;
        private const double minFrameTimeD = 1.0 / maxFPS;

        // max size of in-memory disk reads, can and will be exceeded
        private const int maxBufferSize = 1024 * 1024 * 50; // 50MB
        // min amount of files to try to keep in memory, regardless of maxBufferSize
        private const int minFileRead = 10;

        // Max number of new texture load coroutines that will be spawned each frame.
        // This should roughly limit the max frame time spent on loading textures.
        private const int MaxTextureSpawnsPerFrame = 512;

        private static Harmony persistentHarmony;
        private static string PersistentHarmonyID => typeof(KSPCFFastLoader).FullName;

        private static Harmony assetAndPartLoaderHarmony;
        private static string AssetAndPartLoaderHarmonyID => typeof(KSPCFFastLoader).FullName + "AssetAndPartLoader";

        private static Harmony expansionsLoaderHarmony;
        private static string ExpansionsLoaderHarmonyID => typeof(KSPCFFastLoader).FullName + "ExpansionsLoader";

        public static KSPCFFastLoader loader;

        public static bool IsPatchEnabled { get; private set; }
        // Vestigial: kept so the popup can persist its choice across launches once it is repurposed.
        private static bool textureCacheEnabled;

        private static string ModPath => Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
        private static string ConfigPath => Path.Combine(ModPath, "PluginData", "PNGTextureCache.cfg");

        private bool userOptInChoiceDone;
        private string configPath;

        internal static Dictionary<string, GameObject> modelsByUrl;
        internal static Dictionary<string, GameObject> modelsByDirectoryUrl;
        internal static Dictionary<GameObject, UrlFile> urlFilesByModel;
        internal static Dictionary<string, TextureInfo> texturesByUrl;

        private void Awake()
        {
            if (KSPCommunityFixes.KspVersion < new Version(1, 12, 3))
            {
                Debug.Log("[KSPCF] FastLoader patch not applied, requires KSP 1.12.3 or latter");
                IsPatchEnabled = false;
                return;
            }

            KSPCFFastLoaderReport.initialConfigLoadTime = Time.realtimeSinceStartup;

            Debug.Log("[KSPCF] Injecting FastLoader...");
            loader = this;
            IsPatchEnabled = true;

            // Patch the various GameDatabase.GetModel/GetTexture methods to use the FastLoader dictionaries
            BasePatch.Patch(typeof(GameDatabasePerf));

            persistentHarmony = new Harmony(PersistentHarmonyID);

            MethodInfo m_PSystemManager_Awake = AccessTools.Method(typeof(PSystemManager), nameof(PSystemManager.Awake));
            MethodInfo p_PSystemManager_Awake = AccessTools.Method(typeof(KSPCFFastLoaderPSystemSetup), nameof(KSPCFFastLoaderPSystemSetup.PSystemManager_Awake_Prefix));
            persistentHarmony.Patch(m_PSystemManager_Awake, new HarmonyMethod(p_PSystemManager_Awake));

            assetAndPartLoaderHarmony = new Harmony(AssetAndPartLoaderHarmonyID);

            MethodInfo m_GameDatabase_SetupMainLoaders = AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.SetupMainLoaders));
            MethodInfo t_GameDatabase_SetupMainLoaders = AccessTools.Method(typeof(KSPCFFastLoader), nameof(GameDatabase_SetupMainLoaders_Prefix));
            assetAndPartLoaderHarmony.Patch(m_GameDatabase_SetupMainLoaders, new HarmonyMethod(t_GameDatabase_SetupMainLoaders));

            MethodInfo m_GameDatabase_LoadAssetBundleObjects_MoveNext = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.LoadAssetBundleObjects)));
            MethodInfo pr_GameDatabase_LoadAssetBundleObjects_MoveNext = AccessTools.Method(typeof(KSPCFFastLoader), nameof(GameDatabase_LoadAssetBundleObjects_MoveNext_Prefix));
            MethodInfo po_GameDatabase_LoadAssetBundleObjects_MoveNext = AccessTools.Method(typeof(KSPCFFastLoader), nameof(GameDatabase_LoadAssetBundleObjects_MoveNext_Postfix));
            MethodInfo t_GameDatabase_LoadAssetBundleObjects_MoveNext = AccessTools.Method(typeof(KSPCFFastLoader), nameof(GameDatabase_LoadAssetBundleObjects_MoveNext_Transpiler));
            assetAndPartLoaderHarmony.Patch(
                m_GameDatabase_LoadAssetBundleObjects_MoveNext,
                new HarmonyMethod(pr_GameDatabase_LoadAssetBundleObjects_MoveNext),
                new HarmonyMethod(po_GameDatabase_LoadAssetBundleObjects_MoveNext),
                new HarmonyMethod(t_GameDatabase_LoadAssetBundleObjects_MoveNext)
            );

            MethodInfo m_PartLoader_StartLoad = AccessTools.Method(typeof(PartLoader), nameof(PartLoader.StartLoad));
            MethodInfo t_PartLoader_StartLoad = AccessTools.Method(typeof(KSPCFFastLoader), nameof(PartLoader_StartLoad_Transpiler));
            assetAndPartLoaderHarmony.Patch(m_PartLoader_StartLoad, null, null, new HarmonyMethod(t_PartLoader_StartLoad));

            PatchStartCoroutineInCoroutine(AccessTools.Method(typeof(PartLoader), nameof(PartLoader.CompileParts)));
            PatchStartCoroutineInCoroutine(AccessTools.Method(typeof(DragCubeSystem), nameof(DragCubeSystem.SetupDragCubeCoroutine), new[] { typeof(Part) }));
            PatchStartCoroutineInCoroutine(AccessTools.Method(typeof(DragCubeSystem), nameof(DragCubeSystem.RenderDragCubesCoroutine)));

            // Fix for issue #114 : Drag cubes are incorrectly calculated with KSPCF 1.24.1 
            MethodInfo m_DragCubeSystem_RenderDragCubes_MoveNext = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(DragCubeSystem), nameof(DragCubeSystem.RenderDragCubes)));
            MethodInfo m_DragCubeSystem_RenderDragCubes_MoveNext_Transpiler = AccessTools.Method(typeof(KSPCFFastLoader), nameof(DragCubeSystem_RenderDragCubes_MoveNext_Transpiler));
            assetAndPartLoaderHarmony.Patch(m_DragCubeSystem_RenderDragCubes_MoveNext, null, null, new HarmonyMethod(m_DragCubeSystem_RenderDragCubes_MoveNext_Transpiler));

            expansionsLoaderHarmony = new Harmony(ExpansionsLoaderHarmonyID);
            MethodInfo m_ExpansionsLoader_StartLoad = AccessTools.Method(typeof(ExpansionsLoader), nameof(PartLoader.StartLoad));
            MethodInfo p_ExpansionsLoader_StartLoad = AccessTools.Method(typeof(KSPCFFastLoader), nameof(ExpansionsLoader_StartLoad_Prefix));
            expansionsLoaderHarmony.Patch(m_ExpansionsLoader_StartLoad, new HarmonyMethod(p_ExpansionsLoader_StartLoad));
            GameEvents.OnExpansionSystemLoaded.Add(OnExpansionSystemLoaded);
            GameEvents.OnGameDatabaseLoaded.Add(OnGameDatabaseLoaded);

            configPath = ConfigPath;

            if (File.Exists(configPath))
            {
                ConfigNode config = ConfigNode.Load(configPath);

                if (!config.TryGetValue(nameof(userOptInChoiceDone), ref userOptInChoiceDone))
                    userOptInChoiceDone = false;

                if (!config.TryGetValue(nameof(textureCacheEnabled), ref textureCacheEnabled))
                    userOptInChoiceDone = false;
            }
        }

        /// <summary>
        /// Remove all harmony patches. Avoid breaking stock gamedatabase reload feature and runtime drag cube generation
        /// </summary>
        void OnDestroy()
        {
            if (!IsPatchEnabled)
                return;

            assetAndPartLoaderHarmony.UnpatchAll(AssetAndPartLoaderHarmonyID);
            assetAndPartLoaderHarmony = null;
            loader = null;
        }

        #region Asset loader reimplementation (patches)

        private static bool loadObjectsInProgress;

        /// <summary>
        /// This is our entry point in the GameDatabase loader (GameDatabase.LoadObjects()). It can't be patched directly because at the earliest point 
        /// we are capable of running code, we are already in that coroutine. So the strategy is to rewrite everything called after SetupMainLoaders()
        /// in a separate coroutine (FastAssetLoader) that we start before purposedly crashing GameDatabase.LoadObjects(). Doing so will cause the parent
        /// coroutine (GameDatabase.CreateDatabase()) to move on to the next loader coroutine (LoadAssetBundleObjects()). To prevent that coroutine (and 
        /// the rest of the loading process) from running immediately, we patch it so it wait for a flag (loadObjectsInProgress) to become false, which
        /// is done at the end of our FastAssetLoader coroutine. Now read that again, slowly.
        /// </summary>
        static void GameDatabase_SetupMainLoaders_Prefix()
        {
            GameDatabase gdb = GameDatabase.Instance;

            gdb.loadersAudio = new List<DatabaseLoader<AudioClip>>();
            gdb.loadersTexture = new List<DatabaseLoader<TextureInfo>>();
            gdb.loadersModel = new List<DatabaseLoader<GameObject>>();

            // only include loaders defined in mods, we replace all stock loaders
            foreach (AssemblyLoader.LoadedAssembly assembly in AssemblyLoader.loadedAssemblies)
            {
                if (assembly.assembly.GetName().Name == "Assembly-CSharp")
                    continue;

                foreach (Type t in AccessTools.GetTypesFromAssembly(assembly.assembly))
                {
                    if (t.IsSubclassOf(typeof(DatabaseLoader<AudioClip>)))
                    {
                        gdb.loadersAudio.Add((DatabaseLoader<AudioClip>)Activator.CreateInstance(t));
                    }
                    else if (t.IsSubclassOf(typeof(DatabaseLoader<TextureInfo>)))
                    {
                        gdb.loadersTexture.Add((DatabaseLoader<TextureInfo>)Activator.CreateInstance(t));
                    }
                    else if (t.IsSubclassOf(typeof(DatabaseLoader<GameObject>)))
                    {
                        gdb.loadersModel.Add((DatabaseLoader<GameObject>)Activator.CreateInstance(t));
                    }
                }
            }

            List<ConfigFileType> configFileTypes = new List<ConfigFileType>();

            ConfigFileType assemblyFileType = new ConfigFileType(FileType.Assembly);
            configFileTypes.Add(assemblyFileType);
            assemblyFileType.extensions.Add("dll");

            ConfigFileType audioFileType = new ConfigFileType(FileType.Audio);
            configFileTypes.Add(audioFileType);
            audioFileType.extensions.Add("wav");
            audioFileType.extensions.Add("ogg");
            foreach (DatabaseLoader<AudioClip> audioLoader in gdb.loadersAudio)
                audioFileType.extensions.AddRange(audioLoader.extensions);

            ConfigFileType textureFileType = new ConfigFileType(FileType.Texture);
            configFileTypes.Add(textureFileType);
            textureFileType.extensions.Add("dds");
            textureFileType.extensions.Add("jpg");
            textureFileType.extensions.Add("jpeg");
            textureFileType.extensions.Add("mbm");
            textureFileType.extensions.Add("png");
            textureFileType.extensions.Add("tga");
            textureFileType.extensions.Add("truecolor");
            foreach (DatabaseLoader<TextureInfo> textureLoader in gdb.loadersTexture)
                textureFileType.extensions.AddRange(textureLoader.extensions);

            ConfigFileType modelFileType = new ConfigFileType(FileType.Model);
            configFileTypes.Add(modelFileType);
            modelFileType.extensions.Add("mu");
            modelFileType.extensions.Add("dae");
            modelFileType.extensions.Add("DAE");
            foreach (DatabaseLoader<GameObject> modelLoader in gdb.loadersModel)
                modelFileType.extensions.AddRange(modelLoader.extensions);

            loadObjectsInProgress = true;
            gdb.StartCoroutine(FastAssetLoader(configFileTypes));

            Debug.Log("[KSPCF] Taking over stock loader. An exception will follow, this is intended.");
            throw new Exception("Terminating stock loader coroutine, this is intended and not an error");
        }

        static FieldInfo f_LoadAssetBundleObjects_Current;

        /// <summary>
        /// Wait for our FastAssetLoader() coroutine to finish before proceeding to the rest of the loading process
        /// </summary>
        static bool GameDatabase_LoadAssetBundleObjects_MoveNext_Prefix(object __instance, ref bool __result)
        {
            if (loadObjectsInProgress)
            {
                if (f_LoadAssetBundleObjects_Current == null)
                    f_LoadAssetBundleObjects_Current = AccessTools.GetDeclaredFields(__instance.GetType()).First(p => p.Name.Contains("current"));

                f_LoadAssetBundleObjects_Current.SetValue(__instance, null);
                __result = true;
                return false;
            }

            KSPCFFastLoaderReport.wAssetBundleLoading.Start();
            return true;
        }

        static void GameDatabase_LoadAssetBundleObjects_MoveNext_Postfix(object __instance, ref bool __result)
        {
            if (!__result)
            {
                KSPCFFastLoaderReport.wAssetBundleLoading.Stop();
                KSPCFFastLoaderReport.wGamedatabaseLoading.Start();
            }
        }

        private void OnGameDatabaseLoaded()
        {
            KSPCFFastLoaderReport.wGamedatabaseLoading.Stop();
            GameEvents.OnGameDatabaseLoaded.Remove(OnGameDatabaseLoaded);
        }




        static void ExpansionsLoader_StartLoad_Prefix() => KSPCFFastLoaderReport.wExpansionLoading.Start();

        private void OnExpansionSystemLoaded()
        {
            KSPCFFastLoaderReport.wExpansionLoading.Stop();
            expansionsLoaderHarmony.UnpatchAll(ExpansionsLoaderHarmonyID);
            GameEvents.OnExpansionSystemLoaded.Remove(OnExpansionSystemLoaded);
        }

        #endregion

        #region Asset loader reimplementation (main coroutine)

        /// <summary>
        /// Faster than Time.realtimeSinceStartup, result is in seconds.
        /// </summary>
        static double ElapsedTime => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        static int totalAssetCount;
        internal static int loadedAssetCount;

        /// <summary>
        /// Custom partial reimplementation of the stock GameDatabase.LoadObjects() coroutine
        /// - Concurrent audio assets loading
        /// - Threaded disk reads for textures/models
        /// - Partial reimplementation of the stock texture/model loaders
        /// - Framerate decoupling
        /// </summary>
        static IEnumerator FastAssetLoader(List<ConfigFileType> configFileTypes)
        {
            GameDatabase gdb = GameDatabase.Instance;
            gdb.progressTitle = "Loading configs...";
            gdb.progressFraction = 0f;

            // note : rebuilding the whole database here can be very long in a modde dinstall and
            // is quite silly since it was already built just before.
            // The intent is just to mark assets files with their type (UrlFile.fileType) according to
            // the registered type in configFileTypes.
            // However, the full reload means mods can take the opportunity to generate configs/assets on
            // the fly from Awake() in a Startup.Instantly KSPAddon and have it being loaded. I've found
            // at least 2 mods doing that, so unfortunately this can't really be optimized...
            KSPCFFastLoaderReport.wSecondConfigLoad.Restart();
            gdb._root = new UrlDir(gdb.urlConfig.ToArray(), configFileTypes.ToArray());
            KSPCFFastLoaderReport.wSecondConfigLoad.Stop();

            // Optimized version of GameDatabase.translateLoadedNodes()
            KSPCFFastLoaderReport.wConfigTranslate.Restart();
            TranslateLoadedNodes(gdb);
            KSPCFFastLoaderReport.wConfigTranslate.Stop();
            yield return null;

            // Start load asset bundles in the background while we load other assets.
            PreloadAssetBundleObjects(gdb);

            gdb.progressTitle = "Searching assets to load...";
            yield return null;

            KSPCFFastLoaderReport.wAssetsLoading.Restart();
            double nextFrameTime = ElapsedTime + minFrameTimeD;

            // Files loaded by our custom loaders
            List<UrlFile> audioFiles = new List<UrlFile>(1000);
            List<TextureLoadRequest> textureRequests = new List<TextureLoadRequest>(10000);
            List<RawAsset> modelAssets = new List<RawAsset>(5000);

            // Files loaded by mod-defined loaders (ex : Shabby *.shab files)
            List<UrlFile> unsupportedAudioFiles = new List<UrlFile>(100);
            List<UrlFile> unsupportedTextureFiles = new List<UrlFile>(100);
            List<UrlFile> unsupportedModelFiles = new List<UrlFile>(100);

            // Keeping track of already loaded files to avoid loading duplicates.
            // Note that to replicate stock behavior, we can't populate those
            // directly, we have to ensure a file is actually loaded without errors
            // before flaging a same-url file as duplicate. Not doing this can break
            // mods relying on that implementation detail, looking at you, Shabby
            // and ConformalDecals
            HashSet<string> allAudioFiles = new HashSet<string>(1000);
            HashSet<string> allTextureFiles = new HashSet<string>(10000);
            HashSet<string> allModelFiles = new HashSet<string>(5000);

            foreach (UrlDir dir in gdb.root.AllDirectories)
            {
                int fileCount = dir.files.Count;
                for (int i = 0; i < fileCount; i++)
                {
                    UrlFile file = dir.files[i];
                    if (file == null)
                        continue;

                    totalAssetCount++;
                    switch (file.fileType)
                    {
                        case FileType.Audio:
                            switch (file.fileExtension)
                            {
                                case "wav":
                                case "ogg":
                                    audioFiles.Add(file);
                                    break;
                                default:
                                    unsupportedAudioFiles.Add(file);
                                    break;
                            }
                            break;
                        case FileType.Texture:
                            switch (file.fileExtension)
                            {
                                case "dds":
                                    textureRequests.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureDDS));
                                    break;
                                case "jpg":
                                case "jpeg":
                                    textureRequests.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureJPG));
                                    break;
                                case "mbm":
                                    textureRequests.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureMBM));
                                    break;
                                case "png":
                                    textureRequests.Add(new TextureLoadRequest(file, RawAsset.AssetType.TexturePNG));
                                    break;
                                case "tga":
                                    textureRequests.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureTGA));
                                    break;
                                case "truecolor":
                                    textureRequests.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureTRUECOLOR));
                                    break;
                                default:
                                    unsupportedTextureFiles.Add(file);
                                    break;
                            }
                            break;
                        case FileType.Model:
                            switch (file.fileExtension)
                            {
                                case "mu":
                                    modelAssets.Add(new RawAsset(file, RawAsset.AssetType.ModelMU));
                                    break;
                                case "dae":
                                case "DAE":
                                    modelAssets.Add(new RawAsset(file, RawAsset.AssetType.ModelDAE));
                                    break;
                                default:
                                    unsupportedModelFiles.Add(file);
                                    break;
                            }
                            break;
                    }
                }

                if (ElapsedTime > nextFrameTime)
                {
                    nextFrameTime = ElapsedTime + minFrameTimeD;
                    yield return null;
                }
            }

            gdb.progressTitle = "Loading sound assets...";
            KSPCFFastLoaderReport.wAudioLoading.Restart();
            yield return null;

            // call non-stock audio loaders
            int unsupportedFilesCount = unsupportedAudioFiles.Count;
            int loadersCount = gdb.loadersAudio.Count;

            if (loadersCount > 0 && unsupportedFilesCount > 0)
            {
                for (int i = 0; i < unsupportedFilesCount; i++)
                {
                    UrlFile file = unsupportedAudioFiles[i];

                    if (allAudioFiles.Contains(file.url))
                    {
                        Debug.LogWarning($"Duplicate audio asset '{file.url}' with extension '{file.fileExtension}' won't be loaded");
                        continue;
                    }

                    Debug.Log($"Load Audio: {file.url}");
                    for (int k = 0; k < loadersCount; k++)
                    {
                        DatabaseLoader<AudioClip> loader = gdb.loadersAudio[k];
                        if (!loader.extensions.Contains(file.fileExtension))
                            continue;

                        yield return gdb.StartCoroutine(loader.Load(file, new FileInfo(file.fullPath)));

                        if (loader.successful)
                        {
                            loader.obj.name = file.url;
                            gdb.databaseAudio.Add(loader.obj);
                            gdb.databaseAudioFiles.Add(file);
                            allAudioFiles.Add(file.url);
                            loadedAssetCount++;
                            gdb.progressFraction = (float)loadedAssetCount / totalAssetCount;
                        }
                        break;
                    }
                }
            }

            // custom wav/ogg audio loader
            int audioFilesCount = audioFiles.Count;
            const int maxConcurrentCoroutines = 25;
            int j = 0;

            while (j < audioFilesCount)
            {
                if (concurrentAudioCoroutines < maxConcurrentCoroutines)
                {
                    UrlFile file = audioFiles[j];

                    if (!allAudioFiles.Add(file.url))
                    {
                        Debug.LogWarning($"Duplicate audio asset '{file.url}' with extension '{file.fileExtension}' won't be loaded");
                        audioFilesLoaded++;
                    }
                    else
                    {
                        Debug.Log($"Load Audio: {file.url}");
                        gdb.StartCoroutine(AudioLoader(file));
                    }
                    j++;
                }
                else if (ElapsedTime > nextFrameTime)
                {
                    nextFrameTime = ElapsedTime + minFrameTimeD;
                    gdb.progressFraction = (float)(loadedAssetCount + audioFilesLoaded) / totalAssetCount;
                    gdb.progressTitle = $"Loading sound asset {audioFilesLoaded}/{audioFilesCount}";
                    yield return null;
                }
            }

            while (audioFilesLoaded < audioFilesCount)
            {
                gdb.progressFraction = (float)(loadedAssetCount + audioFilesLoaded) / totalAssetCount;
                gdb.progressTitle = $"Loading sound asset {audioFilesLoaded}/{audioFilesCount}";
                yield return null;
            }

            loadedAssetCount += audioFilesLoaded;

            // initialize array pool
            arrayPool = ArrayPool<byte>.Create(1024 * 1024 * 20, 50);

            // start texture loading
            gdb.progressFraction = 0.25f;
            KSPCFFastLoaderReport.wAudioLoading.Stop();
            SupportedFormatCache.Build();
            KSPCFFastLoaderReport.wTextureLoading.Restart();
            gdb.progressTitle = "Loading texture assets...";
            yield return null;

            // call non-stock texture loaders

            // note : we could use the StringComparer.OrdinalIgnoreCase comparer as the dictionary key comparer,
            // as this is the comparison that stock is doing. However, profiling show that casing mismatches rarely happen
            // (never in stock, 0.22% of calls in a very heavily modded install with a bunch of part mods of varying quality)
            // and the overhead of the OrdinalIgnoreCase comparer is offsetting the gains (but a small margin, but still). 
            texturesByUrl = new Dictionary<string, TextureInfo>(allTextureFiles.Count);
            unsupportedFilesCount = unsupportedTextureFiles.Count;
            loadersCount = gdb.loadersTexture.Count;

            if (loadersCount > 0 && unsupportedFilesCount > 0)
            {
                for (int i = 0; i < unsupportedFilesCount; i++)
                {
                    UrlFile file = unsupportedTextureFiles[i];

                    if (allTextureFiles.Contains(file.url))
                    {
                        Debug.LogWarning($"Duplicate texture asset '{file.url}' with extension '{file.fileExtension}' won't be loaded");
                        continue;
                    }

                    Debug.Log($"Load Texture: {file.url}");
                    for (int k = 0; k < loadersCount; k++)
                    {
                        DatabaseLoader<TextureInfo> loader = gdb.loadersTexture[k];
                        if (!loader.extensions.Contains(file.fileExtension))
                            continue;

                        yield return gdb.StartCoroutine(loader.Load(file, new FileInfo(file.fullPath)));
                        if (loader.successful)
                        {
                            loader.obj.name = file.url;
                            loader.obj.texture.name = file.url;
                            gdb.databaseTexture.Add(loader.obj);
                            allTextureFiles.Add(file.url);
                            loadedAssetCount++;
                            gdb.progressFraction = (float)loadedAssetCount / totalAssetCount;
                        }
                        break;
                    }
                }
            }

            // call our custom loader

            yield return gdb.StartCoroutine(TextureDriverCoroutine(textureRequests, allTextureFiles));

            // start model loading
            gdb.progressFraction = 0.75f;
            KSPCFFastLoaderReport.wTextureLoading.Stop();
            KSPCFFastLoaderReport.wModelLoading.Start();
            gdb.progressTitle = "Loading model assets...";
            yield return null;

            // call non-stock model loaders
            modelsByUrl = new Dictionary<string, GameObject>(allModelFiles.Count);
            modelsByDirectoryUrl = new Dictionary<string, GameObject>(allModelFiles.Count);
            urlFilesByModel = new Dictionary<GameObject, UrlFile>(allModelFiles.Count);
            unsupportedFilesCount = unsupportedModelFiles.Count;
            loadersCount = gdb.loadersModel.Count;

            if (loadersCount > 0 && unsupportedFilesCount > 0)
            {
                for (int i = 0; i < unsupportedFilesCount; i++)
                {
                    UrlFile file = unsupportedModelFiles[i];

                    if (allModelFiles.Contains(file.url))
                    {
                        Debug.LogWarning($"Duplicate model asset '{file.url}' with extension '{file.fileExtension}' won't be loaded");
                        continue;
                    }

                    Debug.Log($"Load Model: {file.url}");
                    for (int k = 0; k < loadersCount; k++)
                    {
                        DatabaseLoader<GameObject> loader = gdb.loadersModel[k];
                        if (loader.extensions.Contains(file.fileExtension))
                        {
                            yield return gdb.StartCoroutine(loader.Load(file, new FileInfo(file.fullPath)));
                            if (loader.successful)
                            {
                                GameObject obj = loader.obj;
                                obj.transform.name = file.url;
                                obj.transform.parent = gdb.transform;
                                obj.transform.localPosition = Vector3.zero;
                                obj.transform.localRotation = Quaternion.identity;
                                obj.SetActive(value: false);
                                gdb.databaseModel.Add(obj);
                                gdb.databaseModelFiles.Add(file);
                                allModelFiles.Add(file.url);
                                loadedAssetCount++;
                                gdb.progressFraction = (float)loadedAssetCount / totalAssetCount;
                            }
                        }
                    }
                }
            }

            // call our custom loader
            yield return gdb.StartCoroutine(FilesLoader(modelAssets, allModelFiles, "Loading model asset"));

            // all done, do some cleanup
            arrayPool = null;
            MuParser.ReleaseBuffers();

            // stock stuff
            gdb.lastLoadTime = KSPUtil.SystemDateTime.DateTimeNow();
            gdb.progressFraction = 1f;
            loadObjectsInProgress = false;
            KSPCFFastLoaderReport.wModelLoading.Stop();
            KSPCFFastLoaderReport.wAssetsLoading.Stop();
        }

        /// <summary>
        /// ~100 times faster replacement for the stock GameDatabase.translateLoadedNodes() method (RP1 install 12500 ms -> 80 ms)
        /// </summary>
        private static void TranslateLoadedNodes(GameDatabase gdb)
        {
            Dictionary<string, string> tags = Localizer.Instance.tagValues;
            UrlDir root = gdb._root;
            Stack<UrlDir> dirStack = new Stack<UrlDir>(100);
            Stack<ConfigNode> nodesStack = new Stack<ConfigNode>(100);

            dirStack.Push(root);
            while (dirStack.TryPop(out UrlDir urlDir))
            {
                foreach (UrlDir childUrlDir in urlDir.children)
                    dirStack.Push(childUrlDir);

                foreach (UrlFile urlFile in urlDir._files)
                {
                    if (urlFile._fileType != FileType.Config)
                        continue;

                    foreach (UrlConfig urlConfig in urlFile._configs)
                    {
                        nodesStack.Push(urlConfig.config);

                        while (nodesStack.TryPop(out ConfigNode configNode))
                        {
                            foreach (ConfigNode childNode in configNode._nodes.nodes)
                                nodesStack.Push(childNode);

                            foreach (ConfigNode.Value configNodeValue in configNode._values.values)
                            {
                                string value = configNodeValue.value;
                                if (string.IsNullOrEmpty(value))
                                    continue;

                                if (tags.TryGetValue(value, out string localizedValue))
                                    value = localizedValue;

                                configNodeValue.value = LocalizerPerf.UnescapeFormattedString(value);
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Asset loader reimplementation (audio loader)

        static int concurrentAudioCoroutines;
        internal static int audioFilesLoaded;


        /// <summary>
        /// Concurrent coroutines (read "multiple coroutines in the same frame") audio loader
        /// </summary>
        static IEnumerator AudioLoader(UrlFile urlFile)
        {
            concurrentAudioCoroutines++;

            try
            {
                FileInfo fileInfo = new FileInfo(urlFile.fullPath);
                KSPCFFastLoaderReport.audioBytesLoaded += fileInfo.Length;
                string normalizedUri = KSPUtil.ApplicationFileProtocol + fileInfo.FullName;
                UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(normalizedUri, AudioType.UNKNOWN);
                yield return request.SendWebRequest();
                while (!request.isDone)
                {
                    yield return null;
                }
                if (!request.isNetworkError && !request.isHttpError)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    clip.name = urlFile.url;
                    GameDatabase.Instance.databaseAudio.Add(clip);
                    GameDatabase.Instance.databaseAudioFiles.Add(urlFile);
                }
                else
                {
                    Debug.LogWarning("Audio file: " + urlFile.name + " load error: " + request.error);
                }
            }
            finally
            {
                concurrentAudioCoroutines--;
                audioFilesLoaded++;
            }
        }

        #endregion

        #region Asset loader reimplementation (texture/model loader)

        static ArrayPool<byte> arrayPool;
        static int loadedBytes;
        static object lockObject = new object();

        /// <summary>
        /// Textures / models loader coroutine implementing threaded disk reads and framerate decoupling
        /// </summary>
        static IEnumerator FilesLoader(List<RawAsset> assets, HashSet<string> loadedUrls, string loadingLabel)
        {
            GameDatabase gdb = GameDatabase.Instance;

            Deque<RawAsset> assetBuffer = new Deque<RawAsset>();
            int assetCount = assets.Count;
            int currentAssetIndex = 0;

            Thread readerThread = new Thread(() => ReadAssetsThread(assets, assetBuffer));
            readerThread.Start();

            double nextFrameTime = ElapsedTime + minFrameTimeD;
            SpinWait spinWait = new SpinWait();

            while (currentAssetIndex < assetCount)
            {
                while (!Monitor.TryEnter(lockObject))
                    spinWait.SpinOnce();

                RawAsset rawAsset;
                int bufferTotalSize;

                try
                {
                    if (assetBuffer.Count > 0)
                    {
                        rawAsset = assetBuffer.RemoveFromBack();
                        loadedBytes -= rawAsset.DataLength;
                        bufferTotalSize = loadedBytes;
                    }
                    else
                    {
                        continue;
                    }
                }
                finally
                {
                    Monitor.Exit(lockObject);
                }

                try
                {
                    if (!loadedUrls.Add(rawAsset.File.url))
                    {
                        rawAsset.Dispose();
                        Debug.LogWarning($"Duplicate {rawAsset.TypeName} '{rawAsset.File.url}' with extension '{rawAsset.File.fileExtension}' won't be loaded");
                        continue;
                    }

                    Debug.Log($"Load {rawAsset.TypeName}: {rawAsset.File.url}");
                    rawAsset.LoadAndDisposeMainThread();

                    if (rawAsset.State == RawAsset.Result.Failed)
                    {
                        Debug.LogWarning($"LOAD FAILED : {rawAsset.Message}");
                    }
                    else if (rawAsset.State == RawAsset.Result.Warning)
                    {
                        Debug.LogWarning(rawAsset.Message);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    loadedAssetCount++;
                    currentAssetIndex++;
                    spinWait = new SpinWait();
                }

                if (ElapsedTime > nextFrameTime)
                {
                    nextFrameTime = ElapsedTime + minFrameTimeD;
                    gdb.progressFraction = (float)loadedAssetCount / totalAssetCount;
                    gdb.progressTitle = $"{loadingLabel} {currentAssetIndex}/{assetCount} (buffer={bufferTotalSize / (1024 * 1024)}MB)";
                    yield return null;
                }
            }
        }

        /// <summary>
        /// Disk read thread started from FilesLoader
        /// </summary>
        static void ReadAssetsThread(List<RawAsset> files, Deque<RawAsset> buffer)
        {
            foreach (RawAsset rawAsset in files)
            {
                rawAsset.ReadFromDiskWorkerThread();

                SpinWait spin = new SpinWait();
                bool assetAdded = false;

                while (!assetAdded)
                {
                    while (!Monitor.TryEnter(lockObject))
                        spin.SpinOnce();

                    try
                    {
                        // load next file if already sum of already loaded file size is less than 50 MB
                        // or if less than 10 files are loaded
                        if (loadedBytes < maxBufferSize || buffer.Count < minFileRead)
                        {
                            loadedBytes += rawAsset.DataLength;
                            buffer.AddToFront(rawAsset);
                            assetAdded = true;
                        }
                    }
                    finally
                    {
                        Monitor.Exit(lockObject);
                    }
                }
            }
        }

        /// <summary>
        /// Asset wrapper class, actual implementation of the disk reader, individual texture/model formats loaders
        /// </summary>
        private class RawAsset
        {
            public enum AssetType
            {
                TextureDDS,
                TextureJPG,
                TextureMBM,
                TexturePNG,
                TextureTGA,
                TextureTRUECOLOR,
                ModelMU,
                ModelDAE
            }

            private static readonly string[] assetTypeNames =
            {
                "DDS texture",
                "JPG texture",
                "MBM texture",
                "PNG texture",
                "TGA texture",
                "TRUECOLOR texture",
                "MU model",
                "DAE model"
            };

            public enum Result
            {
                Valid,
                Warning,
                Failed
            }

            private UrlFile file;
            private AssetType assetType;
            private bool useRentedBuffer;
            private byte[] buffer;
            private int dataLength;
            private Result result;
            private string resultMessage;

            public UrlFile File => file;
            public Result State => result;
            public string Message => resultMessage;
            public int DataLength => dataLength;
            public string TypeName => assetTypeNames[(int)assetType];

            public RawAsset(UrlFile file, AssetType assetType)
            {
                this.result = Result.Valid;
                this.file = file;
                this.assetType = assetType;
            }

            private void SetError(string message)
            {
                result = Result.Failed;
                if (resultMessage == null)
                    resultMessage = message;
                else
                    resultMessage = $"{resultMessage}\n{message}";
            }

            private void SetWarning(string message)
            {
                if (result == Result.Failed)
                {
                    if (resultMessage == null)
                        resultMessage = message;
                    else
                        resultMessage = $"{resultMessage}\nWARNING: {message}";
                }
                else
                {
                    result = Result.Warning;
                    if (resultMessage == null)
                        resultMessage = message;
                    else
                        resultMessage = $"{resultMessage}\n{message}";
                }
            }

            public void ReadFromDiskWorkerThread()
            {
                switch (assetType)
                {
                    case AssetType.ModelMU:
                    case AssetType.ModelDAE:
                        useRentedBuffer = true;
                        break;
                }

                try
                {
                    string path = file.fullPath;

                    using (FileStream fileStream = System.IO.File.OpenRead(path))
                    {
                        long length = fileStream.Length;
                        if (length > int.MaxValue)
                        {
                            throw new IOException("Reading more than 2GB with this call is not supported");
                        }
                        dataLength = (int)length;
                        int offset = 0;
                        int count = dataLength;

                        // Don't use array pool for small files < 1KB (allocating is faster)
                        // Don't use array pool for huge files > 20MB (memory usage concerns)
                        if (useRentedBuffer)
                            if (dataLength < 1024 || dataLength > 1024 * 1024 * 20)
                                useRentedBuffer = false;

                        if (useRentedBuffer)
                            buffer = arrayPool.Rent(dataLength);
                        else
                            buffer = new byte[dataLength];

                        try
                        {
                            while (count > 0)
                            {
                                int read = fileStream.Read(buffer, offset, count);
                                if (read == 0)
                                {
                                    throw new IOException("Unexpected end of stream");
                                }
                                offset += read;
                                count -= read;
                            }
                        }
                        catch
                        {
                            if (useRentedBuffer)
                            {
                                arrayPool.Return(buffer);
                                buffer = null;
                            }
                            throw;
                        }
                    }
                }
                catch (Exception e)
                {
                    SetError(e.Message);
                }
            }

            public void LoadAndDisposeMainThread()
            {
                try
                {
                    if (result == Result.Failed)
                        return;

                    if (file.fileType == FileType.Model)
                    {
                        GameObject model;
                        switch (assetType)
                        {
                            case AssetType.ModelMU:
                                model = LoadMU();
                                break;
                            case AssetType.ModelDAE:
                                model = LoadDAE();
                                break;
                            default:
                                SetError("Unknown model format");
                                return;
                        }

                        if (result == Result.Failed || model.IsNullOrDestroyed())
                        {
                            result = Result.Failed;
                            if (string.IsNullOrEmpty(resultMessage))
                                resultMessage = $"{TypeName} load error";
                        }
                        else
                        {
                            model.transform.name = file.url;
                            model.transform.parent = Instance.transform;
                            model.transform.localPosition = Vector3.zero;
                            model.transform.localRotation = Quaternion.identity;
                            model.SetActive(false);
                            Instance.databaseModel.Add(model);
                            Instance.databaseModelFiles.Add(file);
                            modelsByUrl[file.url] = model;
                            // if multiple models in the same dir, we only add the first
                            // to ensure identical behavior as the GameDatabase.GetModelPrefabIn() method
                            modelsByDirectoryUrl.TryAdd(file.parent.url, model);
                            urlFilesByModel.Add(model, file);
                            KSPCFFastLoaderReport.modelsBytesLoaded += dataLength;
                            KSPCFFastLoaderReport.modelsLoaded++;
                        }
                    }
                }
                catch (Exception e)
                {
                    SetError(e.ToString());
                }
                finally
                {
                    Dispose();
                }
            }

            public void Dispose()
            {
                if (useRentedBuffer)
                    arrayPool.Return(buffer);
            }

            private GameObject LoadMU()
            {
                return MuParser.Parse(file.parent.url, buffer, dataLength);
            }

            private GameObject LoadDAE()
            {
                // given that this is a quite obsolete thing and that it's mess to reimplement, just call the stock
                // stuff and re-load the file

                GameObject gameObject = new DatabaseLoaderModel_DAE.DAE().Load(file, new FileInfo(file.fullPath));
                if (gameObject.IsNotNullOrDestroyed())
                {
                    MeshFilter[] componentsInChildren = gameObject.GetComponentsInChildren<MeshFilter>();
                    foreach (MeshFilter meshFilter in componentsInChildren)
                    {
                        if (meshFilter.gameObject.name == "node_collider")
                        {
                            meshFilter.gameObject.AddComponent<MeshCollider>().sharedMesh = meshFilter.mesh;
                            MeshRenderer component = meshFilter.gameObject.GetComponent<MeshRenderer>();
                            UnityEngine.Object.Destroy(meshFilter);
                            UnityEngine.Object.Destroy(component);
                        }
                    }
                }

                return gameObject;
            }

        }

        #endregion

        #region Per-texture coroutine loader

        // Profiling markers for the work scheduled on background threads via Task.Run.
        // Each marker.Auto() scope is opened inside the Task lambda so the timing
        // appears under that thread in the Unity profiler.
        private static readonly ProfilerMarker s_pmParseDDSHeader = new ProfilerMarker("KSPCF.Tex.ParseDDSHeader");
        private static readonly ProfilerMarker s_pmSwizzleNormalMap = new ProfilerMarker("KSPCF.Tex.SwizzleNormalMap");
        private static readonly ProfilerMarker s_pmFileSize = new ProfilerMarker("KSPCF.Tex.FileSize");
        private static readonly ProfilerMarker s_pmReadAllBytes = new ProfilerMarker("KSPCF.Tex.ReadAllBytes");
        private static readonly ProfilerMarker s_pmCompress = new ProfilerMarker("KSPCF.Tex.Compress");
        private static readonly ProfilerMarker s_pmGetRawDataDDS = new ProfilerMarker("KSPCF.Tex.LoadDDS.GetRawTextureData");
        private static readonly ProfilerMarker s_pmGetRawDataUWR = new ProfilerMarker("KSPCF.Tex.LoadUWR.GetRawTextureData");
        private static readonly ProfilerMarker s_pmGetRawDataTRUECOLOR = new ProfilerMarker("KSPCF.Tex.LoadTRUECOLOR.GetRawTextureData");
        private static readonly ProfilerMarker s_pmGetRawDataTGA = new ProfilerMarker("KSPCF.Tex.LoadTGA.GetRawTextureData");

        // Result/error carrier for each texture file. Replaces RawAsset for textures.
        private sealed class TextureLoadRequest
        {
            public enum State : byte { Pending, Ready, Failed }

            public UrlFile File;
            public RawAsset.AssetType AssetType;
            public long FileLength;
            public volatile State Status;
            public TextureInfo Result;
            public string ErrorMessage;
            public Exception Exception;

            public TextureLoadRequest(UrlFile file, RawAsset.AssetType assetType)
            {
                File = file;
                AssetType = assetType;
                Status = State.Pending;
            }
        }

        // Result of background DDS header parsing.
        private struct DDSPreparedHeader
        {
            public int Width;
            public int Height;
            public bool MipChain;
            public bool IsNormalMap;
            public GraphicsFormat Format;
            public long DataOffset;
            public long FileLength;
        }

        // Probes which GraphicsFormats are actually usable on the running GPU.
        // Built once on the main thread before texture loading starts so that the
        // background DDS header parser can produce a format and we can verify it
        // against this set without needing main-thread access.
        private static class SupportedFormatCache
        {
            private static HashSet<GraphicsFormat> supported;

            public static void Build()
            {
                supported = new HashSet<GraphicsFormat>();
                GraphicsFormat[] candidates = new[]
                {
                    GraphicsFormat.RGBA_DXT1_UNorm,
                    GraphicsFormat.RGBA_DXT1_SRGB,
                    GraphicsFormat.RGBA_DXT5_UNorm,
                    GraphicsFormat.RGBA_DXT5_SRGB,
                    GraphicsFormat.R_BC4_UNorm,
                    GraphicsFormat.R_BC4_SNorm,
                    GraphicsFormat.RG_BC5_UNorm,
                    GraphicsFormat.RG_BC5_SNorm,
                    GraphicsFormat.RGBA_BC7_UNorm,
                    GraphicsFormat.RGBA_BC7_SRGB,
                    GraphicsFormat.RGB_BC6H_SFloat,
                    GraphicsFormat.RGB_BC6H_UFloat,
                    GraphicsFormat.R16G16B16A16_UNorm,
                    GraphicsFormat.R16G16B16A16_SNorm,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    GraphicsFormat.R16_SFloat,
                    GraphicsFormat.R16G16_SFloat,
                    GraphicsFormat.R32_SFloat,
                    GraphicsFormat.R32G32_SFloat,
                    GraphicsFormat.R32G32B32A32_SFloat,
                };
                foreach (GraphicsFormat fmt in candidates)
                    if (SystemInfo.IsFormatSupported(fmt, FormatUsage.Sample))
                        supported.Add(fmt);
            }

            public static bool IsSupported(GraphicsFormat fmt) => supported != null && supported.Contains(fmt);
        }

        private static DDSPreparedHeader ParseDDSHeader(string path)
        {
            FileInfo fi = new FileInfo(path);
            long fileLength = fi.Length;
            if (fileLength < 128)
                throw new IOException($"DDS file '{path}' is too small ({fileLength} bytes)");

            using FileStream fs = File.OpenRead(path);
            using BinaryReader br = new BinaryReader(fs);

            if (br.ReadUInt32() != DDSValues.uintMagic)
                throw new IOException($"DDS: '{path}' is not a DDS format file");

            DDSHeader hdr = new DDSHeader(br);
            bool mipChain = (hdr.dwCaps & DDSPixelFormatCaps.MIPMAP) != 0;
            bool isNormalMap = (hdr.ddspf.dwFlags & 0x80000u) != 0 || (hdr.ddspf.dwFlags & 0x80000000u) != 0;

            DDSHeaderDX10 dx10Header = default;
            bool hasDx10 = (DDSFourCC)hdr.ddspf.dwFourCC == DDSFourCC.DX10;
            if (hasDx10)
            {
                if (fileLength < 148)
                    throw new IOException($"DDS file '{path}' has DX10 marker but is too small for DX10 header");
                dx10Header = new DDSHeaderDX10(br);
            }

            GraphicsFormat fmt = MapDDSFormat(hdr, hasDx10, dx10Header, out string error);
            if (fmt == GraphicsFormat.None || error != null)
                throw new IOException($"DDS: {error ?? "unknown format"}");

            long dataOffset = hasDx10 ? 148 : 128;
            return new DDSPreparedHeader
            {
                Width = (int)hdr.dwWidth,
                Height = (int)hdr.dwHeight,
                MipChain = mipChain,
                IsNormalMap = isNormalMap,
                Format = fmt,
                DataOffset = dataOffset,
                FileLength = fileLength,
            };
        }

        private enum DDSFourCC : uint
        {
            DXT1 = 0x31545844,
            DXT2 = 0x32545844,
            DXT3 = 0x33545844,
            DXT4 = 0x34545844,
            DXT5 = 0x35545844,
            BC4U_ATI = 0x31495441,
            BC4U = 0x55344342,
            BC4S = 0x53344342,
            BC5U_ATI = 0x32495441,
            BC5U = 0x55354342,
            BC5S = 0x53354342,
            RGBG = 0x47424752,
            GRGB = 0x42475247,
            UYVY = 0x59565955,
            YUY2 = 0x32595559,
            DX10 = 0x30315844,
            R16G16B16A16_UNORM = 36,
            R16G16B16A16_SNORM = 110,
            R16_FLOAT = 111,
            R16G16_FLOAT = 112,
            R16G16B16A16_FLOAT = 113,
            R32_FLOAT = 114,
            R32G32_FLOAT = 115,
            R32G32B32A32_FLOAT = 116,
            CxV8U8 = 117,
        }

        // Returns GraphicsFormat.None and sets error on failure.
        private static GraphicsFormat MapDDSFormat(DDSHeader hdr, bool hasDx10, DDSHeaderDX10 dx10, out string error)
        {
            error = null;
            DDSFourCC fourCC = (DDSFourCC)hdr.ddspf.dwFourCC;
            switch (fourCC)
            {
                case DDSFourCC.DXT1: return GraphicsFormatUtility.GetGraphicsFormat(TextureFormat.DXT1, true);
                case DDSFourCC.DXT5: return GraphicsFormatUtility.GetGraphicsFormat(TextureFormat.DXT5, true);
                case DDSFourCC.BC4U_ATI:
                case DDSFourCC.BC4U: return GraphicsFormat.R_BC4_UNorm;
                case DDSFourCC.BC4S: return GraphicsFormat.R_BC4_SNorm;
                case DDSFourCC.BC5U_ATI:
                case DDSFourCC.BC5U: return GraphicsFormat.RG_BC5_UNorm;
                case DDSFourCC.BC5S: return GraphicsFormat.RG_BC5_SNorm;
                case DDSFourCC.R16G16B16A16_UNORM: return GraphicsFormat.R16G16B16A16_UNorm;
                case DDSFourCC.R16G16B16A16_SNORM: return GraphicsFormat.R16G16B16A16_SNorm;
                case DDSFourCC.R16_FLOAT: return GraphicsFormat.R16_SFloat;
                case DDSFourCC.R16G16_FLOAT: return GraphicsFormat.R16G16_SFloat;
                case DDSFourCC.R16G16B16A16_FLOAT: return GraphicsFormat.R16G16B16A16_SFloat;
                case DDSFourCC.R32_FLOAT: return GraphicsFormat.R32_SFloat;
                case DDSFourCC.R32G32_FLOAT: return GraphicsFormat.R32G32_SFloat;
                case DDSFourCC.R32G32B32A32_FLOAT: return GraphicsFormat.R32G32B32A32_SFloat;
                case DDSFourCC.DX10:
                    if (!hasDx10)
                    {
                        error = "DX10 marker without DX10 header";
                        return GraphicsFormat.None;
                    }
                    switch (dx10.dxgiFormat)
                    {
                        case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM: return GraphicsFormat.RGBA_DXT1_UNorm;
                        case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB: return GraphicsFormat.RGBA_DXT1_SRGB;
                        case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM: return GraphicsFormat.RGBA_DXT5_UNorm;
                        case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB: return GraphicsFormat.RGBA_DXT5_SRGB;
                        case DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM: return GraphicsFormat.R_BC4_SNorm;
                        case DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM: return GraphicsFormat.R_BC4_UNorm;
                        case DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM: return GraphicsFormat.RG_BC5_SNorm;
                        case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM: return GraphicsFormat.RG_BC5_UNorm;
                        case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM: return GraphicsFormat.RGBA_BC7_UNorm;
                        case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB: return GraphicsFormat.RGBA_BC7_SRGB;
                        case DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16: return GraphicsFormat.RGB_BC6H_SFloat;
                        case DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16: return GraphicsFormat.RGB_BC6H_UFloat;
                        case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM: return GraphicsFormat.R16G16B16A16_UNorm;
                        case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM: return GraphicsFormat.R16G16B16A16_SNorm;
                        case DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT: return GraphicsFormat.R16_SFloat;
                        case DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT: return GraphicsFormat.R16G16_SFloat;
                        case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT: return GraphicsFormat.R16G16B16A16_SFloat;
                        case DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT: return GraphicsFormat.R32_SFloat;
                        case DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT: return GraphicsFormat.R32G32_SFloat;
                        case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT: return GraphicsFormat.R32G32B32A32_SFloat;
                        default:
                            error = $"DXT10 format '{dx10.dxgiFormat}' is not supported";
                            return GraphicsFormat.None;
                    }
                case DDSFourCC.DXT2:
                case DDSFourCC.DXT3:
                case DDSFourCC.DXT4:
                case DDSFourCC.RGBG:
                case DDSFourCC.GRGB:
                case DDSFourCC.UYVY:
                case DDSFourCC.YUY2:
                case DDSFourCC.CxV8U8:
                    error = $"format '{fourCC}' is not supported, use DXT1 for RGB textures or DXT5 for RGBA textures";
                    return GraphicsFormat.None;
                default:
                    error = $"unknown dwFourCC format '0x{(uint)fourCC:X}'";
                    return GraphicsFormat.None;
            }
        }

        // In-place channel-swizzle for RGBA32 normal maps. Operates on the texture's
        // entire raw byte buffer, which means every populated mip level when the texture
        // was created with a mip chain.
        //
        // Channel swizzle is per-pixel and the box-filter mip generator is linear, so
        // swizzling pre-built mips matches what stock KSP produced by swizzling level-0
        // and regenerating from there (BitmapToCompressedNormalMapFast).
        private static unsafe void SwizzleNormalMap(NativeArray<byte> data)
        {
            using var scope = s_pmSwizzleNormalMap.Auto();

            byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(data);
            int len = data.Length;
            // (r, g, b, a) -> (g, g, g, r)
            for (int i = 0; i < len; i += 4)
            {
                byte r = p[i];
                byte g = p[i + 1];
                p[i] = g;
                p[i + 1] = g;
                p[i + 2] = g;
                p[i + 3] = r;
            }
        }

        // Legacy src->dst swizzle, kept for the rare TGA RGB24 path (where source and
        // destination have different pixel sizes so an in-place transform is impossible).
        // Walks src.Length end-to-end; the caller must size dst with a mip chain that
        // matches src's so the constant 3:4 (or 4:4) byte-count ratio fills dst exactly.
        private static unsafe void SwizzleNormalMap(NativeArray<byte> src, NativeArray<byte> dst, TextureFormat srcFormat)
        {
            using var scope = s_pmSwizzleNormalMap.Auto();

            byte* s = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(src);
            byte* d = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst);
            int srcLen = src.Length;

            switch (srcFormat)
            {
                case TextureFormat.RGBA32:
                    // (r, g, b, a) -> (g, g, g, r)
                    for (int i = 0; i < srcLen; i += 4)
                    {
                        byte r = s[i];
                        byte g = s[i + 1];
                        d[i] = g; d[i + 1] = g; d[i + 2] = g; d[i + 3] = r;
                    }
                    break;
                case TextureFormat.ARGB32:
                    // (a, r, g, b) -> (g, g, g, r)
                    for (int i = 0; i < srcLen; i += 4)
                    {
                        byte r = s[i + 1];
                        byte g = s[i + 2];
                        d[i] = g; d[i + 1] = g; d[i + 2] = g; d[i + 3] = r;
                    }
                    break;
                case TextureFormat.RGB24:
                    // (r, g, b) -> (g, g, g, r); 3-byte in, 4-byte out
                    {
                        int j = 0;
                        for (int i = 0; i < srcLen; i += 3)
                        {
                            byte r = s[i];
                            byte g = s[i + 1];
                            d[j] = g; d[j + 1] = g; d[j + 2] = g; d[j + 3] = r;
                            j += 4;
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException($"SwizzleNormalMap: unsupported source format {srcFormat}");
            }
        }

        // Returns the most informative exception from a faulted Task
        private static Exception UnwrapFaultedTask(Task task, string fallbackMessage)
        {
            AggregateException ae = task.Exception;
            if (ae != null && ae.InnerException != null)
                return ae.InnerException;
            if (ae != null)
                return ae;
            return new IOException(fallbackMessage);
        }

        // Iterator methods can't contain unsafe blocks in C# 8, so the AsyncReadManager
        // pointer setup goes through this static helper.
        private static unsafe ReadHandle BeginAsyncRead(string path, NativeArray<byte> dst, long offset, long size)
        {
            ReadCommand cmd = new ReadCommand
            {
                Buffer = NativeArrayUnsafeUtility.GetUnsafePtr(dst),
                Offset = offset,
                Size = size,
            };
            return AsyncReadManager.Read(path, &cmd, 1);
        }

        // Mirrors UnityEngine.Experimental.Rendering.TextureCreationFlags but with the
        // additional flag values that exist in Unity's native code but aren't exposed in
        // the public managed enum. DontInitializePixels skips the zero-fill that the
        // normal Texture2D constructor performs — pointless work when we're about to
        // overwrite the bytes via LoadRawTextureData / AsyncReadManager / LoadImage.
        // Borrowed from KSPTextureLoader (../AsyncTextureLoad/src/KSPTextureLoader/TextureUtils.cs).
        [Flags]
        private enum InternalTextureCreationFlags
        {
            None = 0,
            MipChain = 1 << 0,
            DontInitializePixels = 1 << 2,
            DontDestroyTexture = 1 << 3,
            DontCreateSharedTextureData = 1 << 4,
            APIShareable = 1 << 5,
            Crunch = 1 << 6,
        }

        // Allocates a Texture2D without zeroing its pixel buffer. Equivalent to the
        // standard Texture2D constructor except for the DontInitializePixels flag,
        // which the public managed API doesn't expose for the TextureFormat overload.
        private static Texture2D CreateUninitializedTexture2D(
            int width,
            int height,
            TextureFormat format = TextureFormat.RGBA32,
            bool mipChain = false,
            bool linear = false,
            InternalTextureCreationFlags flags = InternalTextureCreationFlags.None)
        {
            if (GraphicsFormatUtility.IsCrunchFormat(format))
                flags |= InternalTextureCreationFlags.Crunch;
            int mipCount = !mipChain ? 1 : -1;
            return CreateUninitializedTexture2D(
                width, height, mipCount,
                GraphicsFormatUtility.GetGraphicsFormat(format, isSRGB: !linear),
                flags);
        }

        private static Texture2D CreateUninitializedTexture2D(
            int width,
            int height,
            int mipCount,
            GraphicsFormat format,
            InternalTextureCreationFlags flags = InternalTextureCreationFlags.None)
        {
            Texture2D tex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            if (!tex.ValidateFormat(GraphicsFormatUtility.GetTextureFormat(format)))
                return tex;

            flags |= InternalTextureCreationFlags.DontInitializePixels;
            if (mipCount != 1)
                flags |= InternalTextureCreationFlags.MipChain;

            Texture2D.Internal_Create(
                tex, width, height, mipCount, format,
                (TextureCreationFlags)flags, IntPtr.Zero);

            return tex;
        }

        // Wraps an inner format-specific coroutine with exception capture.
        // C# does not allow yield inside a try/catch, so we manually drive MoveNext() and
        // do the catch around just the MoveNext call. The driver detects completion via
        // req.Status, so no other signaling is required here.
        private static IEnumerator LoadTextureWrapperCoroutine(TextureLoadRequest req, IEnumerator inner)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext())
                        break;

                    current = inner.Current;
                }
                catch (Exception e)
                {
                    req.Exception = e;
                    req.ErrorMessage = $"{e.GetType().Name}: {e.Message}";
                    req.Status = TextureLoadRequest.State.Failed;
                    yield break;
                }

                yield return current;
            }

            if (req.Status != TextureLoadRequest.State.Pending)
                yield break;

            if (req.Result != null)
            {
                req.Status = TextureLoadRequest.State.Ready;
            }
            else
            {
                req.ErrorMessage ??= "Loader produced no result";
                req.Status = TextureLoadRequest.State.Failed;
            }
        }

        private static IEnumerator LoadDDSCoroutine(TextureLoadRequest req)
        {
            string path = req.File.fullPath;
            Task<DDSPreparedHeader> hdrTask = Task.Run(() =>
            {
                using var scope = s_pmParseDDSHeader.Auto();
                return ParseDDSHeader(path);
            });
            while (!hdrTask.IsCompleted)
                yield return null;
            if (hdrTask.IsFaulted)
                throw UnwrapFaultedTask(hdrTask, "DDS header parse failed");
            DDSPreparedHeader hdr = hdrTask.Result;
            req.FileLength = hdr.FileLength;

            if (!SupportedFormatCache.IsSupported(hdr.Format))
            {
                req.ErrorMessage = $"DDS: format '{hdr.Format}' is not supported by your GPU";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            Texture2D tex = CreateUninitializedTexture2D(
                hdr.Width, hdr.Height,
                hdr.MipChain ? -1 : 1,
                hdr.Format);
            if (tex.IsNullOrDestroyed())
            {
                req.ErrorMessage = "DDS: Texture2D allocation failed";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            // Wait until the texture is finished uploading so unity doesn't
            // copy its internal buffer when we call GetRawTextureData
            yield return WaitForGraphicsThread();

            NativeArray<byte> dst;
            using (s_pmGetRawDataDDS.Auto())
                dst = tex.GetRawTextureData<byte>();
            long expectedSize = dst.Length;
            if (hdr.FileLength - hdr.DataOffset < expectedSize)
            {
                UnityEngine.Object.Destroy(tex);
                req.ErrorMessage = $"DDS: file is too small for declared format (need {expectedSize} bytes after offset {hdr.DataOffset}, have {hdr.FileLength - hdr.DataOffset})";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            ReadHandle handle = BeginAsyncRead(path, dst, hdr.DataOffset, expectedSize);

            while (handle.Status == ReadStatus.InProgress)
                yield return null;

            ReadStatus status = handle.Status;
            handle.Dispose();

            if (status != ReadStatus.Complete)
            {
                UnityEngine.Object.Destroy(tex);
                req.ErrorMessage = $"DDS: AsyncReadManager.Read failed (status={status})";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            req.Result = new TextureInfo(req.File, tex, hdr.IsNormalMap, false, true);
            req.Status = TextureLoadRequest.State.Ready;
        }

        private static IEnumerator LoadUWRCoroutine(TextureLoadRequest req)
        {
            string filePath = req.File.fullPath;
            req.FileLength = new FileInfo(filePath).Length;
            string url = "file:///" + filePath.Replace('\\', '/');

            UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(url, nonReadable: false);
            try
            {
                yield return uwr.SendWebRequest();

                if (uwr.isNetworkError || uwr.isHttpError)
                {
                    req.ErrorMessage = $"UWR: {uwr.error}";
                    req.Status = TextureLoadRequest.State.Failed;
                    yield break;
                }

                Texture2D src = DownloadHandlerTexture.GetContent(uwr);
                if (src.IsNullOrDestroyed())
                {
                    req.ErrorMessage = "UWR: GetContent returned null";
                    req.Status = TextureLoadRequest.State.Failed;
                    yield break;
                }

                // Wait until the texture is finished uploading so unity doesn't
                // copy its internal buffer when we operate on it.
                yield return WaitForGraphicsThread();

                bool isNormalMap = req.File.name.EndsWith("NRM");
                bool canCompress = src.width % 4 == 0 && src.height % 4 == 0;

                // UWR returns a Texture2D with a mipchain already populated, so for normal
                // maps we swizzle every level of its CPU buffer in place — no dst alloc,
                // no copy, no Apply(true).
                if (isNormalMap)
                {
                    src.wrapMode = TextureWrapMode.Repeat;

                    NativeArray<byte> allLevels;
                    using (s_pmGetRawDataUWR.Auto())
                        allLevels = src.GetRawTextureData<byte>();
                    Task swizzleTask = Task.Run(() =>
                    {
                        using (s_pmSwizzleNormalMap.Auto())
                            SwizzleNormalMap(allLevels);
                    });
                    while (!swizzleTask.IsCompleted)
                        yield return null;
                    if (swizzleTask.IsFaulted)
                    {
                        UnityEngine.Object.Destroy(src);
                        throw UnwrapFaultedTask(swizzleTask, "swizzle task faulted");
                    }
                }

                if (canCompress)
                {
                    using (s_pmCompress.Auto())
                        src.Compress(highQuality: !isNormalMap);
                }
                else if (!isNormalMap)
                    Debug.LogWarning($"Texture '{req.File.url}' isn't eligible for DXT compression, width and height must be multiples of 4");

                src.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                bool isCompressed =
                    src.graphicsFormat == GraphicsFormat.RGBA_DXT5_UNorm
                    || src.graphicsFormat == GraphicsFormat.RGBA_DXT5_SRGB
                    || src.graphicsFormat == GraphicsFormat.RGBA_DXT1_UNorm
                    || src.graphicsFormat == GraphicsFormat.RGBA_DXT1_SRGB;
                req.Result = new TextureInfo(req.File, src, isNormalMap, isReadable: false, isCompressed: isCompressed);
                req.Status = TextureLoadRequest.State.Ready;
            }
            finally
            {
                uwr.Dispose();
            }
        }

        private static IEnumerator LoadTRUECOLORCoroutine(TextureLoadRequest req)
        {
            string path = req.File.fullPath;
            Task<long> sizeTask = Task.Run(() =>
            {
                using (s_pmFileSize.Auto())
                    return new FileInfo(path).Length;
            });
            while (!sizeTask.IsCompleted)
                yield return null;
            if (sizeTask.IsFaulted)
                throw UnwrapFaultedTask(sizeTask, "file size read failed");

            long len = sizeTask.Result;
            req.FileLength = len;
            if (len <= 0 || len > int.MaxValue)
            {
                req.ErrorMessage = $"TRUECOLOR: invalid file length {len}";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            NativeArray<byte> data = new NativeArray<byte>((int)len, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            ReadHandle handle = BeginAsyncRead(path, data, 0, len);
            while (handle.Status == ReadStatus.InProgress)
                yield return null;
            ReadStatus rs = handle.Status;
            handle.Dispose();

            if (rs != ReadStatus.Complete)
            {
                data.Dispose();
                req.ErrorMessage = $"TRUECOLOR: AsyncReadManager.Read failed (status={rs})";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            byte[] managed = data.ToArray();
            data.Dispose();

            // Create as RGBA32 with mipchain when this is a normal map: LoadImage will
            // populate every mip level for us, so we can swizzle the whole thing in place.
            // Non-normals keep the existing single-mip readable behavior.
            bool isNormalMap = req.File.name.EndsWith("NRM");
            Texture2D tex = CreateUninitializedTexture2D(2, 2, TextureFormat.RGBA32, mipChain: isNormalMap);
            if (!tex.LoadImage(managed, markNonReadable: false))
            {
                UnityEngine.Object.Destroy(tex);
                req.ErrorMessage = "TRUECOLOR: ImageConversion.LoadImage failed";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            if (isNormalMap)
            {
                bool isPot = Numerics.IsPowerOfTwo(tex.width) && Numerics.IsPowerOfTwo(tex.height);
                tex.wrapMode = TextureWrapMode.Repeat;

                // Wait until the texture is finished uploading so unity doesn't
                // copy its internal buffer when we call GetRawTextureData
                yield return WaitForGraphicsThread();

                NativeArray<byte> allLevels;
                using (s_pmGetRawDataTRUECOLOR.Auto())
                    allLevels = tex.GetRawTextureData<byte>();
                Task swizzleTask = Task.Run(() =>
                {
                    using (s_pmSwizzleNormalMap.Auto())
                        SwizzleNormalMap(allLevels);
                });
                while (!swizzleTask.IsCompleted)
                    yield return null;
                if (swizzleTask.IsFaulted)
                {
                    UnityEngine.Object.Destroy(tex);
                    throw UnwrapFaultedTask(swizzleTask, "swizzle task faulted");
                }

                if (isPot)
                {
                    using (s_pmCompress.Auto())
                        tex.Compress(highQuality: false);
                }
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                req.Result = new TextureInfo(req.File, tex, true, isReadable: false, isCompressed: isPot);
            }
            else
            {
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                req.Result = new TextureInfo(req.File, tex, false, isReadable: true, isCompressed: false);
            }
            req.Status = TextureLoadRequest.State.Ready;
        }

        private static IEnumerator LoadMBMCoroutine(TextureLoadRequest req)
        {
            string path = req.File.fullPath;
            Task<byte[]> readTask = Task.Run(() =>
            {
                using (s_pmReadAllBytes.Auto())
                    return File.ReadAllBytes(path);
            });
            while (!readTask.IsCompleted)
                yield return null;
            if (readTask.IsFaulted)
                throw UnwrapFaultedTask(readTask, "MBM file read failed");

            byte[] buffer = readTask.Result;
            req.FileLength = buffer.Length;

            Texture2D texture;
            bool isNormalMap;
            using (MemoryStream ms = new MemoryStream(buffer, 0, buffer.Length))
            using (BinaryReader br = new BinaryReader(ms))
            {
                texture = MBMReader.ReadTexture2D(buffer, br, true, true, out isNormalMap);
            }
            if (texture.IsNullOrDestroyed())
            {
                req.ErrorMessage = "MBM: ReadTexture2D failed";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            req.Result = new TextureInfo(req.File, texture, isNormalMap, isReadable: false, isCompressed: true);
            req.Status = TextureLoadRequest.State.Ready;
        }

        private static IEnumerator LoadTGACoroutine(TextureLoadRequest req)
        {
            string path = req.File.fullPath;
            Task<byte[]> readTask = Task.Run(() =>
            {
                using (s_pmReadAllBytes.Auto())
                    return File.ReadAllBytes(path);
            });
            while (!readTask.IsCompleted)
                yield return null;
            if (readTask.IsFaulted)
                throw UnwrapFaultedTask(readTask, "TGA file read failed");

            byte[] buffer = readTask.Result;
            req.FileLength = buffer.Length;
            if (buffer.Length < 18)
            {
                req.ErrorMessage = $"TGA invalid length of only {buffer.Length} bytes";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            TGAImage tgaImage = new TGAImage();
            TGAImage.header = new TGAHeader(buffer);
            TGAImage.colorData = tgaImage.ReadImage(TGAImage.header, buffer);
            if (TGAImage.colorData == null)
            {
                req.ErrorMessage = "TGA: ReadImage failed";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            Texture2D texture = tgaImage.CreateTexture(mipmap: true, linear: false, compress: true, compressHighQuality: true, allowRead: true);
            if (texture.IsNullOrDestroyed())
            {
                req.ErrorMessage = "TGA: CreateTexture failed";
                req.Status = TextureLoadRequest.State.Failed;
                yield break;
            }

            bool isNormalMap = req.File.name.EndsWith("NRM");
            if (isNormalMap)
            {
                bool isPot = Numerics.IsPowerOfTwo(texture.width) && Numerics.IsPowerOfTwo(texture.height);

                if (texture.format == TextureFormat.RGBA32)
                {
                    // tgaImage.CreateTexture(mipmap: true, ...) already calls Apply(true)
                    // and the texture is readable, so the CPU buffer holds every populated
                    // mip level. Swizzle the whole thing in place.
                    texture.wrapMode = TextureWrapMode.Repeat;

                    // Wait until the texture is finished uploading so unity doesn't
                    // copy its internal buffer when we call GetRawTextureData
                    yield return WaitForGraphicsThread();

                    NativeArray<byte> allLevels;
                    using (s_pmGetRawDataTGA.Auto())
                        allLevels = texture.GetRawTextureData<byte>();
                    Task swizzleTask = Task.Run(() =>
                    {
                        using (s_pmSwizzleNormalMap.Auto())
                            SwizzleNormalMap(allLevels);
                    });
                    while (!swizzleTask.IsCompleted)
                        yield return null;
                    if (swizzleTask.IsFaulted)
                    {
                        UnityEngine.Object.Destroy(texture);
                        throw UnwrapFaultedTask(swizzleTask, "swizzle task faulted");
                    }

                    if (isPot)
                    {
                        using (s_pmCompress.Auto())
                            texture.Compress(highQuality: false);
                    }
                    texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                }
                else
                {
                    // RGB24 (24bpp TGA): pixel size differs from RGBA32, so we can't
                    // swizzle in place. Fall back to the legacy src->dst expansion
                    // path. dst is allocated with a full mip chain so its byte layout
                    // matches the mipmapped src (CreateTexture(mipmap: true) populates
                    // every level), letting the swizzle fill dst end-to-end.
                    Texture2D dst = CreateUninitializedTexture2D(texture.width, texture.height, TextureFormat.RGBA32, mipChain: true);
                    dst.wrapMode = TextureWrapMode.Repeat;

                    yield return null;

                    NativeArray<byte> srcData;
                    NativeArray<byte> dstData;
                    using (s_pmGetRawDataTGA.Auto())
                    {
                        srcData = texture.GetRawTextureData<byte>();
                        dstData = dst.GetRawTextureData<byte>();
                    }

                    TextureFormat srcFormat = texture.format;
                    Task swizzleTask = Task.Run(() =>
                    {
                        using (s_pmSwizzleNormalMap.Auto())
                            SwizzleNormalMap(srcData, dstData, srcFormat);
                    });
                    while (!swizzleTask.IsCompleted)
                        yield return null;
                    if (swizzleTask.IsFaulted)
                    {
                        UnityEngine.Object.Destroy(texture);
                        UnityEngine.Object.Destroy(dst);
                        throw UnwrapFaultedTask(swizzleTask, "swizzle task faulted");
                    }
                    UnityEngine.Object.Destroy(texture);
                    texture = dst;

                    if (isPot)
                    {
                        using (s_pmCompress.Auto())
                            texture.Compress(highQuality: false);
                    }
                    texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                }
            }

            req.Result = new TextureInfo(req.File, texture, isNormalMap, isReadable: !isNormalMap, isCompressed: true);
            req.Status = TextureLoadRequest.State.Ready;
        }

        private static IEnumerator TextureDriverCoroutine(List<TextureLoadRequest> requests, HashSet<string> loadedUrls)
        {
            GameDatabase gdb = GameDatabase.Instance;
            Queue<TextureLoadRequest> active = new Queue<TextureLoadRequest>();
            int total = requests.Count;
            var iter = requests.GetEnumerator();
            int completed = 0;

            while (true)
            {
                while (active.TryPeek(out var pending))
                {
                    if (pending.Status == TextureLoadRequest.State.Pending)
                        break;

                    active.Dequeue();
                    InsertReadyRequest(pending, loadedUrls);
                    loadedAssetCount++;
                    completed++;
                }

                for (int i = 0; i < MaxTextureSpawnsPerFrame; ++i)
                {
                    if (!iter.MoveNext())
                        goto WINDDOWN;
                    var request = iter.Current;

                    gdb.StartCoroutine(LoadTextureCoroutine(request));
                    active.Enqueue(request);
                }

                gdb.progressFraction = (float)loadedAssetCount / totalAssetCount;
                gdb.progressTitle = $"Loading texture asset {completed}/{total}";
                yield return null;
            }

        WINDDOWN:
            while (active.TryDequeue(out var pending))
            {
                while (pending.Status == TextureLoadRequest.State.Pending)
                {
                    gdb.progressFraction = (float)loadedAssetCount / totalAssetCount;
                    gdb.progressTitle = $"Loading texture asset {completed}/{total}";
                    yield return null;
                }

                InsertReadyRequest(pending, loadedUrls);
                loadedAssetCount++;
                completed++;
            }
        }

        private static IEnumerator LoadTextureCoroutine(TextureLoadRequest req)
        {
            IEnumerator inner;
            switch (req.AssetType)
            {
                case RawAsset.AssetType.TextureDDS:
                    inner = LoadDDSCoroutine(req);
                    break;
                case RawAsset.AssetType.TexturePNG:
                case RawAsset.AssetType.TextureJPG:
                    inner = LoadUWRCoroutine(req);
                    break;
                case RawAsset.AssetType.TextureTRUECOLOR:
                    inner = LoadTRUECOLORCoroutine(req);
                    break;
                case RawAsset.AssetType.TextureMBM:
                    inner = LoadMBMCoroutine(req);
                    break;
                case RawAsset.AssetType.TextureTGA:
                    inner = LoadTGACoroutine(req);
                    break;
                default:
                    req.ErrorMessage = $"Unknown asset type {req.AssetType}";
                    req.Status = TextureLoadRequest.State.Failed;
                    yield break;
            }

            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext())
                        break;

                    current = inner.Current;
                }
                catch (Exception e)
                {
                    req.Exception = e;
                    req.ErrorMessage = $"{e.GetType().Name}: {e.Message}";
                    req.Status = TextureLoadRequest.State.Failed;
                    yield break;
                }

                yield return current;
            }

            if (req.Status != TextureLoadRequest.State.Pending)
                yield break;

            if (req.Result != null)
            {
                req.Status = TextureLoadRequest.State.Ready;
            }
            else
            {
                req.ErrorMessage ??= "Loader produced no result";
                req.Status = TextureLoadRequest.State.Failed;
            }
        }

        private static void SpawnTextureCoroutine(TextureLoadRequest req, Queue<TextureLoadRequest> active, GameDatabase gdb)
        {
            IEnumerator inner;
            switch (req.AssetType)
            {
                case RawAsset.AssetType.TextureDDS:
                    inner = LoadDDSCoroutine(req);
                    break;
                case RawAsset.AssetType.TexturePNG:
                case RawAsset.AssetType.TextureJPG:
                    inner = LoadUWRCoroutine(req);
                    break;
                case RawAsset.AssetType.TextureTRUECOLOR:
                    inner = LoadTRUECOLORCoroutine(req);
                    break;
                case RawAsset.AssetType.TextureMBM:
                    inner = LoadMBMCoroutine(req);
                    break;
                case RawAsset.AssetType.TextureTGA:
                    inner = LoadTGACoroutine(req);
                    break;
                default:
                    inner = null;
                    break;
            }

            if (inner == null)
            {
                req.ErrorMessage = $"Unknown asset type {req.AssetType}";
                req.Status = TextureLoadRequest.State.Failed;
            }
            else
            {
                Debug.Log($"Load Texture: {req.File.url}");
                gdb.StartCoroutine(LoadTextureWrapperCoroutine(req, inner));
            }
            active.Enqueue(req);
        }

        private static void InsertReadyRequest(TextureLoadRequest req, HashSet<string> loadedUrls)
        {
            Debug.Log($"Load Texture: {req.File.url}");

            if (req.Status == TextureLoadRequest.State.Failed)
            {
                Debug.LogWarning($"LOAD FAILED: {req.File.url}: {req.ErrorMessage}");
                if (req.Result != null && req.Result.texture.IsNotNullOrDestroyed())
                    UnityEngine.Object.Destroy(req.Result.texture);
                return;
            }

            if (!loadedUrls.Add(req.File.url))
            {
                Debug.LogWarning($"Duplicate texture asset '{req.File.url}' with extension '{req.File.fileExtension}' won't be loaded");
                if (req.Result != null && req.Result.texture.IsNotNullOrDestroyed())
                    UnityEngine.Object.Destroy(req.Result.texture);
                return;
            }

            req.Result.name = req.File.url;
            req.Result.texture.name = req.File.url;
            GameDatabase.Instance.databaseTexture.Add(req.Result);
            texturesByUrl[req.File.url] = req.Result;
            KSPCFFastLoaderReport.texturesBytesLoaded += req.FileLength;
            KSPCFFastLoaderReport.texturesLoaded++;
        }

        #endregion

        #region PartLoader reimplementation

        private static IEnumerable<CodeInstruction> PartLoader_StartLoad_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_PartLoader_CompileAll = AccessTools.Method(typeof(PartLoader), nameof(PartLoader.CompileAll));
            MethodInfo m_PartLoader_CompileAll_Modded = AccessTools.Method(typeof(KSPCFFastLoader), nameof(PartLoader_CompileAll));
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            bool valid = false;

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Call && ReferenceEquals(code[i].operand, m_PartLoader_CompileAll))
                {
                    code[i].operand = m_PartLoader_CompileAll_Modded;
                    for (int j = i - 1; j >= i - 4; j--)
                    {
                        if (code[j].opcode == OpCodes.Ldarg_0 && code[j - 1].opcode == OpCodes.Ldarg_0)
                        {
                            code[j].opcode = OpCodes.Nop;
                            valid = true;
                            break;
                        }
                    }
                    break;
                }
            }

            if (!valid)
                throw new Exception("PartLoader_StartLoad_Transpiler : transpiler patch failed");

            return code;
        }

        private static IEnumerator PartLoader_CompileAll()
        {
            PartLoader instance = PartLoader.Instance;

            if (instance._recompile)
            {
                instance.ClearAll();
            }
            instance.progressTitle = "";
            instance.progressFraction = 0f;
            KSPCFFastLoaderReport.wBuiltInPartsCopy.Restart();
            // copy the prebuilt parts (eva kerbals and flags) into the loaded part db
            for (int i = 0; i < instance.initialPartsLength; i++)
            {
                AvailablePart availablePart = new AvailablePart(instance.parts[i]);
                availablePart.partPrefab.gameObject.SetActive(value: false);
                availablePart.partPrefab = Instantiate(availablePart.partPrefab);
                availablePart.partPrefab.transform.parent = instance.transform;
                availablePart.partPrefab.gameObject.SetActive(value: false);
                if (availablePart.partPrefab.fxGroups != null)
                {
                    for (int j = 0; j < availablePart.partPrefab.fxGroups.Count; j++)
                    {
                        if (availablePart.partPrefab.fxGroups[j].maxVisualPower == 0f)
                        {
                            availablePart.partPrefab.fxGroups[j].maxVisualPower = 1f;
                        }
                    }
                }
                if ((bool)FlightGlobals.fetch)
                {
                    FlightGlobals.PersistentLoadedPartIds.Remove(availablePart.partPrefab.persistentId);
                }
                if (availablePart.iconPrefab != null)
                {
                    availablePart.iconPrefab = Instantiate(availablePart.iconPrefab);
                    availablePart.iconPrefab.transform.parent = instance.transform;
                    availablePart.iconPrefab.name = availablePart.partPrefab.name + " icon";
                    availablePart.iconPrefab.gameObject.SetActive(value: false);
                }
                instance.loadedParts.Add(availablePart);
            }
            KSPCFFastLoaderReport.wBuiltInPartsCopy.Stop();
            KSPCFFastLoaderReport.wPartConfigExtraction.Restart();
            UrlConfig[] configs = GameDatabase.Instance.GetConfigs("PART");
            UrlConfig[] allPropNodes = GameDatabase.Instance.GetConfigs("PROP");
            UrlConfig[] allSpaceNodes = GameDatabase.Instance.GetConfigs("INTERNAL");
            UrlConfig[] configs2 = GameDatabase.Instance.GetConfigs("VARIANTTHEME");
            KSPCFFastLoaderReport.wPartConfigExtraction.Stop();
            int num = configs.Length + allPropNodes.Length + allSpaceNodes.Length;
            instance.progressDelta = 1f / num;
            instance.InitializePartDatabase();
            instance.APFinderByIcon.Clear();
            instance.APFinderByName.Clear();
            instance.CompileVariantThemes(configs2);

            KSPCFFastLoaderReport.wPartCompilationLoading.Restart();
            PartCompilationInProgress = true;
            IEnumerator compilePartsEnumerator = FrameUnlockedCoroutine(instance.CompileParts(configs));
            while (compilePartsEnumerator.MoveNext())
                yield return null;
            PartCompilationInProgress = false;
            KSPCFFastLoaderReport.wPartCompilationLoading.Stop();

            KSPCFFastLoaderReport.wInternalCompilationLoading.Restart();
            IEnumerator compileInternalPropsEnumerator = FrameUnlockedCoroutine(instance.CompileInternalProps(allPropNodes));
            while (compileInternalPropsEnumerator.MoveNext())
                yield return null;

            IEnumerator compileInternalSpacesEnumerator = FrameUnlockedCoroutine(instance.CompileInternalSpaces(allSpaceNodes));
            while (compileInternalSpacesEnumerator.MoveNext())
                yield return null;
            KSPCFFastLoaderReport.wInternalCompilationLoading.Stop();

            Destroy(loader);

            instance.SavePartDatabase();

            instance._recompile = false;
            PartUpgradeManager.Handler.LinkUpgrades();
            GameEvents.OnUpgradesLinked.Fire();
            instance.isReady = true;
            GameEvents.OnPartLoaderLoaded.Fire();
        }

        #endregion

        #region PartLoader Coroutine patcher infrastructure

        /// <summary>
        /// Patch all "yield StartCoroutine()" calls in the compiler generated MoveNext() method of a coroutine. The StartCoroutine() call will 
        /// be replaced by a pass-through method returning the IEnumerator, which mean it will be yielded. This allow to manually iterate over
        /// a coroutine, even if that coroutine has nested StartCoroutine() calls.
        /// </summary>
        private static void PatchStartCoroutineInCoroutine(MethodInfo coroutine)
        {
            MethodInfo t_StartCoroutinePassThroughTranspiler = AccessTools.Method(typeof(KSPCFFastLoader), nameof(StartCoroutinePassThroughTranspiler));
            assetAndPartLoaderHarmony.Patch(AccessTools.EnumeratorMoveNext(coroutine), null, null, new HarmonyMethod(t_StartCoroutinePassThroughTranspiler));
        }

        /// <summary>
        /// Transpiler for the PatchStartCoroutineInCoroutine() method.
        /// </summary>
        private static IEnumerable<CodeInstruction> StartCoroutinePassThroughTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_StartCoroutine = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new[] { typeof(IEnumerator) });
            MethodInfo m_StartCoroutinePassThrough = AccessTools.Method(typeof(KSPCFFastLoader), nameof(StartCoroutinePassThrough));

            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Call && ReferenceEquals(code[i].operand, m_StartCoroutine))
                {
                    code[i].operand = m_StartCoroutinePassThrough;
                }
            }

            return code;
        }

        /// <summary>
        /// Pass-through replacement method for StartCoroutine()
        /// </summary>
        /// <remarks>
        /// The unused instance param is there so we match the original StartCoroutine() method signature
        /// </remarks>
        static object StartCoroutinePassThrough(object instance, IEnumerator enumerator)
        {
            return enumerator;
        }

        /// <summary>
        /// Reimplementation of StartCoroutine supporting nested yield StartCoroutine() calls patched with PatchStartCoroutineInCoroutine()
        /// and yielding null only after a fixed amount of time elapsed
        /// </summary>
        static IEnumerator FrameUnlockedCoroutine(IEnumerator coroutine)
        {
            LoaderExceptionInfo exceptionInfo = null;
            float nextFrameTime = Time.realtimeSinceStartup + minFrameTime;

            Stack<IEnumerator> enumerators = new Stack<IEnumerator>();
            enumerators.Push(coroutine);



            while (enumerators.TryPop(out IEnumerator currentEnumerator))
            {
                bool moveNext;

                try
                {
                    moveNext = currentEnumerator.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);

                    if (currentEnumerator == coroutine)
                    {
                        exceptionInfo = new LoaderExceptionInfo(e, coroutine);
                        moveNext = false;
                    }
                    else
                    {
                        enumerators.Clear();
                        enumerators.Push(coroutine);
                        continue;
                    }
                }

                while (moveNext)
                {
                    if (frameSkipRequested || Time.realtimeSinceStartup > nextFrameTime)
                    {
                        frameSkipRequested = false;
                        nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
                        yield return null;
                    }

                    if (currentEnumerator.Current is IEnumerator nestedCoroutine)
                    {
                        enumerators.Push(currentEnumerator);
                        currentEnumerator = nestedCoroutine;
                        continue;
                    }

                    try
                    {
                        moveNext = currentEnumerator.MoveNext();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);

                        if (currentEnumerator == coroutine)
                        {
                            exceptionInfo = new LoaderExceptionInfo(e, coroutine);
                            moveNext = false;
                        }
                        else
                        {
                            enumerators.Clear();
                            enumerators.Push(coroutine);
                        }
                    }
                }
            }

            if (exceptionInfo != null)
            {
                exceptionInfo.Show();
                while (true)
                {
                    Thread.Sleep(10);
                    yield return null;
                }
            }
        }

        // Fix for issue #114 : Drag cubes are incorrectly calculated with KSPCF 1.24.1 
        private static bool frameSkipRequested;
        public static void RequestFrameSkip() => frameSkipRequested = true;

        public static bool PartCompilationInProgress;

        private static IEnumerable<CodeInstruction> DragCubeSystem_RenderDragCubes_MoveNext_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            MethodInfo m_IMultipleDragCube_AssumeDragCubePosition = AccessTools.Method(typeof(IMultipleDragCube), nameof(IMultipleDragCube.AssumeDragCubePosition));
            MethodInfo m_KSPCFFastLoader_RequestFrameSkip = AccessTools.Method(typeof(KSPCFFastLoader), nameof(RequestFrameSkip));

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Callvirt && ReferenceEquals(code[i].operand, m_IMultipleDragCube_AssumeDragCubePosition))
                {
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, m_KSPCFFastLoader_RequestFrameSkip));
                    break;
                }
            }

            return code;
        }

        #endregion

        #region Asset bundle preloading
        static List<AssetBundleCreateRequest> AssetBundleRequestCache = null;

        static void PreloadAssetBundleObjects(GameDatabase gdb)
        {
            Profiler.BeginSample("FastLoader.PreloadAssetBundleObjects");
            KSPCFFastLoaderReport.wAssetBundleLoading.Start();

            Debug.Log("Preloading Asset Bundle Definitions");

            Profiler.BeginSample("FastLoader.LoadAssetBlacklist");
            gdb.LoadAssetBlacklist();
            Profiler.EndSample();

            PreloadAssetDefinitions();

            KSPCFFastLoaderReport.wAssetBundleLoading.Stop();
            Profiler.EndSample();
        }

        static void PreloadAssetDefinitions()
        {
            Profiler.BeginSample("FastLoader.PreloadAssetDefinitions");

            var loader = AssetLoader.Instance;
            var assetDirectory = AssetLoader.CreateApplicationPath(loader.assetDirectory);
            var assetBlacklist = new HashSet<string>(loader.assetBlacklist);

            loader.coreAndAutoloadDefinitions = new List<BundleDefinition>();
            loader.ready = false;

            var coreDir = new DirectoryInfo(Path.Combine(assetDirectory, loader.coreDirectory));
            var assetDir = new DirectoryInfo(assetDirectory);
            var glob = "*." + loader.assetExtension;

            var files = Enumerable.Repeat(coreDir, 1)
                .Concat(
                    assetDir
                        .EnumerateDirectories()
                        .Where(dir => dir.Name != loader.coreDirectory)
                )
                .AsParallel()
                .AsOrdered()
                .SelectMany(dir => dir.GetFiles(glob, SearchOption.AllDirectories))
                .Where(file => !assetBlacklist.Contains(file.Name))
                .AsSequential();

            loader.allFilesList = new List<FileInfo>();
            AssetBundleRequestCache = new List<AssetBundleCreateRequest>();

            var seen = new HashSet<string>();
            var requestCache = AssetBundleRequestCache;
            foreach (var assetFile in files)
            {
                // We don't need to check for duplicates here because the files
                // enumerator avoids them by construction.

                loader.allFilesList.Add(assetFile);

                // Some asset bundles have the same name. We can't load those
                // concurrently so we just keep a null request and load them
                // as we encounter them later on.
                if (seen.Contains(assetFile.Name))
                {
                    requestCache.Add(null);
                }
                else
                {
                    // Debug.Log($"AssetLoader: Preloading bundle {path}");
                    seen.Add(assetFile.Name);
                    requestCache.Add(AssetBundle.LoadFromFileAsync(assetFile.FullName));
                }
            }

            Profiler.EndSample();
        }

        static IEnumerator LoadAssetDefinitionsAsync(AssetLoader loader)
        {
            Debug.Log("AssetLoader: Loading bundle definitions");
            var files = loader.allFilesList;
            var requestCache = AssetBundleRequestCache;
            AssetBundleRequestCache = null;

            // Keep track of which bundles could not be preloaded and start
            // loading them as soon as the conflicting bundle has been unloaded.
            var missing = new Dictionary<string, int>();
            for (int i = 0; i < files.Count; ++i)
            {
                var assetFile = files[i];
                var request = requestCache[i];

                if (!(request is null))
                    continue;

                // Make sure to only track the first index, in case there are
                // even more bundles with the same name.
                if (missing.ContainsKey(assetFile.Name))
                    continue;
                missing.Add(assetFile.Name, i);
            }

            for (int i = 0; i < files.Count; ++i)
            {
                var assetFile = files[i];
                var request = requestCache[i];

                AssetBundle bundle;
                if (request is null)
                {
                    // Some bundles can't be preloaded because they share the same
                    // name with a pre-existing asset bundle. In that case we just
                    // load them now.
                    bundle = AssetBundle.LoadFromFile(assetFile.FullName);
                }
                else
                {
                    if (!request.isDone)
                        yield return request;

                    bundle = request.assetBundle;
                }

                if (bundle == null)
                {
                    Debug.LogError("AssetLoader: Bundle is null");
                    continue;
                }

                BundleDefinition bundleDefinition = null;
                string[] assetNames = bundle.GetAllAssetNames();
                foreach (string name in assetNames)
                {
                    if (name.EndsWith(loader.assetDefinitionSuffix))
                    {
                        var asset = bundle.LoadAsset<TextAsset>(name);
                        var bundleDef = BundleDefinition.CreateFromText(asset.text);
                        if (bundleDef != null)
                            bundleDefinition = bundleDef;
                    }
                    else if (name.EndsWith(loader.bundleDependencySuffix))
                    {
                        string platform = Application.platform == RuntimePlatform.LinuxPlayer
                            ? "linux"
                            : "windows";

                        if (!name.Contains(platform))
                            continue;

                        var asset = bundle.LoadAsset<TextAsset>(name);
                        var bundleName = Path.GetFileNameWithoutExtension(name);
                        string savePath = Path.Combine(Path.GetDirectoryName(assetFile.FullName), bundleName.Remove(bundleName.IndexOf('_')) + ".ksp");

                        using (var fs = File.Open(savePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                        {
                            fs.SetLength(0L);
                            fs.Write(asset.bytes, 0, asset.bytes.Length);
                            fs.Close();
                        }

                        var depBundleFile = new FileInfo(savePath);
                        if (!files.Contains(depBundleFile, new AssetLoader.FileComparer()))
                        {
                            files.Add(depBundleFile);
                            requestCache.Add(AssetBundle.LoadFromFileAsync(depBundleFile.FullName));
                        }
                    }
                }

                bundle.Unload(unloadAllLoadedObjects: true);

                if (bundleDefinition != null)
                {
                    if (bundleDefinition.autoLoad || (!bundleDefinition.name.ToLower().StartsWith("kspedia_") && bundleDefinition.name.ToLower().Contains("core")))
                    {
                        bundleDefinition.path = assetFile.FullName;
                        loader.coreAndAutoloadDefinitions.Add(bundleDefinition);
                        loader.amountAutoLoadBundles++;
                        Debug.Log("AssetLoader: Loaded bundle '" + bundleDefinition.name + "'");
                    }
                    else if (!bundleDefinition.autoLoad && !bundleDefinition.name.ToLower().Contains("core"))
                    {
                        bundleDefinition.path = assetFile.FullName;
                        loader.coreAndAutoloadDefinitions.Add(bundleDefinition);
                        loader.amountAutoLoadBundles++;
                        Debug.Log("AssetLoader: Loaded mod bundle '" + bundleDefinition.name + "'");
                    }
                }
                else
                {
                    bundleDefinition = new BundleDefinition
                    {
                        name = assetFile.Name,
                        path = assetFile.FullName
                    };
                }

                // If we were blocking the load of another bundle then start that now.
                if (missing.TryGetValue(assetFile.Name, out var index))
                {
                    missing.Remove(assetFile.Name);
                    requestCache[index] = AssetBundle.LoadFromFileAsync(files[index].FullName);
                }
            }

            loader.CompileBundleDefinitions();
            loader.CreateAssetDefinitionList();
            Debug.Log("AssetLoader: Finished loading. " + loader.coreAndAutoloadDefinitions.Count + " bundle definitions loaded.");
            loader.ready = true;
        }

        static IEnumerable<CodeInstruction> GameDatabase_LoadAssetBundleObjects_MoveNext_Transpiler(
            IEnumerable<CodeInstruction> instructions
        )
        {
            // We want to avoid repeating the work we did in the preload stage:
            // - strip out the call to LoadAssetBlacklist
            // - replace the call to LoadDefinitionsAsync with a custom version
            //   that uses the bundle load requests we have already made, among
            //   other optimizations

            var loadAssetBlacklistMethod = SymbolExtensions.GetMethodInfo((GameDatabase gdb) => gdb.LoadAssetBlacklist());
            var loadDefinitionsAsyncMethod = SymbolExtensions.GetMethodInfo((AssetLoader l) => l.LoadDefinitionsAsync());

            var matcher = new CodeMatcher(instructions);
            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Call, loadAssetBlacklistMethod))
                .ThrowIfInvalid("Unable to find call to LoadAssetBlacklist")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Pop))
                .MatchStartForward(new CodeMatch(OpCodes.Callvirt, loadDefinitionsAsyncMethod))
                .ThrowIfInvalid("Unable to find call to LoadDefinitionAsync")
                .Set(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => LoadAssetDefinitionsAsync(null)));

            return matcher.Instructions();
        }
        #endregion

        #region User opt-in popup (vestigial)

        // The popup, opt-in flow, and PNG-cache-size estimator below are intentionally
        // kept around — the cache they were originally tied to has been removed, but the
        // popup is going to be repurposed for an upcoming feature. Nothing currently
        // triggers WaitForUserOptIn; it must be invoked explicitly by the new feature.

        private static IEnumerator WaitForUserOptIn()
        {
            long cacheSize = 0;
            long normalsSize = 0;
            int textureCount = 0;
            foreach (UrlFile textureFile in Instance.root.GetFiles(FileType.Texture))
            {
                if (string.Equals(Path.GetExtension(textureFile.fullPath), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    if (GetPngCacheSize(textureFile.fullPath, out int size, out bool isNormal))
                    {
                        textureCount++;
                        cacheSize += size;
                        if (isNormal)
                            normalsSize += size;
                    }
                }
            }

            // 4s for 1350 textures
            double timePerTexture = 4.0 / 1350.0;

            // 4s for 100MB of cached textures
            double timePerByte = 4.0 / 100.0 / 1024.0 / 1024.0;

            // 30s for 130MB of cached normal maps 
            double timePerNormalByte = 30.0 / 130.0 / 1024.0 / 1024.0;

            double loadingTimeReduction = (textureCount * timePerTexture) + (cacheSize * timePerByte) + (normalsSize * timePerNormalByte);

            string desc =
                "<size=120%><color=\"white\">" +
                LOC_PopupL1 + "\n\n" +
                Localizer.Format(LOC_F_PopupL2, loadingTimeReduction.ToString("F0")) + "\n\n" +
                Localizer.Format(LOC_F_PopupL3, (cacheSize / 1024.0 / 1024.0).ToString("F0")) + "\n\n" +
                LOC_PopupL4 + "\n\n" +
                "<align=\"center\">" + LOC_PopupL5 + "\n";

            string cacheSizeMb = (cacheSize / 1024.0 / 1024.0).ToString("F0") + "Mb";
            bool? choosed = null;
            bool dismissed = false;
            MultiOptionDialog dialog = new MultiOptionDialog("TextureLoaderOptimizations",
                desc,
                KSPCommunityFixes.LOC_KSPCF_Title,
                HighLogic.UISkin, 350f,
                new DialogGUIButton(Localizer.Format("#autoLOC_439839"), delegate { SetOptIn(true, ref choosed); }), // yes
                new DialogGUIButton(Localizer.Format("#autoLOC_439840"), delegate { SetOptIn(false, ref choosed); })); // no
            PopupDialog popup = PopupDialog.SpawnPopupDialog(dialog, false, HighLogic.UISkin, false);
            popup.OnDismiss = () => dismissed = true;

            while (choosed == null)
            {
                // prevent the user being able to skip choosing by "ESC closing" the dialog
                if (dismissed)
                {
                    yield return Instance.StartCoroutine(WaitForUserOptIn());
                    yield break;
                }

                yield return null;
            }
        }

        private static void SetOptIn(bool optIn, ref bool? choosed)
        {
            loader.userOptInChoiceDone = true;
            textureCacheEnabled = optIn;
            choosed = true;

            ConfigNode config = new ConfigNode();
            config.AddValue(nameof(userOptInChoiceDone), true);
            config.AddValue(nameof(textureCacheEnabled), optIn);

            string pluginDataPath = Path.Combine(ModPath, "PluginData");
            if (!Directory.Exists(pluginDataPath))
                Directory.CreateDirectory(pluginDataPath);

            config.Save(ConfigPath);
        }

        private static readonly string flagsPath = Path.DirectorySeparatorChar + "Flags" + Path.DirectorySeparatorChar;

        private static bool GetPngCacheSize(string path, out int cacheSize, out bool isNormal)
        {
            isNormal = false;
            cacheSize = 0;

            if (!GetPngSize(path, out uint width, out uint height))
                return false;

            if (width % 4 != 0 || height % 4 != 0)
                return false;

            cacheSize = (int)(width * height);

            isNormal = Path.GetFileNameWithoutExtension(path).EndsWith("NRM");

            // if has mipmaps, about 30% larger file size
            if (isNormal || path.Contains(flagsPath))
                cacheSize = (int)(cacheSize * 1.3);

            return true;
        }

        #endregion

        #region Utility

        private static int GetDefaultMipMapCount(int height, int width)
        {
            return 1 + (int)(Math.Floor(Math.Log(Math.Max(width, height), 2.0)));
        }

        private static bool GetPNGSize(byte[] pngData, out uint width, out uint height)
        {
            width = height = 0;

            if (pngData.Length < 24)
                return false;

            // validate PNG magic bytes
            if (pngData[0] != 137
                || pngData[1] != 80
                || pngData[2] != 78
                || pngData[3] != 71
                || pngData[4] != 13
                || pngData[5] != 10
                || pngData[6] != 26
                || pngData[7] != 10)
                return false;

            // validate IHDR chunk length (always 13)
            if (pngData[11] != 13)
                return false;

            // validate chunk name ("IHDR")
            if (pngData[12] != 73
                || pngData[13] != 72
                || pngData[14] != 68
                || pngData[15] != 82)
                return false;

            // width and height are big-endian encoded unsigned ints
            width = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(pngData, 16, 4));
            height = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(pngData, 20, 4));
            return true;
        }

        private static bool GetPngSize(string path, out uint width, out uint height)
        {
            BinaryReader binaryReader = null;
            try
            {
                binaryReader = new BinaryReader(File.OpenRead(path));
                byte[] header = binaryReader.ReadBytes(24);
                return GetPNGSize(header, out width, out height);
            }
            catch
            {
                width = height = 0;
                return false;
            }
            finally
            {
                binaryReader?.Dispose();
            }
        }

        private class LoaderExceptionInfo
        {
            private string message;
            private string stackTrace;
            private string loader;
            private string origin;

            public LoaderExceptionInfo(Exception e, IEnumerator rootEnumerator)
            {
                message = $"{e.GetType()}: {e.Message}";
                stackTrace = e.StackTrace;

                string enumeratorTypeName = rootEnumerator.GetType().Name;

                try
                {
                    if (enumeratorTypeName.Contains(nameof(PartLoader.CompileParts)))
                    {
                        FieldInfo apField = rootEnumerator.GetType().GetFields(AccessTools.all).FirstOrDefault(p => p.FieldType == typeof(AvailablePart));
                        if (apField != null)
                        {
                            loader = "Part compilation";
                            origin = "Part";
                            AvailablePart ap = (AvailablePart)apField.GetValue(rootEnumerator);
                            if (ap != null)
                            {
                                origin += ": ";
                                if (ap.title != null)
                                    origin += ap.title;

                                if (ap.partUrl != null)
                                    origin += $" ({ap.partUrl})";
                            }
                        }
                    }
                    else if (enumeratorTypeName.Contains(nameof(PartLoader.CompileInternalProps)))
                    {
                        loader = "Internal props compilation";
                        FieldInfo[] fields = rootEnumerator.GetType().GetFields(AccessTools.all);
                        FieldInfo allPropNodesField = fields.FirstOrDefault(p => p.FieldType == typeof(UrlConfig[]));
                        FieldInfo indexField = fields.FirstOrDefault(p => p.Name.Contains("<i>"));
                        UrlConfig[] allPropNodes = allPropNodesField?.GetValue(rootEnumerator) as UrlConfig[];

                        if (indexField != null && allPropNodes != null)
                        {
                            int index = (int)indexField.GetValue(rootEnumerator);
                            if (index >= 0 && index < allPropNodes.Length)
                                origin = $"Prop: {allPropNodes[index].url}";
                        }
                    }
                    else if (enumeratorTypeName.Contains(nameof(PartLoader.CompileInternalSpaces)))
                    {
                        loader = "Internal spaces compilation";
                        FieldInfo[] fields = rootEnumerator.GetType().GetFields(AccessTools.all);
                        FieldInfo allSpaceNodesField = fields.FirstOrDefault(p => p.FieldType == typeof(UrlConfig[]));
                        FieldInfo indexField = fields.FirstOrDefault(p => p.Name.Contains("<i>"));
                        UrlConfig[] allSpaceNodes = allSpaceNodesField?.GetValue(rootEnumerator) as UrlConfig[];

                        if (indexField != null && allSpaceNodes != null)
                        {
                            int index = (int)indexField.GetValue(rootEnumerator);
                            if (index >= 0 && index < allSpaceNodes.Length)
                                origin = $"Space: {allSpaceNodes[index].url}";
                        }
                    }
                }
                catch { }
            }

            public void Show()
            {
                string content = "Loading has failed due to an unhandled error\n\n";
                if (loader != null)
                    content += $"Failure in subsystem : {loader}\n";
                if (origin != null)
                    content += $"{origin}\n";

                content += $"\n{message}\n{stackTrace}";

                DialogGUITextInput input = new DialogGUITextInput(content, true, int.MaxValue, s => s, () => content, TMP_InputField.ContentType.Standard);

                MultiOptionDialog dialog = new MultiOptionDialog("loadingFailed",
                    string.Empty,
                    "Loading failed",
                    HighLogic.UISkin, 600f,
                    input,
                    new DialogGUIHorizontalLayout(true, false,
                    new DialogGUIButton("Copy to clipboard", () => GUIUtility.systemCopyBuffer = content, false),
                    new DialogGUIButton("Quit", Application.Quit)));
                PopupDialog.SpawnPopupDialog(dialog, true, HighLogic.UISkin);
                input.field.textComponent.enableWordWrapping = false;
                input.field.textComponent.overflowMode = TextOverflowModes.Overflow;
                input.uiItem.GetComponent<LayoutElement>().minHeight = input.field.textComponent.GetPreferredHeight() + 15f;
            }
        }

        // A helper that yields until it has been processed on the render thread.
        // Use this to delay until the render thread is no longer using a texture
        // (or any other resource).
        private unsafe class WaitForGraphicsThreadInst : CustomYieldInstruction
        {
            static CommandBuffer DispatchCB;
            static readonly IntPtr NotifyPtr = (IntPtr)Marshal.GetFunctionPointerForDelegate((Action<int, IntPtr>)Notify);
            static readonly int GchandleOffset = UnsafeUtility.GetFieldOffset(
                typeof(WaitForGraphicsThreadInst).GetField(nameof(gchandle), BindingFlags.Instance | BindingFlags.NonPublic));
            static readonly int ReadyOffset = UnsafeUtility.GetFieldOffset(
                typeof(WaitForGraphicsThreadInst).GetField(nameof(ready), BindingFlags.Instance | BindingFlags.NonPublic));

            ulong gchandle = 0;
            bool ready = false;

            public override bool keepWaiting => !ready;

            public WaitForGraphicsThreadInst()
            {
                DispatchCB ??= new CommandBuffer()
                {
                    name = "KSPCF.WaitForGraphicsThreadCB"
                };

                void* addr = UnsafeUtility.PinGCObjectAndGetAddress(this, out gchandle);
                try
                {
                    DispatchCB.Clear();
                    DispatchCB.IssuePluginEventAndData(NotifyPtr, 0, (IntPtr)addr);
                    Graphics.ExecuteCommandBuffer(DispatchCB);
                }
                catch
                {
                    UnsafeUtility.ReleaseGCObject(gchandle);
                    throw;
                }
            }

            static void Notify(int _, IntPtr data)
            {
                ulong gchandle = *(ulong*)((byte*)data + GchandleOffset);
                bool* ready = (bool*)((byte*)data + ReadyOffset);

                *ready = true;
                UnsafeUtility.ReleaseGCObject(gchandle);
            }
        }

        private static WaitForGraphicsThreadInst WaitForGraphicsThread() =>
            new WaitForGraphicsThreadInst();

        #endregion


    }

#if DEBUG
    public class GetInfoThrowModule : PartModule
    {
        public override string GetInfo()
        {
            // this should be fatal an stop the loading process
            throw new Exception("Exception from GetInfo");
        }
    }

    public class AssumeDragCubePositionThrowModule : PartModule, IMultipleDragCube
    {
        public string[] GetDragCubeNames() => new[] { "A", "B" };

        // this shouldn't be fatal and shouldn't stop the loading process
        public void AssumeDragCubePosition(string name)
        {
            throw new Exception("Exception from AssumeDragCubePosition");
        }

        public bool UsesProceduralDragCubes() => false;

        public bool IsMultipleCubesActive => true;
    }
#endif
}
