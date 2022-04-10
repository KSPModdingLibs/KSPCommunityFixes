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

        private Stopwatch cacheLoadingWatch = new Stopwatch();

        private string modPath;
        private string textureCachePath;
        private string textureCacheDataPath;
        private string textureProgressMarkerPath;

        private Dictionary<string, CachedTextureInfo> textureCacheData;
        private HashSet<uint> textureDataIds;
        private bool cacheUpdated = false;
        private int loadedTexturesCount = 0;

        void Awake()
        {
            // DatabaseLoaderTexture_PNG has been modified quite a lot between 1.8, 1.9 and 1.10
            // We only support the latest revision
            Version kspVersion = new Version(Versioning.version_major, Versioning.version_minor, Versioning.Revision);
            Version minVersion = new Version(1, 10, 0);

            if (kspVersion < minVersion)
            {
                Destroy(this);
                return;
            }

            instance = this;

            Harmony harmony = new Harmony("TextureLoaderOptimizations");
            modPath = Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

            harmony.Patch(
                AccessTools.Method(typeof(DatabaseLoaderTexture_PNG), nameof(DatabaseLoaderTexture_PNG.Load)),
                new HarmonyMethod(AccessTools.Method(typeof(TextureLoaderOptimizations), nameof(DatabaseLoaderTexture_PNG_Load_Prefix))));

            textureCachePath = Path.Combine(modPath, "PluginData", "TextureCache");
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

            GameEvents.OnGameDatabaseLoaded.Add(OnGameDatabaseLoaded);
        }

        private void OnGameDatabaseLoaded()
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

            Destroy(this);
        }

        void OnDestroy()
        {
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
            [NonSerialized] public bool loaded = false;

            public CachedTextureInfo() { }

            public CachedTextureInfo(UrlDir.UrlFile urlFile, FileInfo file, Texture2D texture)
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
                readable = !name.Contains("@thumbs");
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
            __result = GetEnumerator(__instance, urlFile, file);
            return false;
        }

        static IEnumerator GetEnumerator(DatabaseLoaderTexture_PNG loader, UrlDir.UrlFile urlFile, FileInfo file)
        {
            instance.cacheLoadingWatch.Start();
            if (instance.textureCacheData.TryGetValue(urlFile.url, out CachedTextureInfo cachedTextureInfo))
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
                    GameDatabase.TextureInfo textureInfo = new GameDatabase.TextureInfo(urlFile, cachedTexture, false, cachedTextureInfo.readable, true);
                    loader.obj = textureInfo;
                    loader.successful = true;
                    instance.cacheLoadingWatch.Stop();
                    yield break;
                }
            }
            instance.cacheLoadingWatch.Stop();

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
                Texture2D normal = BitmapToUnityNormalMapFast(content);
                GameDatabase.TextureInfo textureInfo = new GameDatabase.TextureInfo(urlFile, normal, isNormalMap: true, isReadable: false, isCompressed: true);
                loader.obj = textureInfo;
                loader.successful = true;

                // note : compressing and caching normal maps would be beneficial from a performance and memory usage PoV,
                // but testing show a noticeable quality drop (for example on the SP25 structural panel gold foil variant)
                // Given that there are only a handful of PNG normal maps in stock, this doesn't matter much anyway.

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

            if (texture2D.isReadable && texture2D.graphicsFormat == GraphicsFormat.RGBA_DXT5_UNorm)
            {
                cachedTextureInfo = new CachedTextureInfo(urlFile, file, texture2D);
                cachedTextureInfo.SaveRawTextureData(texture2D);
                instance.textureCacheData.Add(cachedTextureInfo.name, cachedTextureInfo);
                instance.textureDataIds.Add(cachedTextureInfo.id);
                instance.cacheUpdated = true;
                Debug.Log($"[KSPCF:TextureLoaderOptimizations] PNG texture {urlFile.url} was converted to DXT5 and has been cached for future reloads");
            }
        }

        public static Texture2D BitmapToUnityNormalMapFast(Texture2D tex)
        {
            // much faster, going from 2.85s to 0.45s
            int size = tex.width * tex.height;
            NativeArray<Color32> rawData = tex.GetRawTextureData<Color32>();
            Color32[] finalData = new Color32[size];
            byte g, r;
            for (int i = 0; i < size; i++)
            {
                g = rawData[i].g;
                r = rawData[i].r;
                finalData[i] = new Color32(g, g, g, r);
            }

            Texture2D texture2D = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, true);
            texture2D.wrapMode = TextureWrapMode.Repeat;
            texture2D.SetPixels32(finalData);
            texture2D.Apply(true, true);

            Destroy(tex);
            return texture2D;
        }

        #endregion
    }
}
