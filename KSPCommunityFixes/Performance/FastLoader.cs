using DDSHeaders;
using HarmonyLib;
using KSPCommunityFixes.Library.Collections;
using KSPCommunityFixes.Library.Buffers;
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using static GameDatabase;
using static UrlDir;
using Debug = UnityEngine.Debug;
using KSPAchievements;
using static ConfigNode;

namespace KSPCommunityFixes.Performance
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    class FastLoader : MonoBehaviour
    {
        // approximate max FPS during asset loading and part parsing
        private const int maxFPS = 30; 
        private const float minFrameTime = 1f / maxFPS;
        private const double minFrameTimeD = 1.0 / maxFPS;

        // max size of in-memory disk reads, can and will be exceeded
        private const int maxBufferSize = 1024 * 1024 * 50; // 50MB
        // min amount of files to try to keep in memory, regardless of maxBufferSize
        private const int minFileRead = 10;

        private static Harmony harmony;
        private static string HarmonyID => typeof(FastLoader).FullName;

        void Awake()
        {
            if (KSPCommunityFixes.KspVersion < new Version(1, 12, 0))
            {
                Debug.Log("[KSPCF] FastLoader patch not applied, requires KSP 1.12+");
                return;
            }
            else
            {
                Debug.Log("[KSPCF] Injecting FastLoader...");
            }
                
            harmony = new Harmony(HarmonyID);

            MethodInfo m_GameDatabase_SetupMainLoaders = AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.SetupMainLoaders));
            MethodInfo t_GameDatabase_SetupMainLoaders = AccessTools.Method(typeof(FastLoader), nameof(GameDatabase_SetupMainLoaders_Prefix));
            harmony.Patch(m_GameDatabase_SetupMainLoaders, new HarmonyMethod(t_GameDatabase_SetupMainLoaders));

            MethodInfo m_GameDatabase_LoadAssetBundleObjects_MoveNext = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.LoadAssetBundleObjects)));
            MethodInfo t_GameDatabase_LoadAssetBundleObjects_MoveNext = AccessTools.Method(typeof(FastLoader), nameof(GameDatabase_LoadAssetBundleObjects_MoveNext_Prefix));
            harmony.Patch(m_GameDatabase_LoadAssetBundleObjects_MoveNext, new HarmonyMethod(t_GameDatabase_LoadAssetBundleObjects_MoveNext));

            MethodInfo m_PartLoader_StartLoad = AccessTools.Method(typeof(PartLoader), nameof(PartLoader.StartLoad));
            MethodInfo t_PartLoader_StartLoad = AccessTools.Method(typeof(FastLoader), nameof(PartLoader_StartLoad_Transpiler));
            harmony.Patch(m_PartLoader_StartLoad, null, null, new HarmonyMethod(t_PartLoader_StartLoad));

            PatchStartCoroutineInCoroutine(AccessTools.Method(typeof(PartLoader), nameof(PartLoader.CompileParts)));
            PatchStartCoroutineInCoroutine(AccessTools.Method(typeof(DragCubeSystem), nameof(DragCubeSystem.SetupDragCubeCoroutine), new[] { typeof(Part) }));
            PatchStartCoroutineInCoroutine(AccessTools.Method(typeof(DragCubeSystem), nameof(DragCubeSystem.RenderDragCubesCoroutine)));
        }

        /// <summary>
        /// Remove all harmony patches. Avoid breaking stock gamedatabase reload feature and runtime drag cube generation
        /// </summary>
        static void Unpatch()
        {
            harmony.UnpatchAll(HarmonyID);
            harmony = null;
        }

        #region Asset loader reimplementation (patches)

        static bool loadObjectsInProgress = false;

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
            throw new Exception("Terminating stock loader coroutine");
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

            return true;
        }

        #endregion

        #region Asset loader reimplementation (main coroutine)

        /// <summary>
        /// Faster than Time.realtimeSinceStartup, result is in seconds.
        /// </summary>
        static double ElapsedTime => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        static int totalAssetCount;
        static int loadedAssetCount;

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
            gdb._root = new UrlDir(gdb.urlConfig.ToArray(), configFileTypes.ToArray());
            gdb.translateLoadedNodes();

            gdb.progressTitle = "Searching assets to load...";
            yield return null;

            double nextFrameTime = ElapsedTime + minFrameTimeD;
            gdb.progressFraction = 0f;

            // Files loaded by our custom loaders
            List<UrlFile> audioFiles = new List<UrlFile>(1000);
            List<RawAsset> textureAssets = new List<RawAsset>(10000);
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
                                    textureAssets.Add(new RawAsset(file, RawAsset.AssetType.TextureDDS));
                                    break;
                                case "jpg":
                                case "jpeg":
                                    textureAssets.Add(new RawAsset(file, RawAsset.AssetType.TextureJPG));
                                    break;
                                case "mbm":
                                    textureAssets.Add(new RawAsset(file, RawAsset.AssetType.TextureMBM));
                                    break;
                                case "png":
                                    textureAssets.Add(new RawAsset(file, RawAsset.AssetType.TexturePNG));
                                    break;
                                case "tga":
                                    textureAssets.Add(new RawAsset(file, RawAsset.AssetType.TextureTGA));
                                    break;
                                case "truecolor":
                                    textureAssets.Add(new RawAsset(file, RawAsset.AssetType.TextureTRUECOLOR));
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

            gdb.progressTitle = $"Loading sound assets...";
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

                    UnityEngine.Debug.Log($"Load (Audio): {file.url}");
                    for (int k = 0; k < loadersCount; k++)
                    {
                        DatabaseLoader<AudioClip> loader = gdb.loadersAudio[k];
                        if (loader.extensions.Contains(file.fileExtension))
                        {
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
            }

            // custom wav/ogg audio loader
            int audioFilesCount = audioFiles.Count;
            int maxConcurrentCoroutines = 25;
            int j = 0;

            while (j < audioFilesCount)
            {
                if (concurrentAudioCoroutines < maxConcurrentCoroutines)
                {
                    UrlFile file = audioFiles[j];

                    if (!allAudioFiles.Add(file.url))
                    {
                        Debug.LogWarning($"Duplicate audio asset '{file.url}' with extension '{file.fileExtension}' won't be loaded");
                    }
                    else
                    {
                        Debug.Log($"Load (Audio): {file.url}");
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
            gdb.progressTitle = $"Loading texture assets...";
            yield return null;

            // call non-stock texture loaders
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

                    UnityEngine.Debug.Log($"Load (Texture): {file.url}");
                    for (int k = 0; k < loadersCount; k++)
                    {
                        DatabaseLoader<TextureInfo> loader = gdb.loadersTexture[k];
                        if (loader.extensions.Contains(file.fileExtension))
                        {
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
            }

            // call our custom texture loader
            yield return gdb.StartCoroutine(FilesLoader(textureAssets, allTextureFiles, "Loading texture asset"));

            // start model loading
            gdb.progressFraction = 0.75f;
            gdb.progressTitle = $"Loading model assets...";
            yield return null;

            // call non-stock model loaders
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

                    UnityEngine.Debug.Log($"Load (Model): {file.url}");
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

            // call our custom texture loader
            yield return gdb.StartCoroutine(FilesLoader(modelAssets, allModelFiles, "Loading model asset"));

            // all done, do some cleanup
            arrayPool = null;

            // stock stuff
            gdb.lastLoadTime = KSPUtil.SystemDateTime.DateTimeNow();
            gdb.progressFraction = 1f;
            loadObjectsInProgress = false;
        }

        #endregion

        #region Asset loader reimplementation (audio loader)

        static int concurrentAudioCoroutines;
        static int audioFilesLoaded;

        /// <summary>
        /// Concurrent coroutines (read "multiple coroutines in the same frame") audio loader
        /// </summary>
        static IEnumerator AudioLoader(UrlFile urlFile)
        {
            concurrentAudioCoroutines++;

            try
            {
                string normalizedUri = KSPUtil.ApplicationFileProtocol + new FileInfo(urlFile.fullPath).FullName;
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

                RawAsset rawAsset = null;
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

                    Debug.Log($"Load ({rawAsset.TypeName}): {rawAsset.File.url}");
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

            private static string[] assetTypeNames = new string[]
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
            private MemoryStream memoryStream;
            private BinaryReader binaryReader;
            private Result result;
            private string message;

            public UrlFile File => file;
            public Result State => result;
            public string Message => message;
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
                if (this.message == null)
                    this.message = message;
                else
                    this.message = $"{this.message}\n{message}";
            }

            private void SetWarning(string message)
            {
                if (result == Result.Failed)
                {
                    if (this.message == null)
                        this.message = message;
                    else 
                        this.message = $"{this.message}\nWARNING: {message}";
                }
                else
                {
                    result = Result.Warning;
                    if (this.message == null)
                        this.message = message;
                    else
                        this.message = $"{this.message}\n{message}";
                }
            }

            public void ReadFromDiskWorkerThread()
            {
                switch (assetType)
                {
                    case AssetType.TextureDDS:
                    case AssetType.TextureMBM:
                    case AssetType.TextureTGA:
                    case AssetType.ModelMU:
                    case AssetType.ModelDAE:
                        useRentedBuffer = true;
                        break;
                }

                try
                {
                    using (FileStream fileStream = System.IO.File.OpenRead(file.fullPath))
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

                    if (file.fileType == FileType.Texture)
                    {
                        TextureInfo textureInfo;
                        switch (assetType)
                        {
                            case AssetType.TextureDDS:
                                textureInfo = LoadDDS();
                                break;
                            case AssetType.TextureJPG:
                                textureInfo = LoadJPG();
                                break;
                            case AssetType.TextureMBM:
                                textureInfo = LoadMBM();
                                break;
                            case AssetType.TexturePNG:
                                textureInfo = LoadPNG();
                                break;
                            case AssetType.TextureTGA:
                                textureInfo = LoadTGA();
                                break;
                            case AssetType.TextureTRUECOLOR:
                                textureInfo = LoadTRUECOLOR();
                                break;
                            default:
                                SetError("Unknown texture format");
                                return;
                        }

                        if (result == Result.Failed || textureInfo == null || textureInfo.texture.IsNullOrDestroyed())
                        {
                            result = Result.Failed;
                            if (string.IsNullOrEmpty(message))
                                message = $"{TypeName} load error";
                        }
                        else
                        {
                            textureInfo.name = file.url;
                            textureInfo.texture.name = file.url;
                            Instance.databaseTexture.Add(textureInfo);
                        }
                    }
                    else if (file.fileType == FileType.Model)
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
                            if (string.IsNullOrEmpty(message))
                                message = $"{TypeName} load error";
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
                if (binaryReader != null)
                    binaryReader.Dispose();

                if (memoryStream != null)
                    memoryStream.Dispose();

                if (useRentedBuffer)
                    arrayPool.Return(buffer);
            }

            private TextureInfo LoadDDS()
            {
                memoryStream = new MemoryStream(buffer, 0, dataLength);
                binaryReader = new BinaryReader(memoryStream);

                if (binaryReader.ReadUInt32() != DDSValues.uintMagic)
                {
                    SetError("DDS: File is not a DDS format file!");
                    return null;
                }
                DDSHeader dDSHeader = new DDSHeader(binaryReader);
                if (dDSHeader.ddspf.dwFourCC == DDSValues.uintDX10)
                {
                    new DDSHeaderDX10(binaryReader);
                }
                bool mipChain = (dDSHeader.dwCaps & DDSPixelFormatCaps.MIPMAP) != 0;
                bool isNormalMap = (dDSHeader.ddspf.dwFlags & 0x80000u) != 0 || (dDSHeader.ddspf.dwFlags & 0x80000000u) != 0;
                Texture2D texture2D = null;
                uint dwFourCC = dDSHeader.ddspf.dwFourCC;


                if (dwFourCC == DDSValues.uintDXT1)
                {
                    texture2D = new Texture2D((int)dDSHeader.dwWidth, (int)dDSHeader.dwHeight, TextureFormat.DXT1, mipChain);
                    texture2D.LoadRawTextureData(binaryReader.ReadBytes((int)(binaryReader.BaseStream.Length - binaryReader.BaseStream.Position)));
                    texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                }
                else if (dwFourCC == DDSValues.uintDXT5)
                {
                    texture2D = new Texture2D((int)dDSHeader.dwWidth, (int)dDSHeader.dwHeight, TextureFormat.DXT5, mipChain);
                    texture2D.LoadRawTextureData(binaryReader.ReadBytes((int)(binaryReader.BaseStream.Length - binaryReader.BaseStream.Position)));
                    texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                }
                else if (dwFourCC == DDSValues.uintDXT2)
                {
                    SetError("DDS: DXT2 is not supported!");
                }
                else if (dwFourCC == DDSValues.uintDXT3)
                {
                    SetError("DDS: DXT3(" + dDSHeader.dwWidth + "x" + dDSHeader.dwHeight + ", MipMap=" + mipChain.ToString() + ") - DXT3 format is NOT supported. Use DXT5");
                }
                else if (dwFourCC == DDSValues.uintDXT4)
                {
                    SetError("DDS: DXT4 is not supported!");
                }
                else if (dwFourCC == DDSValues.uintDX10)
                {
                    SetError("DDS: DX10 formats not supported");
                }

                return new TextureInfo(file, texture2D, isNormalMap, false, true);
            }

            private TextureInfo LoadJPG()
            {
                bool isNormal = file.name.EndsWith("NRM");

                if (isNormal)
                {
                    Texture2D tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
                    if (!ImageConversion.LoadImage(tex, buffer, false))
                        return null;

                    Texture2D nrmTex = BitmapToCompressedNormalMapFast(tex);
                    return new TextureInfo(file, nrmTex, true, false, true);
                }
                else
                {
                    Texture2D tex = new Texture2D(1, 1, TextureFormat.DXT1, false);
                    if (!ImageConversion.LoadImage(tex, buffer, false))
                        return null;

                    return new TextureInfo(file, tex, false, true, true);
                }
            }

            private TextureInfo LoadMBM()
            {
                memoryStream = new MemoryStream(buffer, 0, dataLength);
                binaryReader = new BinaryReader(memoryStream);
                Texture2D texture2D = MBMReader.ReadTexture2D(buffer, binaryReader, true, true, out bool isNormalMap);
                return new TextureInfo(file, texture2D, isNormalMap, false, true);
            }

            static string[] noMipMapsPNGTextureNames = new string[3]
            {
                Path.DirectorySeparatorChar + "Icons" + Path.DirectorySeparatorChar,
                Path.DirectorySeparatorChar + "Tutorials" + Path.DirectorySeparatorChar,
                Path.DirectorySeparatorChar + "SimpleIcons" + Path.DirectorySeparatorChar
            };

            private TextureInfo LoadPNG()
            {
                if (!GetPNGSize(buffer, out uint width, out uint height))
                {
                    SetError("Invalid PNG file");
                    return null;
                }

                bool canCompress = width % 4 == 0 && height % 4 == 0;
                bool isNormalMap = file.name.EndsWith("NRM");
                bool hasMipMaps;
                bool nonReadable;

                if (isNormalMap)
                {
                    hasMipMaps = false;
                    nonReadable = false;
                }
                else
                {
                    hasMipMaps = true;
                    for (int i = 0; i < noMipMapsPNGTextureNames.Length; i++)
                        if (file.fullPath.Contains(noMipMapsPNGTextureNames[i]))
                            hasMipMaps = false;

                    // KSPCF optimization : don't keep thumbs in memory
                    nonReadable = file.fullPath.Contains("@thumbs");
                }

                // don't initially compress normal textures, as we need to swizzle the raw data first
                TextureFormat textureFormat;
                if (isNormalMap)
                {
                    textureFormat = TextureFormat.ARGB32;
                }
                else if (!canCompress)
                {
                    textureFormat = TextureFormat.ARGB32;
                    SetWarning($"Texture isn't eligible for DXT compression, width and height must be multiples of 4");
                }
                else
                {
                    textureFormat = TextureFormat.DXT5;
                }

                Texture2D texture = new Texture2D((int)width, (int)height, textureFormat, hasMipMaps);
                if (!ImageConversion.LoadImage(texture, buffer, nonReadable))
                    return null;

                if (isNormalMap)
                    texture = BitmapToCompressedNormalMapFast(texture);

                return new TextureInfo(file, texture, isNormalMap, !nonReadable, true);
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

            private TextureInfo LoadTGA()
            {
                if (dataLength < 18)
                {
                    SetError("TGA invalid length of only " + dataLength + "bytes");
                    return null;
                }

                TGAImage tgaImage = new TGAImage();
                TGAImage.header = new TGAHeader(buffer);
                TGAImage.colorData = tgaImage.ReadImage(TGAImage.header, buffer);
                if (TGAImage.colorData == null)
                    return null;

                Texture2D texture = tgaImage.CreateTexture(mipmap: true, linear: false, compress: true, compressHighQuality: false, allowRead: true);
                if (texture.IsNullOrDestroyed())
                    return null;

                bool isNormalMap = file.name.EndsWith("NRM");
                if (isNormalMap)
                    texture = BitmapToCompressedNormalMapFast(texture);

                return new TextureInfo(file, texture, isNormalMap, !isNormalMap, true);
            }

            private TextureInfo LoadTRUECOLOR()
            {
                bool isNormalMap = file.name.EndsWith("NRM");

                Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                if (!ImageConversion.LoadImage(texture, buffer, false))
                    return null;

                if (isNormalMap)
                    texture = BitmapToCompressedNormalMapFast(texture);

                return new TextureInfo(file, texture, isNormalMap, !isNormalMap, false);
            }

            private static void InitPartReader()
            {
                if (PartReader.matDummies == null)
                    PartReader.matDummies = new List<PartReader.MaterialDummy>();
                else
                    PartReader.matDummies.Clear();

                if (PartReader.boneDummies == null)
                    PartReader.boneDummies = new List<PartReader.BonesDummy>();
                else
                    PartReader.boneDummies.Clear();

                if (PartReader.textureDummies == null)
                    PartReader.textureDummies = new PartReader.TextureDummyList();
                else
                    PartReader.textureDummies.Clear();
            }

            private static void CleanPartReader()
            {
                PartReader.matDummies.Clear();
                PartReader.boneDummies.Clear();
                PartReader.textureDummies.Clear();
            }

            private GameObject LoadMU()
            {
                InitPartReader();
                PartReader.file = file;
                memoryStream = new MemoryStream(buffer, 0, dataLength);
                binaryReader = new BinaryReader(memoryStream);
                PartToolsLib.FileType fileType = (PartToolsLib.FileType)binaryReader.ReadInt32();
                PartReader.fileVersion = binaryReader.ReadInt32();
                _ = binaryReader.ReadString() + string.Empty;
                if (fileType != PartToolsLib.FileType.ModelBinary)
                {
                    SetError($"'{file.url}.mu' is an incorrect type.");
                    return null;
                }
                GameObject gameObject = null;
                try
                {
                    gameObject = PartReader.ReadChild(binaryReader, null);
                    if (PartReader.boneDummies.Count > 0)
                    {
                        int i = 0;
                        for (int count = PartReader.boneDummies.Count; i < count; i++)
                        {
                            Transform[] array = new Transform[PartReader.boneDummies[i].bones.Count];
                            int j = 0;
                            for (int count2 = PartReader.boneDummies[i].bones.Count; j < count2; j++)
                            {
                                array[j] = PartReader.FindChildByName(gameObject.transform, PartReader.boneDummies[i].bones[j]);
                            }
                            PartReader.boneDummies[i].smr.bones = array;
                        }
                    }
                    if (PartReader.shaderFallback)
                    {
                        Renderer[] componentsInChildren = gameObject.GetComponentsInChildren<Renderer>();
                        int k = 0;
                        for (int num = componentsInChildren.Length; k < num; k++)
                        {
                            Renderer renderer = componentsInChildren[k];
                            int l = 0;
                            for (int num2 = renderer.sharedMaterials.Length; l < num2; l++)
                            {
                                renderer.sharedMaterials[l].shader = Shader.Find("KSP/Diffuse");
                            }
                        }
                    }
                }
                finally
                {
                    CleanPartReader();
                }
                return gameObject;
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

            public static Texture2D BitmapToCompressedNormalMapFast(Texture2D original)
            {
                // Much faster than the stock BitmapToUnityNormalMap() method, going from 2.85s to 0.45s
                // Note that this would be a lot more efficient if we didn't have to create a new texture.
                // Unfortunately, Unity doesn't provide any way to add mimaps to a texture that didn't
                // have them initially (but I guess this is a deeper GPU related limitation)...

                TextureFormat originalFormat = original.format;
                Texture2D normalMap = new Texture2D(original.width, original.height, TextureFormat.RGBA32, true);
                normalMap.wrapMode = TextureWrapMode.Repeat;

                if (originalFormat == TextureFormat.RGBA32 
                    || originalFormat == TextureFormat.ARGB32
                    || originalFormat == TextureFormat.RGB24)
                {
                    NativeArray<byte> originalData = original.GetRawTextureData<byte>();
                    NativeArray<byte> normalMapData = normalMap.GetRawTextureData<byte>();
                    int size = originalData.Length;
                    byte r, g;
                    switch (originalFormat)
                    {
                        case TextureFormat.RGBA32:
                            // from (r, g, b, a)
                            // to   (g, g, g, r);
                            for (int i = 0; i < size; i += 4)
                            {
                                r = originalData[i];
                                g = originalData[i + 1];
                                normalMapData[i] = g;
                                normalMapData[i + 1] = g;
                                normalMapData[i + 2] = g;
                                normalMapData[i + 3] = r;
                            }
                            break;
                        case TextureFormat.ARGB32:
                            // from (a, r, g, b)
                            // to   (g, g, g, r);
                            for (int i = 0; i < size; i += 4)
                            {
                                r = originalData[i + 1];
                                g = originalData[i + 2];
                                normalMapData[i] = g;
                                normalMapData[i + 1] = g;
                                normalMapData[i + 2] = g;
                                normalMapData[i + 3] = r;
                            }
                            break;
                        case TextureFormat.RGB24:
                            // from (r, g, b)
                            // to   (g, g, g, r);
                            int j = 0;
                            for (int i = 0; i < size; i += 3)
                            {
                                r = originalData[i];
                                g = originalData[i + 1];
                                normalMapData[j] = g;
                                normalMapData[j + 1] = g;
                                normalMapData[j + 2] = g;
                                normalMapData[j + 3] = r;
                                j += 4;
                            }
                            break;
                    }
                }
                else
                {
                    Color32[] pixels = original.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Color32 pixel = pixels[i];
                        pixel.a = pixel.r;
                        pixel.r = pixel.g;
                        pixel.b = pixel.g;
                        pixels[i] = pixel;
                    }
                    normalMap.SetPixels32(pixels);
                }

                if (normalMap.width % 4 == 0 && normalMap.height % 4 == 0)
                {
                    normalMap.Apply(true, false);
                    normalMap.Compress(false);
                    normalMap.Apply(true, true);
                }
                else
                {
                    normalMap.Apply(true, true);
                }

                Destroy(original);
                return normalMap;
            }
        }

        #endregion

        #region PartLoader reimplementation

        static IEnumerable<CodeInstruction> PartLoader_StartLoad_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_PartLoader_CompileAll = AccessTools.Method(typeof(PartLoader), nameof(PartLoader.CompileAll));
            MethodInfo m_PartLoader_CompileAll_Modded = AccessTools.Method(typeof(FastLoader), nameof(PartLoader_CompileAll));
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

        static IEnumerator PartLoader_CompileAll()
        {
            PartLoader instance = PartLoader.Instance;

            if (instance._recompile)
            {
                instance.ClearAll();
            }
            instance.progressTitle = "";
            instance.progressFraction = 0f;
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
            UrlConfig[] configs = GameDatabase.Instance.GetConfigs("PART");
            UrlConfig[] allPropNodes = GameDatabase.Instance.GetConfigs("PROP");
            UrlConfig[] allSpaceNodes = GameDatabase.Instance.GetConfigs("INTERNAL");
            UrlConfig[] configs2 = GameDatabase.Instance.GetConfigs("VARIANTTHEME");
            int num = configs.Length + allPropNodes.Length + allSpaceNodes.Length;
            instance.progressDelta = 1f / (float)num;
            instance.InitializePartDatabase();
            instance.APFinderByIcon.Clear();
            instance.APFinderByName.Clear();
            instance.CompileVariantThemes(configs2);

            IEnumerator compilePartsEnumerator = FrameUnlockedCoroutine(instance.CompileParts(configs));
            while (compilePartsEnumerator.MoveNext())
                yield return null;

            IEnumerator compileInternalPropsEnumerator = FrameUnlockedCoroutine(instance.CompileInternalProps(allPropNodes));
            while (compileInternalPropsEnumerator.MoveNext())
                yield return null;

            IEnumerator compileInternalSpacesEnumerator = FrameUnlockedCoroutine(instance.CompileInternalSpaces(allSpaceNodes));
            while (compileInternalSpacesEnumerator.MoveNext())
                yield return null;

            Unpatch();

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
        static void PatchStartCoroutineInCoroutine(MethodInfo coroutine)
        {
            MethodInfo t_StartCoroutinePassThroughTranspiler = AccessTools.Method(typeof(FastLoader), nameof(StartCoroutinePassThroughTranspiler));
            harmony.Patch(AccessTools.EnumeratorMoveNext(coroutine), null, null, new HarmonyMethod(t_StartCoroutinePassThroughTranspiler));
        }

        /// <summary>
        /// Transpiler for the PatchStartCoroutineInCoroutine() method.
        /// </summary>
        static IEnumerable<CodeInstruction> StartCoroutinePassThroughTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo m_StartCoroutine = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new[] { typeof(IEnumerator) });
            MethodInfo m_StartCoroutinePassThrough = AccessTools.Method(typeof(FastLoader), nameof(StartCoroutinePassThrough));

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
                    moveNext = false;
                }

                while (moveNext)
                {
                    if (Time.realtimeSinceStartup > nextFrameTime)
                    {
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
                        moveNext = false;
                    }
                }
            }
        }

        #endregion
    }
}
