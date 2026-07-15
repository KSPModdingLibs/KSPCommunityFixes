// #define DEBUG_TEXTURE_CACHE
// #define DEBUG_MODEL_LOAD_ORDER

using DDSHeaders;
using Expansions;
using HarmonyLib;
using KSP.Localization;
using KSPAssets;
using KSPAssets.Loaders;
using KSPCommunityFixes.Library.Buffers;
using KSPCommunityFixes.Library.Collections;
using KSPCommunityFixes.Library.Model;
using KSPCommunityFixes.Library.TextureBundle;
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
using System.Collections.Concurrent;

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
        private const int MaxTextureSpawnsPerFrame = 64;

        // Max number of new model load coroutines that will be spawned each frame.
        // This roughly limits the max frame time spent replaying models / loading their meshes.
        private const int MaxModelSpawnsPerFrame = 12;

        // v1 tuning knob: cap on native mesh-bundle bytes resident at once. The pump waits before kicking off a
        // group's LoadFromMemoryAsync while at least this many bytes are already resident, so the driver can
        // Unload earlier groups first. Restores the old streaming loader's ~50 MB-capped bounded-memory
        // behavior (regression guard vs loading every group's native copy up front).
        private const long MaxResidentModelBundleBytes = 96L * 1024 * 1024; // 96 MB

        private const int ModelGroupQueueCapacity = 4;  // groups the compile task may run ahead of the pump; bounds managed bundle-byte pressure (tuning knob)

        // Native mesh-bundle bytes currently resident: the pump ADDS a group's size when it kicks off the
        // load, the driver SUBTRACTS it on Unload. Both the pump and the driver are main-thread Unity
        // coroutines that never run concurrently, so this plain field needs no lock. Reset when the model
        // pipeline is kicked off.
        private static long residentModelBundleBytes;

        // Set by CompileModelGroups' last-resort outer catch on the background Task thread; logged ONCE on the
        // main thread by ModelDriverCoroutine at termination. NEVER Debug.* from the Task thread (the mod
        // handlers chained onto Application.logMessageReceived aren't thread-safe).
        private static volatile Exception modelCompileFault;

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

            // If the user hasn't chosen yet then wati for the opt-in
            if (!loader.userOptInChoiceDone)
            {
                gdb.progressTitle = "Waiting for texture cache opt-in...";
                yield return gdb.StartCoroutine(WaitForUserOptIn());
            }

            gdb.progressTitle = "Searching assets to load...";
            yield return null;

            KSPCFFastLoaderReport.wAssetsLoading.Restart();
            double nextFrameTime = ElapsedTime + minFrameTimeD;

            // Files loaded by our custom loaders
            List<UrlFile> audioFiles = new(1000);
            // Textures that need to be loaded on the main thread go through here.
            BlockingCollection<TextureLoadRequest> textureQueue = [];
            List<TextureLoadRequest> bundleRequests = new(10000);
            List<RawAsset> modelAssets = new(5000);

            // Files loaded by mod-defined loaders (ex : Shabby *.shab files)
            List<UrlFile> unsupportedAudioFiles = new(100);
            List<UrlFile> unsupportedModelFiles = new(100);

            // Keeping track of already loaded files to avoid loading duplicates.
            // Note that to replicate stock behavior, we can't populate those
            // directly, we have to ensure a file is actually loaded without errors
            // before flaging a same-url file as duplicate. Not doing this can break
            // mods relying on that implementation detail, looking at you, Shabby
            // and ConformalDecals
            HashSet<string> allAudioFiles = new(1000);
            HashSet<string> allTextureFiles = new(10000);
            HashSet<string> allModelFiles = new(5000);

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
                                    bundleRequests.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureDDS));
                                    break;
                                case "jpg":
                                case "jpeg":
                                    textureQueue.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureJPG));
                                    break;
                                case "mbm":
                                    textureQueue.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureMBM));
                                    break;
                                case "png":
                                    bundleRequests.Add(new TextureLoadRequest(file, RawAsset.AssetType.TexturePNG));
                                    break;
                                case "tga":
                                    textureQueue.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureTGA));
                                    break;
                                case "truecolor":
                                    textureQueue.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureTRUECOLOR));
                                    break;
                                default:
                                    textureQueue.Add(new TextureLoadRequest(file, RawAsset.AssetType.TextureCUSTOM));
                                    break;
                            }
                            break;
                        case FileType.Model:
                            switch (file.fileExtension)
                            {
                                case "mu":
                                    modelAssets.Add(new RawAsset(file));
                                    break;
                                case "dae":
                                case "DAE":
                                    modelAssets.Add(new RawAsset(file));
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

            SupportedFormatCache.Build();

            // Tune the AUP for much better throughput
            QualitySettings.asyncUploadTimeSlice = 25;
            QualitySettings.asyncUploadBufferSize = 256;

            int textureCount = bundleRequests.Count + textureQueue.Count;

            // Kick off the background bundle build
            BundleState bundleState = new();
            gdb.StartCoroutine(LoadBundledAssets(bundleState, bundleRequests, textureQueue));

            // Kick off the background model compile + bundle pump NOW so it overlaps texture loading.
            // CompileModelGroups classifies/compiles every .mu off-thread and folds the ordered results into
            // count-capped ModelGroups; ModelBundlePumpCoroutine kicks off each group's mesh-bundle
            // LoadFromMemoryAsync and forwards its requests (in order) into modelQueue. The DRIVER is NOT
            // started here: replaying a model's CreateMaterial.Execute needs every texture registered, which
            // is only true after InsertBundledTextures below, so the driver is started at the model stage.
            var groupQueue = new BlockingCollection<ModelGroup>(ModelGroupQueueCapacity);
            var modelQueue = new BlockingCollection<ModelLoadRequest>();
            // Reset the shared pump<->driver state before the pipeline starts (both are static, so a prior
            // load in this process could have left them non-zero/non-null).
            residentModelBundleBytes = 0;
            modelCompileFault = null;
            Task.Run(() => CompileModelGroups(modelAssets, groupQueue));
            gdb.StartCoroutine(ModelBundlePumpCoroutine(groupQueue, modelQueue));

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

            // start texture loading
            gdb.progressFraction = 0.25f;
            KSPCFFastLoaderReport.wAudioLoading.Stop();
            KSPCFFastLoaderReport.wTextureLoading.Restart();
            gdb.progressTitle = "Loading texture assets...";

            yield return null;

            // note : we could use the StringComparer.OrdinalIgnoreCase comparer as the dictionary key comparer,
            // as this is the comparison that stock is doing. However, profiling show that casing mismatches rarely happen
            // (never in stock, 0.22% of calls in a very heavily modded install with a bunch of part mods of varying quality)
            // and the overhead of the OrdinalIgnoreCase comparer is offsetting the gains (but a small margin, but still). 
            texturesByUrl = new Dictionary<string, TextureInfo>(allTextureFiles.Count);

            // call our custom loader
            yield return gdb.StartCoroutine(TextureDriverCoroutine(textureQueue, allTextureFiles, bundleState, textureCount));

            // Now wait for all asset bundle textures to finish
            yield return gdb.StartCoroutine(InsertBundledTextures(bundleState, allTextureFiles, textureCount));

            QualitySettings.asyncUploadTimeSlice = 2;

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

            // call our custom loader: drain the model pipeline (compiled meshes replayed from their group
            // bundle, skinned/dae fallbacks, failures) in strict modelAssets order. The compile task + pump
            // started overlapping texture loading above; the driver only registers models now that every
            // texture is in the database (CreateMaterial replay resolves textures via GameDatabase).
            yield return gdb.StartCoroutine(ModelDriverCoroutine(modelQueue, allModelFiles, modelAssets.Count));

#if DEBUG_MODEL_LOAD_ORDER
            // Optional load-order dump for an old-vs-new diff (enable via the #define at the top of the file).
            {
                var sb = new System.Text.StringBuilder(1024);
                sb.Append("[KSPCF:FastLoader] model load order (").Append(gdb.databaseModelFiles.Count).Append(" files):\n");
                for (int i = 0; i < gdb.databaseModelFiles.Count; i++)
                    sb.Append(gdb.databaseModelFiles[i].url).Append('\n');
                sb.Append("[KSPCF:FastLoader] modelsByDirectoryUrl first-wins (").Append(modelsByDirectoryUrl.Count).Append(" dirs):\n");
                foreach (var kvp in modelsByDirectoryUrl)
                    sb.Append(kvp.Key).Append(" -> ").Append(kvp.Value.IsNotNullOrDestroyed() ? kvp.Value.transform.name : "<null>").Append('\n');
                Debug.Log(sb.ToString());
            }
#endif

            QualitySettings.asyncUploadBufferSize = 32;

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

        /// <summary>
        /// Asset wrapper class, carrier for model files flowing through the background model pipeline
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
                TextureCUSTOM,
                ModelMU,
                ModelDAE
            }

            private UrlFile file;

            public UrlFile File => file;

            public RawAsset(UrlFile file)
            {
                this.file = file;
            }
        }

        #endregion

        #region Texture bundle loader
        private struct BundleItem
        {
            public TextureLoadRequest Request;
            public bool IsNormalMap;
        }

        // Result of the background DDS bucketing + bundle-building task.
        private sealed class BundleBuildResult
        {
            // The combined bundle bytes, or null when no DDS texture was bundle-eligible.
            public byte[] Bytes;
            // The eligible textures, to be looked up in the loaded bundle by File.url.
            public List<BundleItem> Items;
        }

        private sealed class BundleState
        {
            // How many textures are getting loaded from the bundle?
            public int Count => Items.Count;
            // What's the current progress of the bundle
            public float Progress;
            // Did the bundle load fail?
            public bool Failed;
            public bool Done;
            public Dictionary<string, Texture2D> Map = [];
            public List<BundleItem> Items = [];
        }

        private static BundleBuildResult BuildDDSBundle(
            List<TextureLoadRequest> bundleRequests,
            BlockingCollection<TextureLoadRequest> textureQueue)
        {
            List<TextureBundleBuilder.TextureEntry> entries = new(bundleRequests.Count);
            List<BundleItem> items = new(bundleRequests.Count);

            try
            {
                foreach (BundleClassification result in bundleRequests.AsParallel().AsOrdered().Select(ClassifyBundleRequest))
                {
                    if (result.Eligible)
                    {
                        entries.Add(result.Entry);
                        items.Add(result.Item);
                    }
                    else
                    {
                        textureQueue.Add(result.Request);
                    }
                }
            }
            finally
            {
                // Signal to the main thread that no new requests are coming
                textureQueue.CompleteAdding();
            }

            // Put the largest textures first so unity loads them during the audio phase.
            entries.Sort(static (a, b) => -a.PixelsLength.CompareTo(b.PixelsLength));

            return new BundleBuildResult
            {
                Bytes = entries.Count == 0 ? null : TextureBundleBuilder.BuildMany(entries),
                Items = items,
            };
        }

        // Outcome of classifying one bundle candidate: either it is bundle-eligible (Entry + Item are set and
        // it goes into the combined bundle) or it isn't (only Request is set and it goes to the driver queue).
        private readonly struct BundleClassification
        {
            public readonly TextureLoadRequest Request;
            public readonly bool Eligible;
            public readonly TextureBundleBuilder.TextureEntry Entry;
            public readonly BundleItem Item;

            public BundleClassification(TextureLoadRequest request)
            {
                Request = request;
                Eligible = false;
                Entry = default;
                Item = default;
            }

            public BundleClassification(TextureBundleBuilder.TextureEntry entry, BundleItem item)
            {
                Request = item.Request;
                Eligible = true;
                Entry = entry;
                Item = item;
            }
        }

        /// <summary>
        /// Can we include this request in the asset bundle?
        /// </summary>
        private static BundleClassification ClassifyBundleRequest(TextureLoadRequest req)
        {
            // Resolve the file whose pixels this request streams from. DDS streams from its own file; a PNG
            // streams from its DXT cache, but only when caching is on and a valid, up-to-date cache exists. A
            // PNG without one goes to the regular decode path (which rebuilds the cache), so its normal-map
            // status comes from the file name, not a header.
            string sourcePath;
            bool isNormalMap;
            if (req.AssetType == RawAsset.AssetType.TexturePNG)
            {
                if (!textureCacheEnabled || !TryGetValidPngCache(req.File, out sourcePath))
                    return new BundleClassification(req);
                isNormalMap = req.File.name.EndsWith("NRM");
            }
            else
            {
                sourcePath = req.File.fullPath;
                isNormalMap = false;
            }

            DDSPreparedHeader hdr;
            try
            {
                using (s_pmParseDDSHeader.Auto())
                    hdr = ParseDDSHeader(sourcePath);
            }
            catch
            {
                // Couldn't parse: let the per-request loader re-parse (rebuilding the cache for a PNG) and
                // surface the error.
                return new BundleClassification(req);
            }

            req.FileLength = hdr.FileLength;
            if (req.AssetType != RawAsset.AssetType.TexturePNG)
                isNormalMap = hdr.IsNormalMap;

            if (hdr.BundleEligible && SupportedFormatCache.IsSupported(hdr.Format))
            {
                req.Bundled = true;
                TextureBundleBuilder.TextureEntry entry = new(
                    req.File.url,
                    hdr.Width, hdr.Height, hdr.MipCount,
                    hdr.ClassicTextureFormat, hdr.ColorSpace, readable: false,
                    sourcePath, hdr.DataOffset, hdr.StreamedSize);
                return new BundleClassification(entry, new BundleItem { Request = req, IsNormalMap = isNormalMap });
            }

            return new BundleClassification(req);
        }

        private static IEnumerator LoadBundledAssets(
            BundleState state,
            List<TextureLoadRequest> requests,
            BlockingCollection<TextureLoadRequest> textureQueue
        )
        {
            var inner = LoadBundledAssetsImpl(state, requests, textureQueue);

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
                    if (e is AggregateException agg)
                        e = agg.InnerException ?? e;

                    Debug.LogError("Failed to load bundled textres");
                    Debug.LogException(e);

                    state.Progress = 1f;
                    state.Failed = true;
                    break;
                }

                yield return current;
            }

            state.Done = true;
        }

        private static IEnumerator LoadBundledAssetsImpl(
            BundleState state,
            List<TextureLoadRequest> requests,
            BlockingCollection<TextureLoadRequest> textureQueue)
        {
            var task = Task.Run(() => BuildDDSBundle(requests, textureQueue));
            while (!task.IsCompleted)
                yield return null;

            var built = task.Result;
            state.Items = built.Items;
            if (state.Count == 0)
            {
                state.Progress = 1f;
                yield break;
            }

            var bundleRequest = AssetBundle.LoadFromMemoryAsync(built.Bytes);
            yield return bundleRequest;

            var bundle = bundleRequest.assetBundle;
            if (bundle == null)
                throw new Exception("failed to load texture asset bundle");

            var request = bundle.LoadAllAssetsAsync();

            // This should (maybe?) allow other concurrent asset bundle requests
            // to not be blocked by this one.
            request.priority = -10;
            while (!request.isDone)
            {
                state.Progress = request.progress;
                yield return null;
            }
            state.Progress = 1f;

            UnityEngine.Object[] assets = request.allAssets;
            Dictionary<string, Texture2D> map = new(assets.Length);
            for (int i = 0; i < assets.Length; ++i)
            {
                if (assets[i] is Texture2D tex)
                    map[tex.name] = tex;
            }

            state.Map = map;
        }

        private static IEnumerator InsertBundledTextures(
            BundleState state,
            HashSet<string> loadedUrls,
            int totalTextureCount)
        {
            var gdb = GameDatabase.Instance;
            while (!state.Done)
            {
                int progress = (int)(state.Progress * state.Count);
                gdb.progressFraction = (float)(loadedAssetCount + progress) / totalAssetCount;
                gdb.progressTitle = $"Loading texture asset {progress}/{totalTextureCount}";
                yield return null;
            }

            List<BundleItem> items = state.Items;
            if (items == null || items.Count == 0)
                yield break;

            Dictionary<string, Texture2D> map = state.Map;

            foreach (var item in items)
            {
                TextureLoadRequest req = item.Request;

                if (!state.Failed && map != null
                    && map.TryGetValue(req.File.url, out Texture2D tex) && tex.IsNotNullOrDestroyed())
                {
                    req.Result = new TextureInfo(req.File, tex, item.IsNormalMap, isReadable: false, isCompressed: true);
                    req.Status = TextureLoadRequest.State.Ready;
                }
                else
                {
                    req.ErrorMessage ??= "DDS: streamed texture missing from combined bundle";
                    req.Status = TextureLoadRequest.State.Failed;
                }

                InsertReadyRequest(req, loadedUrls);
                loadedAssetCount++;

                float frameTime = Time.realtimeSinceStartup - Time.unscaledTime;
                if (frameTime > 0.1)
                    yield return null;
            }
        }
        #endregion

        #region Model bundle loader

        // Outcome of compiling one model file off-thread. Mutually-exclusive shapes: a .dae marker
        // (IsDae), a file-read failure (Compiled == null, ReadFailure set), or a compiled model (Compiled
        // set). The compile SUCCESS/COMPILE-FAILED split is decided in the fold from Compiled's flags.
        private struct CompileResult
        {
            public UrlFile File;
            public bool IsDae;
            public string ReadFailure;
            public CompiledModel Compiled;
            public long FileLength;
        }

        // Compile one model file. MUST NOT throw (a faulted PLINQ Select would abort the whole enumeration):
        // the only thrower here is File.ReadAllBytes, caught into a ReadFailure marker; MuModelCompiler.Compile
        // never throws.
        private static CompileResult CompileOne(RawAsset asset, ThreadLocal<MuModelCompiler> tl)
        {
            UrlFile file = asset.File;

            // .dae/.DAE never touch the compiler; the main-thread Dae path reloads them via the stock loader.
            string ext = file.fileExtension;
            if (ext == "dae" || ext == "DAE")
                return new CompileResult { File = file, IsDae = true };

            byte[] data;
            try
            {
                // Plain managed array (NOT arrayPool): avoids cross-thread pool contention with the disk reader.
                data = System.IO.File.ReadAllBytes(file.fullPath);
            }
            catch (Exception e)
            {
                return new CompileResult { File = file, ReadFailure = e.Message };
            }

            // Args mirror MuParser.Parse(file.parent.url, buffer, dataLength): fileUrl == file.url,
            // directoryUrl == file.parent.url. Per-thread compiler (ResetState makes reuse safe).
            CompiledModel cm = tl.Value.Compile(file.url, file.parent.url, data, data.Length);

            return new CompileResult
            {
                File = file,
                Compiled = cm,
                FileLength = data.Length,
            };
        }

        // Phase 1/2 background task (analogue of BuildDDSBundle): a parallel ORDERED PLINQ query compiles
        // every model off-thread, and a serial fold on THIS single background thread groups the ordered
        // results into count-capped ModelGroups, baking each group's static meshes into one mesh bundle via
        // MeshBundleBuilder.BuildMany. AsOrdered + in-order span append is the first link of the load-order
        // chain (pump forward + FIFO drain are the rest). Always CompleteAdding(groupQueue) in finally so the
        // consumer terminates even on fault.
        private static void CompileModelGroups(
            List<RawAsset> modelAssets,
            BlockingCollection<ModelGroup> groupQueue)
        {
            // Tuning knob: max compiled (non-skinned) .mu models per bundle. Larger groups amortize
            // LoadFromMemoryAsync overhead; smaller groups start spawning sooner and drain native bundle
            // copies more often.
            const int GroupModelCap = 512;

            // m9: the WHOLE body runs inside this try so groupQueue.CompleteAdding() (finally, below) fires even
            // if the pre-loop setup (ThreadLocal / list allocations, PLINQ query construction) throws under OOM.
            // Otherwise the pump and driver would hang forever on a queue that never completes.
            try
            {
                // Per-thread compiler: MuModelCompiler holds mutable per-file accumulators, so it is NEVER
                // shared/static; ResetState (top of Compile) makes reuse across files on one worker safe.
                using var tl = new ThreadLocal<MuModelCompiler>(() => new MuModelCompiler());

                var span = new List<ModelLoadRequest>();
                var blobs = new List<MeshBlob>();
                var groupKeys = new HashSet<string>(StringComparer.Ordinal);
                int modelCount = 0;

                // Seal the current span+blobs into a group, build its bundle, free per-request geometry, hand
                // it off, then start fresh. Never runs on an empty span in practice (all call sites guard it).
                void Flush()
                {
                    int compiledCount = 0;
                    for (int i = 0; i < span.Count; i++)
                        if (span[i].ModelKind == ModelLoadRequest.Kind.CompiledMu)
                            compiledCount++;

                    var group = new ModelGroup
                    {
                        Requests = span,
                        PendingBundleRefs = compiledCount,
                    };

                    try
                    {
                        // BuildMany reads the blob list now (throws on duplicate canonical keys; prevented by
                        // the per-group key-set split below). Null bundle when the span has no static meshes.
                        group.BundleBytes = blobs.Count == 0 ? null : MeshBundleBuilder.BuildMany(blobs);

                        for (int i = 0; i < span.Count; i++)
                        {
                            ModelLoadRequest r = span[i];
                            if (r.ModelKind == ModelLoadRequest.Kind.CompiledMu)
                            {
                                r.Group = group;
                                r.Compiled.Blobs = null; // geometry now lives in the bundle; free managed copy
                            }
                        }
                    }
                    catch (Exception buildEx)
                    {
                        // M2: isolate a mesh-bundle build failure to THIS group instead of aborting the whole
                        // fold. HARD-FAIL every CompiledMu request in the group (surfaced by InsertReadyModel's
                        // LOAD FAILED path — no MuParser fallback, so a real compiler/mesh bug is discovered
                        // during in-KSP validation), drop the (absent) bundle and its ref-count, and still
                        // forward the group IN ORDER so load order is preserved.
                        string msg = "mesh bundle build failed: " + buildEx.Message;
                        for (int i = 0; i < span.Count; i++)
                        {
                            ModelLoadRequest r = span[i];
                            if (r.ModelKind == ModelLoadRequest.Kind.CompiledMu)
                            {
                                r.ModelKind = ModelLoadRequest.Kind.Failed;
                                r.FailureMessage = msg;
                                if (r.Compiled != null)
                                    r.Compiled.Blobs = null;
                            }
                        }
                        group.BundleBytes = null;
                        group.PendingBundleRefs = 0;
                    }

                    groupQueue.Add(group);

                    span = new List<ModelLoadRequest>();
                    blobs = new List<MeshBlob>();
                    groupKeys.Clear();
                    modelCount = 0;
                }

                // AsOrdered() => the fold observes results in modelAssets order (load-order parity link #1).
                foreach (CompileResult rec in modelAssets
                    .AsParallel()
                    .AsOrdered()
                    .Select(asset => CompileOne(asset, tl)))
                {
                    var req = new ModelLoadRequest
                    {
                        File = rec.File,
                        FileLength = rec.FileLength,
                    };

                    if (rec.IsDae)
                    {
                        req.ModelKind = ModelLoadRequest.Kind.Dae;
                        span.Add(req); // rides the span for ordering; contributes no blobs
                    }
                    else if (rec.Compiled == null)
                    {
                        // File read failed: hard failure (no Compiled to flush).
                        req.ModelKind = ModelLoadRequest.Kind.Failed;
                        req.FailureMessage = rec.ReadFailure;
                        span.Add(req);
                    }
                    else
                    {
                        CompiledModel cm = rec.Compiled;
                        req.Compiled = cm; // carried for FlushLogs (and, for CompiledMu, replay)

                        if (cm.Failed)
                        {
                            // Compilation failed: hard failure. cm carries its buffered diagnostics; its baked
                            // geometry is never used.
                            req.ModelKind = ModelLoadRequest.Kind.Failed;
                            req.FailureMessage = cm.FailureMessage;
                            cm.Blobs = null;
                            span.Add(req);
                        }
                        else
                        {
                            req.ModelKind = ModelLoadRequest.Kind.CompiledMu;

                            // Per-group duplicate-canonical-key split: two model files sharing a url (the reason
                            // the whole dedup machinery exists) emit identical mesh names ("{url}#i"), which would
                            // make BuildMany throw and fail the ENTIRE group. Flush first so each duplicate lands
                            // in its own bundle; the duplicate is then dropped first-wins at registration.
                            // MeshBlob.Name is already canonical (idempotent under Canonicalize), so we key on it
                            // directly, matching BuildMany's canonical-key dedup.
                            MeshBlob[] cmBlobs = cm.Blobs;
                            bool collides = false;
                            for (int i = 0; i < cmBlobs.Length; i++)
                            {
                                if (groupKeys.Contains(cmBlobs[i].Name))
                                {
                                    collides = true;
                                    break;
                                }
                            }
                            if (collides)
                                Flush();

                            for (int i = 0; i < cmBlobs.Length; i++)
                            {
                                blobs.Add(cmBlobs[i]);
                                groupKeys.Add(cmBlobs[i].Name);
                            }
                            span.Add(req);

                            if (++modelCount == GroupModelCap)
                                Flush();
                        }
                    }
                }

                // Trailing partial group (may carry a null bundle if it is all dae/skinned/failed).
                if (span.Count > 0)
                    Flush();
            }
            catch (Exception e)
            {
                // M2 last resort: with per-group Flush isolation this should almost never fire (a faulted PLINQ
                // enumeration surfaces as AggregateException at the foreach above). Do NOT Debug.* from this
                // background Task thread — the mod handlers chained onto Application.logMessageReceived aren't
                // thread-safe (the exact hazard DeferredLog exists to avoid). Stash it for ModelDriverCoroutine
                // to log ONCE on the main thread when it terminates.
                modelCompileFault = e;
            }
            finally
            {
                // Guarantees the pump (and therefore the driver) terminates even on fault. Mirrors
                // BuildDDSBundle's textureQueue.CompleteAdding().
                groupQueue.CompleteAdding();
            }
        }

        // Phase 3 main-thread pump: drains finished ModelGroups, kicks off each group's mesh-bundle load, and
        // forwards its requests (in order) into modelQueue for the driver. Poll form (TryTake) so it never
        // blocks a coroutine thread on GetConsumingEnumerable. Completes modelQueue in finally.
        private static IEnumerator ModelBundlePumpCoroutine(
            BlockingCollection<ModelGroup> groupQueue,
            BlockingCollection<ModelLoadRequest> modelQueue)
        {
            try
            {
                while (!groupQueue.IsCompleted)
                {
                    while (groupQueue.TryTake(out ModelGroup group))
                    {
                        if (group.BundleBytes != null)
                        {
                            // Capture the native size before nulling the managed copy; BundleSize feeds the
                            // resident-memory accounting here and the driver's Unload adjust.
                            group.BundleSize = group.BundleBytes.Length;

                            // M1 backpressure: bound resident native bundle memory. Wait until the driver has
                            // Unloaded enough earlier groups to make room before kicking this one off. The
                            // residentModelBundleBytes > 0 guard guarantees a single oversized group still loads
                            // once everything before it has drained (no deadlock). This yield loop sits OUTSIDE
                            // the LoadFromMemoryAsync try/catch below on purpose — a try with a catch may not
                            // contain a yield.
                            while (residentModelBundleBytes > 0 &&
                                   residentModelBundleBytes + group.BundleSize > MaxResidentModelBundleBytes)
                                yield return null;

                            bool loadFailed = false;
                            string loadFailMsg = null;
                            try
                            {
                                group.CreateRequest = AssetBundle.LoadFromMemoryAsync(group.BundleBytes);
                                // Match the texture bundle: low priority so third-party bundle loads aren't
                                // starved.
                                group.CreateRequest.priority = -10;
                            }
                            catch (Exception e)
                            {
                                // M4: isolate a LoadFromMemoryAsync failure to this group. HARD-FAIL its
                                // CompiledMu requests (no MuParser fallback) and still forward ALL requests in
                                // order below so load order is preserved.
                                loadFailed = true;
                                loadFailMsg = "bundle load failed: " + e.Message;
                            }

                            group.BundleBytes = null; // drop the managed copy either way; the bundle owns it now

                            if (loadFailed)
                            {
                                group.CreateRequest = null;
                                List<ModelLoadRequest> greqs = group.Requests;
                                for (int i = 0; i < greqs.Count; i++)
                                {
                                    ModelLoadRequest r = greqs[i];
                                    if (r.ModelKind == ModelLoadRequest.Kind.CompiledMu)
                                    {
                                        r.ModelKind = ModelLoadRequest.Kind.Failed;
                                        r.FailureMessage = loadFailMsg;
                                    }
                                }
                            }
                            else
                            {
                                // Reserve the resident bytes now that the load is in flight; the driver frees
                                // them when the group's last CompiledMu registers and it Unload(false)s.
                                residentModelBundleBytes += group.BundleSize;
                            }
                        }

                        List<ModelLoadRequest> reqs = group.Requests;
                        for (int i = 0; i < reqs.Count; i++)
                            modelQueue.Add(reqs[i]); // in-order forward (load-order parity link #3)

                        yield return null;
                    }

                    yield return null;
                }
            }
            finally
            {
                modelQueue.CompleteAdding();
            }
        }

        // Phase 3 driver (clone of TextureDriverCoroutine): spawns up to MaxModelSpawnsPerFrame per-request
        // loaders per frame and inserts finished ones in STRICT FIFO order. The FIFO drain (peek head; if
        // still Pending, WAIT — never skip/reorder) is what preserves modelAssets load order.
        private static IEnumerator ModelDriverCoroutine(
            BlockingCollection<ModelLoadRequest> modelQueue,
            HashSet<string> loadedUrls,
            int totalModelCount)
        {
            GameDatabase gdb = GameDatabase.Instance;
            Queue<ModelLoadRequest> active = new();
            int completed = 0;

            while (true)
            {
                for (int i = 0; i < MaxModelSpawnsPerFrame; ++i)
                {
                    if (!modelQueue.TryTake(out ModelLoadRequest request))
                        break;

                    gdb.StartCoroutine(LoadModelCoroutine(request));
                    active.Enqueue(request);
                }

                while (active.TryPeek(out ModelLoadRequest pending))
                {
                    if (pending.Status == ModelLoadRequest.State.Pending)
                        break; // head not done yet: WAIT, never reorder (load-order parity link #4)

                    active.Dequeue();
                    // A throw in InsertReadyModel's body must not kill the driver (that would halt ALL further
                    // model registration). Its finally still runs on the throwing path (ref-count/Unload/resident
                    // release), so we just log the one bad model and skip it. The bookkeeping below runs
                    // regardless so counts/progress don't stall on a skipped model.
                    try { InsertReadyModel(pending, loadedUrls); }
                    catch (Exception e) { Debug.LogException(e); }
                    loadedAssetCount++;
                    completed++;
                }

                gdb.progressFraction = (float)loadedAssetCount / totalAssetCount;
                gdb.progressTitle = $"Loading model asset {completed}/{totalModelCount}";

                // Done when the producers have finished and everything spawned has been drained.
                if (modelQueue.IsCompleted && active.Count == 0)
                    break;

                yield return null;
            }

            // M2: surface any last-resort compile-task fault ONCE, here on the MAIN thread (the fold stashed it
            // rather than calling Debug.* off the Task thread).
            Exception fault = modelCompileFault;
            if (fault != null)
            {
                modelCompileFault = null;
                Debug.LogException(fault);
            }
        }

        // Exception-wrapping driver for one request (clone of LoadTextureCoroutine): drives the inner
        // per-Kind enumerator, mapping any thrown exception to a Failed status + message.
        private static IEnumerator LoadModelCoroutine(ModelLoadRequest req)
        {
            IEnumerator inner;
            switch (req.ModelKind)
            {
                case ModelLoadRequest.Kind.CompiledMu:
                    inner = LoadCompiledModelCoroutine(req);
                    break;
                case ModelLoadRequest.Kind.Dae:
                    inner = LoadDaeModelCoroutine(req);
                    break;
                default:
                    inner = LoadFailedModelCoroutine(req);
                    break;
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
                    req.FailureMessage = $"{e.GetType().Name}: {e.Message}";
                    req.Status = ModelLoadRequest.State.Failed;
                    yield break;
                }

                yield return current;
            }

            if (req.Status != ModelLoadRequest.State.Pending)
                yield break;

            if (req.Result.IsNotNullOrDestroyed())
            {
                req.Status = ModelLoadRequest.State.Ready;
            }
            else
            {
                req.FailureMessage ??= "Loader produced no result";
                req.Status = ModelLoadRequest.State.Failed;
            }
        }

        // CompiledMu: load this model's meshes from the group bundle (per-name LoadAssetAsync, NOT
        // LoadAllAssetsAsync), then replay the compiled instructions on the main thread. Textures/shaders are
        // resolved inside Execute, which is why the driver only runs after all textures are registered.
        private static IEnumerator LoadCompiledModelCoroutine(ModelLoadRequest req)
        {
            CompiledModel cm = req.Compiled;
            MeshBinding[] bindings = cm.Bindings;
            var locals = new UnityEngine.Object[cm.LocalCount];

            // A model with meshes always sits in a group with a bundle (its blobs made BundleBytes non-null),
            // so waiting for CreateRequest can't deadlock. A mesh-less model (no bindings) may be in a
            // bundle-less group whose CreateRequest stays null forever, so it MUST skip the wait entirely.
            if (bindings.Length > 0)
            {
                ModelGroup g = req.Group;
                while (g.CreateRequest == null)
                    yield return null;

                // POLL isDone; do NOT `yield return g.CreateRequest`. This AssetBundleCreateRequest is SHARED by
                // every model coroutine in the group (created once by ModelBundlePumpCoroutine), and the driver
                // runs up to MaxModelSpawnsPerFrame of them concurrently. Unity forbids yielding one async op
                // from more than one coroutine ("...already being yielded from another coroutine"), so each
                // coroutine waits independently by yielding null until the shared op reports done.
                while (!g.CreateRequest.isDone)
                    yield return null;

                AssetBundle bundle = g.CreateRequest.assetBundle;
                if (bundle == null)
                {
                    req.FailureMessage = "mesh bundle failed to load";
                    req.Status = ModelLoadRequest.State.Failed;
                    yield break;
                }

                for (int i = 0; i < bindings.Length; i++)
                {
                    MeshBinding b = bindings[i];
                    AssetBundleRequest ar = bundle.LoadAssetAsync<Mesh>(b.CanonicalName);
                    ar.priority = -10;
                    yield return ar;
                    // A missing mesh yields a null asset -> null mesh, matching MuParser's null-mesh handling.
                    locals[b.Slot] = ar.asset;
                }
            }

            IModelInstruction[] instructions = cm.Instructions;
            for (int i = 0; i < instructions.Length; i++)
                instructions[i].Execute(locals);

            req.Result = (GameObject)locals[0];
            req.Status = ModelLoadRequest.State.Ready;
        }

        // Dae fallback: reload via the stock DAE loader (see the shared LoadDAE helper).
        private static IEnumerator LoadDaeModelCoroutine(ModelLoadRequest req)
        {
            GameObject go = LoadDAE(req.File);

            if (go.IsNullOrDestroyed())
            {
                req.FailureMessage = "DAE model load error";
                req.Status = ModelLoadRequest.State.Failed;
                yield break;
            }

            req.FileLength = new FileInfo(req.File.fullPath).Length;
            req.Result = go;
            req.Status = ModelLoadRequest.State.Ready;
        }

        // Failed: hard failure. Message was already set in the fold (read or compile failure); keep it.
        private static IEnumerator LoadFailedModelCoroutine(ModelLoadRequest req)
        {
            req.FailureMessage ??= req.Compiled?.FailureMessage;
            req.Status = ModelLoadRequest.State.Failed;
            yield break;
        }

        // Main-thread registration (clone of InsertReadyRequest; replicates RawAsset.LoadAndDisposeMainThread's
        // model registration). Called ONLY from the driver's FIFO drain, so it walks modelAssets order.
        private static void InsertReadyModel(ModelLoadRequest req, HashSet<string> loadedUrls)
        {
            // m5: the ENTIRE body (FlushLogs, the Debug.* calls, and every registration branch) runs inside this
            // try so the finally below runs on EVERY path — it guarantees the exactly-once PendingBundleRefs
            // decrement + resident-bytes release + Unload for each CompiledMu, so even a throw in the body still
            // frees the group's native bundle copy and never strands the resident cap. The throw itself is NOT
            // swallowed here; ModelDriverCoroutine's FIFO drain wraps this call and logs+skips the one bad model,
            // so a single failure can't kill the driver.
            try
            {
                // On the MAIN THREAD: emit the compiler's buffered diagnostics (KSP's log handler and the mod
                // handlers chained onto Application.logMessageReceived are not thread-safe, so they could not be
                // flushed off-thread). Null-safe: Dae has no Compiled.
                req.Compiled?.FlushLogs();

                Debug.Log($"Load Model: {req.File.url}");

                if (req.Status == ModelLoadRequest.State.Failed)
                {
                    Debug.LogWarning($"LOAD FAILED: {req.File.url}: {req.FailureMessage}");
                    if (req.Result.IsNotNullOrDestroyed())
                        UnityEngine.Object.Destroy(req.Result);
                    return;
                }

                // Built-before-check (like the texture dup path): a duplicate url means we already built the
                // GameObject, so it must be destroyed. First-wins, matching stock FilesLoader.
                if (!loadedUrls.Add(req.File.url))
                {
                    Debug.LogWarning($"Duplicate model asset '{req.File.url}' with extension '{req.File.fileExtension}' won't be loaded");
                    if (req.Result.IsNotNullOrDestroyed())
                        UnityEngine.Object.Destroy(req.Result);
                    return;
                }

                // Exact replication of RawAsset.LoadAndDisposeMainThread's model registration.
                GameObject model = req.Result;
                model.transform.name = req.File.url;
                model.transform.parent = Instance.transform;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.SetActive(false);
                Instance.databaseModel.Add(model);
                Instance.databaseModelFiles.Add(req.File);
                modelsByUrl[req.File.url] = model;
                // if multiple models in the same dir, we only add the first
                // to ensure identical behavior as the GameDatabase.GetModelPrefabIn() method
                modelsByDirectoryUrl.TryAdd(req.File.parent.url, model);
                urlFilesByModel.Add(model, req.File);
                KSPCFFastLoaderReport.modelsBytesLoaded += req.FileLength;
                KSPCFFastLoaderReport.modelsLoaded++;
            }
            finally
            {
                if (req.ModelKind == ModelLoadRequest.Kind.CompiledMu && --req.Group.PendingBundleRefs == 0)
                {
                    // M1: release this group's resident-bytes reservation so the pump can load more groups.
                    // Done BEFORE the Unload below on purpose: if Unload ever threw, skipping the release would
                    // strand the cap. BundleSize is 0 for a bundle-less group (pump never reserved for it), so
                    // this is a no-op there; it exactly reverses the pump's single += for a group whose bundle
                    // was loaded.
                    residentModelBundleBytes -= req.Group.BundleSize;

                    // false: keep the loaded meshes (now owned by the built GameObjects); free the bundle's
                    // native copy.
                    AssetBundle b = req.Group.CreateRequest?.assetBundle;
                    if (b != null)
                        b.Unload(false);
                }
            }
        }

        // Shared body of the (now-wrapped) RawAsset.LoadDAE, reused by the new Dae path. Reloads the file via
        // the stock DAE loader and reproduces the node_collider fixup.
        private static GameObject LoadDAE(UrlFile file)
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
        #endregion

        #region PNG texture cache

        // If the user opts into it, we cache compressed versions of PNG files as DDS files under
        // GameData/KSPCommunityFixes/PluginData/TextureCache. Later, when textures are loaded from
        // the cache, they can go through the asset bundle path.

        // Marker written into dwReserved1[4] so a cache file is recognisably ours and not some unrelated DDS
        // that happens to hash to the same name.
        private const uint PngCacheMarker = 0x4643_534Bu; // "KSCF"

        private static string PngCacheDir => Path.Combine(ModPath, "PluginData", "TextureCache");

        // Deterministic, collision-free (SHA1) and length-safe cache path for a texture URL.
        private static string GetPngCachePath(string url)
        {
            byte[] hash;
            using (SHA1 sha = SHA1.Create())
                hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
            return Path.Combine(PngCacheDir, BitConverter.ToString(hash).Replace("-", "") + ".dds");
        }

        // Source-file identity stamp: byte size + last-write-time. A cache is valid only while both still match.
        private static bool GetPngStamp(string path, out long size, out long time)
        {
            size = 0;
            time = 0;
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists)
                    return false;
                size = fi.Length;
                time = fi.LastWriteTimeUtc.ToFileTimeUtc();
                return size > 0;
            }
            catch
            {
                return false;
            }
        }

        // Reads the (size, time) stamp embedded in a cache file's DDS reserved header. Returns false if the
        // file is missing, too small, isn't a DDS, or wasn't written by us.
        private static bool TryReadPngCacheStamp(string path, out long size, out long time)
        {
            size = 0;
            time = 0;
            try
            {
                using FileStream fs = File.OpenRead(path);
                if (fs.Length < 148)
                    return false;
                using BinaryReader br = new BinaryReader(fs);
                if (br.ReadUInt32() != DDSValues.uintMagic)
                    return false;
                // dwReserved1 starts 28 bytes into the 124-byte header, i.e. at file offset 32.
                fs.Position = 32;
                long s = br.ReadInt64();               // dwReserved1[0..1]
                long t = br.ReadInt64();               // dwReserved1[2..3]
                if (br.ReadUInt32() != PngCacheMarker) // dwReserved1[4]
                    return false;
                size = s;
                time = t;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // True when a valid, up-to-date cache DDS exists for the given PNG. cachePath is set to the resolved
        // cache location on success so the caller can stream from it.
        private static bool TryGetValidPngCache(UrlFile file, out string cachePath)
        {
            cachePath = GetPngCachePath(file.url);
            if (!GetPngStamp(file.fullPath, out long size, out long time))
                return false;
            return TryReadPngCacheStamp(cachePath, out long cachedSize, out long cachedTime)
                && cachedSize == size && cachedTime == time;
        }

        // Maps the four DXT graphics formats the PNG loader can produce to their DXGI equivalents. Returns
        // false for anything else (e.g. uncompressed), which is never cached.
        private static bool TryGetCacheDxgiFormat(GraphicsFormat format, out uint dxgiFormat)
        {
            switch (format)
            {
                case GraphicsFormat.RGBA_DXT1_UNorm: dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM; return true;
                case GraphicsFormat.RGBA_DXT1_SRGB: dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB; return true;
                case GraphicsFormat.RGBA_DXT5_UNorm: dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM; return true;
                case GraphicsFormat.RGBA_DXT5_SRGB: dxgiFormat = (uint)DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB; return true;
                default: dxgiFormat = 0; return false;
            }
        }

        // Captures the compressed pixels of a just-loaded PNG (main thread) and writes them to the on-disk
        // cache as a DDS on a background thread. No-op unless the texture is in a cacheable DXT format with a
        // round-trippable mip layout. Must be called before Apply(makeNoLongerReadable) frees the pixels.
        private static void TryWritePngCache(UrlFile file, Texture2D src)
        {
            try
            {
                if (!TryGetCacheDxgiFormat(src.graphicsFormat, out uint dxgiFormat))
                    return;

                int width = src.width;
                int height = src.height;
                int mipCount = src.mipmapCount;
                // The bundle path reconstructs either a full mip chain or a single level; anything else
                // (a partial chain) can't be round-tripped, so don't cache it.
                if (mipCount != 1 && mipCount != ComputeMipCount(width, height))
                    return;

                if (!GetPngStamp(file.fullPath, out long size, out long time))
                    return;

                byte[] data = src.GetRawTextureData<byte>().ToArray();
                string path = GetPngCachePath(file.url);
                Task.Run(() => WritePngCacheFile(path, width, height, mipCount, dxgiFormat, size, time, data));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KSPCFFastLoader] Couldn't cache PNG '{file.url}': {e.Message}");
            }
        }

        private static void WritePngCacheFile(
            string path, int width, int height, int mipCount, uint dxgiFormat, long srcSize, long srcTime, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using FileStream fs = File.Create(path);
                using BinaryWriter bw = new BinaryWriter(fs);
                WriteDdsCacheHeader(bw, width, height, mipCount, dxgiFormat, srcSize, srcTime);
                bw.Write(data, 0, data.Length);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KSPCFFastLoader] Couldn't write PNG cache '{path}': {e.Message}");
            }
        }

        // Writes a DX10 DDS header (magic + 124-byte DDS_HEADER + 20-byte DDS_HEADER_DXT10 = 148 bytes) that
        // ParseDDSHeader reads back to the exact GraphicsFormat / dimensions / mip count. The source PNG's
        // (size, time) stamp plus our marker live in the otherwise-unused dwReserved1 words; Unity streams
        // only the pixel bytes past offset 148 and never parses this header, so those words are free to use.
        private static void WriteDdsCacheHeader(
            BinaryWriter bw, int width, int height, int mipCount, uint dxgiFormat, long srcSize, long srcTime)
        {
            bool hasMips = mipCount > 1;

            const uint DDSD_CAPS = 0x1, DDSD_HEIGHT = 0x2, DDSD_WIDTH = 0x4, DDSD_PIXELFORMAT = 0x1000;
            const uint DDSD_MIPMAPCOUNT = 0x20000, DDSD_LINEARSIZE = 0x80000;
            const uint DDPF_FOURCC = 0x4;
            const uint DDSCAPS_TEXTURE = 0x1000, DDSCAPS_COMPLEX = 0x8, DDSCAPS_MIPMAP = 0x400000;

            bool isBc1 = dxgiFormat == (uint)DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM
                         || dxgiFormat == (uint)DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB;
            int blockBytes = isBc1 ? 8 : 16;
            uint topLinearSize = (uint)(Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockBytes);

            bw.Write(DDSValues.uintMagic);                 // "DDS "
            bw.Write(124u);                                // dwSize
            uint flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_LINEARSIZE;
            if (hasMips) flags |= DDSD_MIPMAPCOUNT;
            bw.Write(flags);                               // dwFlags
            bw.Write((uint)height);                        // dwHeight
            bw.Write((uint)width);                         // dwWidth
            bw.Write(topLinearSize);                       // dwPitchOrLinearSize
            bw.Write(0u);                                  // dwDepth
            bw.Write((uint)mipCount);                      // dwMipMapCount
            // dwReserved1[11]: source stamp + marker, rest zero.
            bw.Write(srcSize);                             // [0..1]
            bw.Write(srcTime);                             // [2..3]
            bw.Write(PngCacheMarker);                      // [4]
            for (int i = 5; i < 11; i++)
                bw.Write(0u);                              // [5..10]
            // DDS_PIXELFORMAT (32 bytes): FourCC "DX10".
            bw.Write(32u);                                 // dwSize
            bw.Write(DDPF_FOURCC);                         // dwFlags
            bw.Write(DDSValues.uintDX10);                  // dwFourCC
            bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u); // bit count + channel masks
            uint caps = DDSCAPS_TEXTURE;
            if (hasMips) caps |= DDSCAPS_COMPLEX | DDSCAPS_MIPMAP;
            bw.Write(caps);                                // dwCaps
            bw.Write(0u);                                  // dwCaps2
            bw.Write(0u);                                  // dwCaps3
            bw.Write(0u);                                  // dwCaps4
            bw.Write(0u);                                  // dwReserved2
            // DDS_HEADER_DXT10 (20 bytes).
            bw.Write(dxgiFormat);                          // dxgiFormat
            bw.Write(3u);                                  // resourceDimension = D3D10_RESOURCE_DIMENSION_TEXTURE2D
            bw.Write(0u);                                  // miscFlag
            bw.Write(1u);                                  // arraySize
            bw.Write(0u);                                  // miscFlags2
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
            public enum State : byte
            {
                Pending,
                Ready,
                Failed,
                // Used for custom loaders, they are responsible for printing
                // their own error messages on failure (unless they throw).
                Skip
            }

            public UrlFile File;
            public RawAsset.AssetType AssetType;
            public long FileLength;
            public volatile State Status;
            public TextureInfo Result;
            public string ErrorMessage;
            public Exception Exception;
            public bool Bundled;

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

            // Whether this texture can be loaded through the streamed asset-bundle path (see
            // LoadDDSCoroutine). When true, the fields below are populated for the bundle body.
            public bool BundleEligible;
            public int ClassicTextureFormat; // legacy TextureFormat as int
            public int ColorSpace; // 0 == linear, 1 == sRGB
            public int MipCount; // full mip count Unity will allocate
            public long StreamedSize; // total mip-chain byte size read from the file
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
            FileInfo fi = new(path);
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
            int width = (int)hdr.dwWidth;
            int height = (int)hdr.dwHeight;
            int mipCount = mipChain ? ComputeMipCount(width, height) : 1;

            // Can we load this texture directly through an asset bundle?
            bool bundleEligible = TryGetClassicFormat(fmt, out int classicFormat, out int colorSpace)
                && IsBlockAligned(fmt, width, height);
            long streamedSize = 0;
            if (bundleEligible)
            {
                streamedSize = ComputeMipChainSize(fmt, width, height, mipCount);
                if (streamedSize > int.MaxValue || fileLength - dataOffset < streamedSize)
                    bundleEligible = false;
            }

            return new DDSPreparedHeader
            {
                Width = width,
                Height = height,
                MipChain = mipChain,
                IsNormalMap = isNormalMap,
                Format = fmt,
                DataOffset = dataOffset,
                FileLength = fileLength,
                BundleEligible = bundleEligible,
                ClassicTextureFormat = classicFormat,
                ColorSpace = colorSpace,
                MipCount = mipCount,
                StreamedSize = streamedSize,
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

        // In-place swizzle for RGBA32 normal maps. Goes from rgba -> gggr.
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

        // Channel swizzle for RGB24, allocates and goes from rgb -> gggr.
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
            ReadCommand cmd = new()
            {
                Buffer = NativeArrayUnsafeUtility.GetUnsafePtr(dst),
                Offset = offset,
                Size = size,
            };
            return AsyncReadManager.Read(path, &cmd, 1);
        }

        // An extended version of TextureCreationFlags that contains additional values
        // that are not exposed publically by unity.
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

        // The classic Texture2D object serializes a legacy TextureFormat plus a colour space; only
        // graphics formats that survive the round-trip can go through the bundle path.
        private static bool TryGetClassicFormat(GraphicsFormat format, out int textureFormat, out int colorSpace)
        {
            TextureFormat tf = GraphicsFormatUtility.GetTextureFormat(format);
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(format);
            textureFormat = (int)tf;
            colorSpace = srgb ? 1 : 0;
            return GraphicsFormatUtility.GetGraphicsFormat(tf, srgb) == format;
        }

        // A texture is "block aligned" when it is uncompressed (block size 1x1, always aligned) or
        // its dimensions are a multiple of the compression block size. Only misaligned compressed
        // textures must avoid the background-upload bundle path.
        private static bool IsBlockAligned(GraphicsFormat format, int width, int height)
        {
            if (!GraphicsFormatUtility.IsCompressedFormat(format))
                return true;
            int blockWidth = (int)GraphicsFormatUtility.GetBlockWidth(format);
            int blockHeight = (int)GraphicsFormatUtility.GetBlockHeight(format);
            return width % blockWidth == 0 && height % blockHeight == 0;
        }

        // The number of mip levels Unity allocates for a full mip chain.
        private static int ComputeMipCount(int width, int height)
        {
            int size = Math.Max(width, height);
            int count = 1;
            while (size > 1)
            {
                size >>= 1;
                count++;
            }
            return count;
        }

        // Total byte size of the mip chain as laid out in a Texture2D's raw data: for each level,
        // ceil(w/blockW) * ceil(h/blockH) * blockSize. Matches Unity's GetRawTextureData layout.
        private static long ComputeMipChainSize(GraphicsFormat format, int width, int height, int mipCount)
        {
            int blockWidth = (int)GraphicsFormatUtility.GetBlockWidth(format);
            int blockHeight = (int)GraphicsFormatUtility.GetBlockHeight(format);
            int blockSize = (int)GraphicsFormatUtility.GetBlockSize(format);
            long total = 0;
            for (int mip = 0; mip < mipCount; ++mip)
            {
                int mipWidth = Math.Max(1, width >> mip);
                int mipHeight = Math.Max(1, height >> mip);
                int blocksX = Math.Max(1, (mipWidth + blockWidth - 1) / blockWidth);
                int blocksY = Math.Max(1, (mipHeight + blockHeight - 1) / blockHeight);
                total += (long)blocksX * blocksY * blockSize;
            }
            return total;
        }

        private static IEnumerator LoadDDSCoroutine(TextureLoadRequest req)
        {
            string path = req.File.fullPath;
            Task<DDSPreparedHeader> prepTask = Task.Run(() =>
            {
                using (s_pmParseDDSHeader.Auto())
                    return ParseDDSHeader(path);
            });
            while (!prepTask.IsCompleted)
                yield return null;
            if (prepTask.IsFaulted)
                throw UnwrapFaultedTask(prepTask, "DDS header parse failed");
            DDSPreparedHeader hdr = prepTask.Result;
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
                    // Avoid making the compress call if the frame time is already > 25ms
                    while (true)
                    {
                        float frameTime = Time.realtimeSinceStartup - Time.unscaledTime;
                        if (frameTime < 0.025)
                            break;

                        yield return null;
                    }

                    using (s_pmCompress.Auto())
                        src.Compress(highQuality: !isNormalMap);
                }
                else if (!isNormalMap)
                    Debug.LogWarning($"Texture '{req.File.url}' isn't eligible for DXT compression, width and height must be multiples of 4");

                // Persist the compressed PNG to the on-disk cache (before the pixels are freed below) so
                // future loads can stream it straight from the combined bundle instead of decoding again.
                if (textureCacheEnabled && req.AssetType == RawAsset.AssetType.TexturePNG)
                    TryWritePngCache(req.File, src);

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
                    // Avoid making the compress call if the frame time is already > 25ms
                    while (true)
                    {
                        float frameTime = Time.realtimeSinceStartup - Time.unscaledTime;
                        if (frameTime < 0.025)
                            break;

                        yield return null;
                    }

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
                        // Avoid making the compress call if the frame time is already > 25ms
                        while (true)
                        {
                            float frameTime = Time.realtimeSinceStartup - Time.unscaledTime;
                            if (frameTime < 0.025)
                                break;

                            yield return null;
                        }

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
                        // Avoid making the compress call if the frame time is already > 25ms
                        while (true)
                        {
                            float frameTime = Time.realtimeSinceStartup - Time.unscaledTime;
                            if (frameTime < 0.025)
                                break;

                            yield return null;
                        }

                        using (s_pmCompress.Auto())
                            texture.Compress(highQuality: false);
                    }

                    texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                }
            }

            req.Result = new TextureInfo(req.File, texture, isNormalMap, isReadable: !isNormalMap, isCompressed: true);
            req.Status = TextureLoadRequest.State.Ready;
        }

        // Custom database loaders are singletons, so we cannot load more than
        // one texture at a time with them. This class serves to ensure that
        // doesn't happen.
        class CustomLoaderGuard : CustomYieldInstruction, IDisposable
        {
            static readonly Dictionary<object, Queue<CustomLoaderGuard>> queues = [];

            object loader;

            public CustomLoaderGuard(object loader)
            {
                if (!queues.TryGetValue(loader, out var queue))
                {
                    queue = [];
                    queues.Add(loader, queue);
                }

                queue.Enqueue(this);
                this.loader = loader;
            }

            public override bool keepWaiting
            {
                get
                {
                    if (loader is null)
                        return false;

                    var queue = queues[loader];
                    var head = queue.Peek();
                    return !ReferenceEquals(head, this);
                }
            }

            public void Dispose()
            {
                if (loader is null)
                    return;

                queues[loader].Dequeue();
                loader = null;
            }

            public static void Clear() => queues.Clear();
        }

        private static IEnumerator LoadCUSTOMCoroutine(TextureLoadRequest req)
        {
            UrlFile file = req.File;
            var gdb = GameDatabase.Instance;

            foreach (var loader in gdb.loadersTexture)
            {
                if (!loader.extensions.Contains(file.fileExtension))
                    continue;

                using var guard = new CustomLoaderGuard(loader);
                yield return guard;

                var inner = loader.Load(file, new FileInfo(file.fullPath));
                using var _guard = inner as IDisposable;
                while (inner.MoveNext())
                    yield return inner.Current;

                if (!loader.successful)
                    break;

                loader.obj.name = file.url;
                loader.obj.texture.name = file.url;
                req.Result = loader.obj;
                req.Status = TextureLoadRequest.State.Ready;
                yield break;
            }

            // Some modded loaders (e.g. shabby) use the texture loader to load
            // non-texture things. In this case they'll load the file but not
            // mark the load as successful. KSP does nothing in this case, so
            // we reproduce these by explicitly skipping them, which prints the
            // "Loaded texture: ..." message but doesn't print any error messages.
            req.Status = TextureLoadRequest.State.Skip;
        }

        private static IEnumerator TextureDriverCoroutine(
            BlockingCollection<TextureLoadRequest> requests,
            HashSet<string> loadedUrls,
            BundleState state,
            int totalTextureCount)
        {
            GameDatabase gdb = GameDatabase.Instance;
            Queue<TextureLoadRequest> active = new();
            int start = loadedAssetCount;

            while (true)
            {
                for (int i = 0; i < MaxTextureSpawnsPerFrame; ++i)
                {
                    if (!requests.TryTake(out var request))
                        break;

                    gdb.StartCoroutine(LoadTextureCoroutine(request));
                    active.Enqueue(request);
                }

                while (active.TryPeek(out var pending))
                {
                    if (pending.Status == TextureLoadRequest.State.Pending)
                        break;

                    active.Dequeue();
                    InsertReadyRequest(pending, loadedUrls);

                    float frameTime = Time.realtimeSinceStartup - Time.unscaledTime;
                    if (frameTime > 0.1)
                        break;
                }

                int completed = loadedAssetCount - start;
                int progress = completed + (int)(state.Progress * state.Count);

                gdb.progressFraction = (float)loadedAssetCount / totalAssetCount;
                gdb.progressTitle = $"Loading texture asset {progress}/{totalTextureCount}";

                // Done when the producers have finished and everything spawned has been drained.
                if (requests.IsCompleted && active.Count == 0)
                    break;

                yield return null;
            }

            CustomLoaderGuard.Clear();
        }

        struct AssetCountGuard() : IDisposable
        {
            public void Dispose() => loadedAssetCount++;
        }

        private static IEnumerator LoadTextureCoroutine(TextureLoadRequest req)
        {
            using var guard = new AssetCountGuard();

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
                    inner = LoadCUSTOMCoroutine(req);
                    break;
            }

            using var _guard = inner as IDisposable;

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

        private static void InsertReadyRequest(TextureLoadRequest req, HashSet<string> loadedUrls)
        {
            Debug.Log($"Load Texture: {req.File.url}");

            if (req.Status == TextureLoadRequest.State.Skip)
                return;

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

        #region User opt-in popup

        // Shown once on first launch (from FastAssetLoader, gated on userOptInChoiceDone) to let the user
        // enable the on-disk PNG texture cache. The choice is persisted to PNGTextureCache.cfg and drives
        // textureCacheEnabled; the popup also estimates the loading-time saving and disk cost from the
        // install's PNG textures.

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
