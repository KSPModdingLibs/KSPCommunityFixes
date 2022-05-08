using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace KSPCommunityFixes
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class TextureLoaderOptimizations : MonoBehaviour
    {
        private static TextureLoaderOptimizations instance;

        public static bool IsPatchEnabled { get; private set; }
        public static bool textureCacheEnabled;
        private static string configPath;
        private static bool isSetPixelDataSupported;

        private Stopwatch cacheLoadingWatch = new Stopwatch();

        private bool userOptInChoiceDone;
        private string modPath;
        private string pluginDataPath;
        private string textureCachePath;
        private string textureCacheDataPath;
        private string textureProgressMarkerPath;

        private Dictionary<string, CachedTextureInfo> textureCacheData;
        private HashSet<uint> textureDataIds;
        private bool cacheUpdated = false;
        private int loadedTexturesCount = 0;

        void Awake()
        {
            // We rely on Texture2D.SetPixelData(), which doesn't exists before Unity 2019.4, so we only support KSP 1.12
            Version kspVersion = new Version(Versioning.version_major, Versioning.version_minor, Versioning.Revision);
            Version minVersion = new Version(1, 10, 0);
            Version unity2019_4Version = new Version(1, 12, 0);

            IsPatchEnabled = kspVersion >= minVersion;
            isSetPixelDataSupported = kspVersion >= unity2019_4Version;

            if (!IsPatchEnabled)
            {
                Destroy(this);
                return;
            }

            instance = this;

            Harmony harmony = new Harmony("TextureLoaderOptimizations");

            harmony.Patch(
                AccessTools.Method(typeof(DatabaseLoaderTexture_PNG), nameof(DatabaseLoaderTexture_PNG.Load)),
                new HarmonyMethod(AccessTools.Method(typeof(TextureLoaderOptimizations), nameof(DatabaseLoaderTexture_PNG_Load_Prefix))));

            modPath = Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            pluginDataPath = Path.Combine(modPath, "PluginData");
            configPath = Path.Combine(pluginDataPath, nameof(TextureLoaderOptimizations) + ".cfg");
            textureCachePath = Path.Combine(pluginDataPath, "TextureCache");

            if (File.Exists(configPath))
            {
                ConfigNode config = ConfigNode.Load(configPath);

                if (!config.TryGetValue(nameof(userOptInChoiceDone), ref userOptInChoiceDone))
                    userOptInChoiceDone = false;

                if (!config.TryGetValue(nameof(textureCacheEnabled), ref textureCacheEnabled))
                    userOptInChoiceDone = false;
            }

#if DEBUG
            userOptInChoiceDone = true;
            textureCacheEnabled = false;
#endif

            if (userOptInChoiceDone)
            {
                if (textureCacheEnabled)
                    SetupTextureCache();
            }
            else
            {
                SetupTextureCache();
            }

            GameEvents.OnGameDatabaseLoaded.Add(OnGameDatabaseLoaded);
        }

        private void SetupTextureCache()
        {

            textureCacheDataPath = Path.Combine(textureCachePath, "textureData.json");
            textureProgressMarkerPath = Path.Combine(textureCachePath, "progressMarker");

            textureCacheData = new Dictionary<string, CachedTextureInfo>(2000);
            textureDataIds = new HashSet<uint>(2000);

            if (Directory.Exists(textureCachePath))
            {
                if (File.Exists(textureProgressMarkerPath))
                {
                    // If progress marker is still here, the game somehow crashed during loading on
                    // the previous run, so we delete the whole cache to avoid orphan cached texture
                    // files from lying around
                    Directory.Delete(textureCachePath, true);
                    Directory.CreateDirectory(textureCachePath);
                }
                else if (File.Exists(textureCacheDataPath))
                {
                    string[] textureCacheDataContent = File.ReadAllLines(textureCacheDataPath);
                    foreach (string json in textureCacheDataContent)
                    {
                        CachedTextureInfo cachedTextureInfo = JsonUtility.FromJson<CachedTextureInfo>(json);
                        textureCacheData.Add(cachedTextureInfo.name, cachedTextureInfo);
                        textureDataIds.Add(cachedTextureInfo.id);
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(textureCachePath);
            }

            File.WriteAllText(textureProgressMarkerPath, string.Empty);
        }



        private void OnGameDatabaseLoaded()
        {
            if (!userOptInChoiceDone || !textureCacheEnabled)
            {
                if (Directory.Exists(textureCachePath))
                    Directory.Delete(textureCachePath, true);
            }
            else
            {
                if (loadedTexturesCount != textureCacheData.Count)
                {
                    cacheUpdated = true;
                    foreach (CachedTextureInfo cachedTextureInfo in textureCacheData.Values)
                    {
                        if (!cachedTextureInfo.loaded)
                        {
                            File.Delete(Path.Combine(textureCachePath, cachedTextureInfo.id.ToString()));
                        }
                    }
                }

                if (cacheUpdated)
                {
                    File.Delete(textureCacheDataPath);

                    List<string> textureCacheDataContent = new List<string>(textureCacheData.Count);

                    foreach (CachedTextureInfo cachedTextureInfo in textureCacheData.Values)
                    {
                        if (cachedTextureInfo.loaded)
                        {
                            cachedTextureInfo.loaded = false;
                            textureCacheDataContent.Add(JsonUtility.ToJson(cachedTextureInfo));
                        }
                    }

                    File.WriteAllLines(textureCacheDataPath, textureCacheDataContent);
                }

                File.Delete(textureProgressMarkerPath);

                if (loadedTexturesCount > 0)
                {
                    Debug.Log($"[KSPCF:TextureLoaderOptimizations] Loaded {loadedTexturesCount} PNG textures from cache in {cacheLoadingWatch.Elapsed.TotalSeconds:F3}s");
                }

                cacheLoadingWatch.Reset();
                cacheUpdated = false;
                loadedTexturesCount = 0;
            }

            Destroy(this);
        }

        void OnDestroy()
        {
            instance = null;
            GameEvents.OnGameDatabaseLoaded.Remove(OnGameDatabaseLoaded);
        }

        #region PNG texture loader optimizations

        [Serializable]
        public class CachedTextureInfo
        {
            private static Random random = new Random();

            public string name;
            public uint id;
            public long creationTime;
            public int width;
            public int height;
            public int mipCount;
            public bool readable;
            public bool normal;
            [NonSerialized] public bool loaded = false;

            public CachedTextureInfo() { }

            public CachedTextureInfo(UrlDir.UrlFile urlFile, FileInfo file, Texture2D texture, bool isNormalMap)
            {
                name = urlFile.url;
                do
                {
                    unchecked
                    {
                        id = (uint)random.Next();
                    }
                }
                while (instance.textureDataIds.Contains(id));

                creationTime = file.CreationTimeUtc.ToFileTimeUtc();
                width = texture.width;
                height = texture.height;
                mipCount = texture.mipmapCount;
                normal = isNormalMap;
                readable = !isNormalMap && !name.Contains("@thumbs");
                loaded = true;
            }

            public void SaveRawTextureData(Texture2D texture)
            {
                byte[] rawData = texture.GetRawTextureData();
                File.WriteAllBytes(Path.Combine(instance.textureCachePath, id.ToString()), rawData);
            }

            public bool TryCreateTexture(out Texture2D texture)
            {
                try
                {
                    texture = new Texture2D(width, height, GraphicsFormat.RGBA_DXT5_UNorm, mipCount, mipCount == 1 ? TextureCreationFlags.None : TextureCreationFlags.MipChain);
                    byte[] rawData = File.ReadAllBytes(Path.Combine(instance.textureCachePath, id.ToString()));
                    texture.LoadRawTextureData(rawData);
                    texture.Apply(false, !readable);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[KSPCF:TextureLoaderOptimizations] Failed to load cached texture data for {name}\n{e}");
                    texture = null;
                    return false;
                }
            }
        }

        static bool DatabaseLoaderTexture_PNG_Load_Prefix(DatabaseLoaderTexture_PNG __instance, UrlDir.UrlFile urlFile, FileInfo file, out IEnumerator __result)
        {
            //__result = null;
            //return true;

            __result = GetEnumerator(__instance, urlFile, file);
            return false;
        }

        static IEnumerator GetEnumerator(DatabaseLoaderTexture_PNG loader, UrlDir.UrlFile urlFile, FileInfo file)
        {
            if (!instance.userOptInChoiceDone)
                yield return instance.StartCoroutine(WaitForUserOptIn());

            CachedTextureInfo cachedTextureInfo;

            if (textureCacheEnabled)
            {
                instance.cacheLoadingWatch.Start();
                if (instance.textureCacheData.TryGetValue(urlFile.url, out cachedTextureInfo))
                {
                    if (cachedTextureInfo.creationTime != file.CreationTimeUtc.ToFileTimeUtc())
                    {
                        instance.textureCacheData.Remove(urlFile.url);
                        instance.textureDataIds.Remove(cachedTextureInfo.id);
                        File.Delete(Path.Combine(instance.textureCachePath, cachedTextureInfo.id.ToString()));
                        instance.cacheUpdated = true;
                    }
                    else if (cachedTextureInfo.TryCreateTexture(out Texture2D cachedTexture))
                    {
                        instance.loadedTexturesCount++;
                        cachedTextureInfo.loaded = true;
                        GameDatabase.TextureInfo textureInfo = new GameDatabase.TextureInfo(urlFile, cachedTexture, cachedTextureInfo.normal, cachedTextureInfo.readable, true);
                        loader.obj = textureInfo;
                        loader.successful = true;
                        instance.cacheLoadingWatch.Stop();
                        yield break;
                    }
                }
                instance.cacheLoadingWatch.Stop();
            }

            string path = KSPUtil.ApplicationFileProtocol + file.FullName;
            UnityWebRequest imageWWW = UnityWebRequestTexture.GetTexture(path);
            yield return imageWWW.SendWebRequest();
            while (!imageWWW.isDone)
            {
                yield return null;
            }

            if (imageWWW.error != null)
            {
                Debug.LogWarning("Texture load error in '" + file.FullName + "': " + imageWWW.error);
                loader.obj = null;
                loader.successful = false;
                yield break;
            }

            if (Path.GetFileNameWithoutExtension(file.Name).EndsWith("NRM"))
            {
                Texture2D content = DownloadHandlerTexture.GetContent(imageWWW);
                Texture2D normal;
                bool normalIsCompressed;

                if (isSetPixelDataSupported)
                    normalIsCompressed = BitmapToCompressedNormalMapFast(content, !textureCacheEnabled, out normal);
                else
                    normalIsCompressed = BitmapToCompressedNormalMapFast_Legacy(content, !textureCacheEnabled, out normal);

                if (normalIsCompressed && textureCacheEnabled)
                {
                    if (normal.graphicsFormat == GraphicsFormat.RGBA_DXT5_UNorm)
                    {
                        cachedTextureInfo = new CachedTextureInfo(urlFile, file, normal, true);
                        cachedTextureInfo.SaveRawTextureData(normal);
                        instance.textureCacheData.Add(cachedTextureInfo.name, cachedTextureInfo);
                        instance.textureDataIds.Add(cachedTextureInfo.id);
                        instance.cacheUpdated = true;
                        Debug.Log($"[KSPCF:TextureLoaderOptimizations] PNG normal map texture {urlFile.url} was converted to DXT5 and has been cached for future reloads");
                    }

                    normal.Apply(false, true);
                }

                GameDatabase.TextureInfo textureInfo = new GameDatabase.TextureInfo(urlFile, normal, isNormalMap: true, isReadable: false, isCompressed: true);
                loader.obj = textureInfo;
                loader.successful = true;
                yield break;
            }

            Texture2D texture2D = DownloadHandlerTexture.GetContent(imageWWW);
            string[] array = new string[3] { "Icons", "Tutorials", "SimpleIcons" };
            bool flag = false;
            bool flag2 = false;
            for (int i = 0; i < array.Length; i++)
            {
                if (path.Contains(Path.DirectorySeparatorChar + array[i] + Path.DirectorySeparatorChar))
                {
                    flag = true;
                }
            }

            if (path.Contains(Path.DirectorySeparatorChar + "Flags" + Path.DirectorySeparatorChar))
            {
                Texture2D texture2D2 = new Texture2D(texture2D.width, texture2D.height, texture2D.format, mipChain: true);
                texture2D2.LoadImage(imageWWW.downloadHandler.data);
                texture2D = texture2D2;
                flag2 = true;
            }

            if (flag)
            {
                Texture2D texture2D3 = new Texture2D(texture2D.width, texture2D.height, TextureFormat.ARGB32, mipChain: false);
                texture2D3.SetPixels32(texture2D.GetPixels32());
                texture2D = texture2D3;
            }

            if (texture2D.width % 4 == 0 && texture2D.height % 4 == 0)
            {
                texture2D.Compress(flag2 ? true : false);
            }
            else
            {
                Debug.LogWarning("Texture resolution is not valid for compression: '" + file.FullName + "' - consider changing the image's width and height to enable compression");
            }

            texture2D.Apply(updateMipmaps: true);
            loader.obj = new GameDatabase.TextureInfo(urlFile, texture2D, isNormalMap: false, isReadable: true, isCompressed: true);
            loader.successful = true;

            if (textureCacheEnabled && texture2D.isReadable && texture2D.graphicsFormat == GraphicsFormat.RGBA_DXT5_UNorm)
            {
                cachedTextureInfo = new CachedTextureInfo(urlFile, file, texture2D, false);
                cachedTextureInfo.SaveRawTextureData(texture2D);
                instance.textureCacheData.Add(cachedTextureInfo.name, cachedTextureInfo);
                instance.textureDataIds.Add(cachedTextureInfo.id);
                instance.cacheUpdated = true;
                Debug.Log($"[KSPCF:TextureLoaderOptimizations] PNG texture {urlFile.url} was converted to DXT5 and has been cached for future reloads");
            }
        }

        public static bool BitmapToCompressedNormalMapFast(Texture2D original, bool makeNoLongerReadable, out Texture2D normal)
        {
            // much faster, going from 2.85s to 0.45s
            // Not sure if that could happen, but in case we don't get those formats, just
            // let the stock getpixel based code run.
            if (original.format != TextureFormat.RGBA32 && original.format != TextureFormat.ARGB32)
            {
                normal = GameDatabase.BitmapToUnityNormalMap(original);
                return false;
            }

            normal = new Texture2D(original.width, original.height, TextureFormat.RGBA32, true);
            normal.wrapMode = TextureWrapMode.Repeat;
            NativeArray<byte> rawData = original.GetRawTextureData<byte>();
            int size = rawData.Length;

            byte[] swizzledData = new byte[size];
            byte g;

            switch (original.format)
            {
                case TextureFormat.RGBA32:
                    // from (r, g, b, a)
                    // to   (g, g, g, r);
                    for (int i = 0; i < size; i += 4)
                    {
                        g = rawData[i + 1];
                        swizzledData[i] = g;
                        swizzledData[i + 1] = g;
                        swizzledData[i + 2] = g;
                        swizzledData[i + 3] = rawData[i];
                    }
                    break;
                case TextureFormat.ARGB32:
                    // from (a, r, g, b)
                    // to   (g, g, g, r);
                    for (int i = 0; i < size; i += 4)
                    {
                        g = rawData[i + 2];
                        swizzledData[i] = g;
                        swizzledData[i + 1] = g;
                        swizzledData[i + 2] = g;
                        swizzledData[i + 3] = rawData[i + 1];
                    }
                    break;
            }

            normal.SetPixelData(swizzledData, 0);

                if (normal.width % 4 == 0 && normal.height % 4 == 0)
            {
                normal.Apply(true, false);
                normal.Compress(false);
                normal.Apply(true, makeNoLongerReadable);
                Destroy(original);
                return true;
            }

            normal.Apply(true, true);
            Destroy(original);
            return false;
        }

        public static bool BitmapToCompressedNormalMapFast_Legacy(Texture2D original, bool makeNoLongerReadable, out Texture2D normal)
        {
            if (original.format != TextureFormat.RGBA32 && original.format != TextureFormat.ARGB32)
            {
                normal = GameDatabase.BitmapToUnityNormalMap(original);
                return false;
            }

            normal = new Texture2D(original.width, original.height, TextureFormat.RGBA32, true);
            normal.wrapMode = TextureWrapMode.Repeat;
            NativeArray<byte> rawData = original.GetRawTextureData<byte>();
            int size = rawData.Length;

            int colorSize = size / 4;
            Color32[] swizzledData = new Color32[colorSize];
            byte g;
            int j = 0;

            switch (original.format)
            {
                case TextureFormat.RGBA32:
                    // from (r, g, b, a)
                    // to   (g, g, g, r);
                    for (int i = 0; i < size; i += 4)
                    {
                        g = rawData[i + 1];
                        swizzledData[j] = new Color32(g, g, g, rawData[i]);
                        j++;
                    }
                    break;
                case TextureFormat.ARGB32:
                    // from (a, r, g, b)
                    // to   (g, g, g, r);
                    for (int i = 0; i < size; i += 4)
                    {
                        g = rawData[i + 2];
                        swizzledData[j] = new Color32(g, g, g, rawData[i + 1]);
                        j++;
                    }
                    break;
            }

            normal.SetPixels32(swizzledData, 0);

            if (normal.width % 4 == 0 && normal.height % 4 == 0)
            {
                normal.Apply(true, false);
                normal.Compress(false);
                normal.Apply(true, makeNoLongerReadable);
                Destroy(original);
                return true;
            }

            normal.Apply(true, true);
            Destroy(original);
            return false;
        }

        #endregion

        private static IEnumerator WaitForUserOptIn()
        {
            long cacheSize = 0;
            long normalsSize = 0;
            int textureCount = 0;
            foreach (UrlDir.UrlFile textureFile in GameDatabase.Instance.root.GetFiles(UrlDir.FileType.Texture))
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
                "KSPCommunityFixes can cache converted PNG textures on disk to speed up loading time.\n\n" +
                $"In your current install, this should reduce future loading time by about <b><color=#FF8000>{loadingTimeReduction:F0} seconds</color></b>.\n\n" +
                $"However, this will use about <b><color=#FF8000>{cacheSize / 1024.0 / 1024.0:F0} MB</color></b> of additional disk space, and potentially much more if you install additional mods.\n\n" +
                "You can change this setting later in the in-game settings menu\n\n" +
                "<align=\"center\">Do you want to enable this optimization ?\n";

            string cacheSizeMb = (cacheSize / 1024.0 / 1024.0).ToString("F0") + "Mb";
            bool? choosed = null;
            bool dismissed = false;
            MultiOptionDialog dialog = new MultiOptionDialog("TextureLoaderOptimizations",
                desc,
                "KSPCommunityFixes",
                HighLogic.UISkin, 350f,
                new DialogGUIButton("Yes", delegate { SetOptIn(true, ref choosed); }),
                new DialogGUIButton("No", delegate { SetOptIn(false, ref choosed); }));
            PopupDialog popup = PopupDialog.SpawnPopupDialog(dialog, false, HighLogic.UISkin, false);
            popup.OnDismiss = () => dismissed = true;

            while (choosed == null)
            {
                // prevent the user being able to skip choosing by "ESC closing" the dialog
                if (dismissed)
                {
                    yield return instance.StartCoroutine(WaitForUserOptIn());
                    yield break;
                }

                yield return null;
            }
        }

        private static void SetOptIn(bool optIn, ref bool? choosed)
        {
            instance.userOptInChoiceDone = true;
            textureCacheEnabled = optIn;
            choosed = true;

            ConfigNode config = new ConfigNode();
            config.AddValue(nameof(userOptInChoiceDone), true);
            config.AddValue(nameof(textureCacheEnabled), optIn);
            config.Save(configPath);
        }

        internal static void OnToggleCacheFromSettings(bool cacheEnabled)
        {
            textureCacheEnabled = cacheEnabled;
            ConfigNode config = new ConfigNode();
            config.AddValue(nameof(userOptInChoiceDone), true);
            config.AddValue(nameof(textureCacheEnabled), cacheEnabled);
            config.Save(configPath);
        }

        private static readonly string flagsPath = Path.DirectorySeparatorChar + "Flags" + Path.DirectorySeparatorChar;

        private static bool GetPngCacheSize(string path, out int cacheSize, out bool isNormal)
        {
            isNormal = false;
            cacheSize = 0;

            if (!GetPngSize(path, out int width, out int height))
                return false;

            if (width % 4 != 0 || height % 4 != 0)
                return false;

            cacheSize = width * height;

            isNormal = Path.GetFileNameWithoutExtension(path).EndsWith("NRM");

            // if has mipmaps, about 30% larger file size
            if (isNormal || path.Contains(flagsPath))
                cacheSize = (int)(cacheSize * 1.3);

            return true;
        }

        private static int GetDefaultMipMapCount(int height, int width)
        {
            return 1 + (int)(Math.Floor(Math.Log(Math.Max(width, height), 2.0)));
        }

        private static readonly byte[] pngMagicBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static bool GetPngSize(string path, out int width, out int height)
        {
            try
            {
                using (BinaryReader binaryReader = new BinaryReader(File.OpenRead(path)))
                {
                    for (int i = 0; i < pngMagicBytes.Length; i++)
                    {
                        if (binaryReader.ReadByte() != pngMagicBytes[i])
                        {
                            width = height = 0;
                            return false;
                        }
                    }

                    binaryReader.ReadBytes(8);
                    byte[] bytes = new byte[sizeof(int)];

                    for (int j = 0; j < sizeof(int); j++)
                        bytes[sizeof(int) - 1 - j] = binaryReader.ReadByte();

                    width = BitConverter.ToInt32(bytes, 0);

                    for (int j = 0; j < sizeof(int); j++)
                        bytes[sizeof(int) - 1 - j] = binaryReader.ReadByte();

                    height = BitConverter.ToInt32(bytes, 0);
                }

                return true;
            }
            catch
            {
                width = height = 0;
                return false;
            }
        }
    }
}
