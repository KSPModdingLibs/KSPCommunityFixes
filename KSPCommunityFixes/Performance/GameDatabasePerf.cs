using System;
using System.Collections.Generic;
using UnityEngine;
using static GameDatabase;
using static UrlDir;

namespace KSPCommunityFixes.Performance
{
    // see https://github.com/KSPModdingLibs/KSPCommunityFixes/pull/269
    // this patch is applied manually on launch from the FastLoader patch.
    // It has no entry in settings.cfg and can't be disabled.
    [ManualPatch]
    internal class GameDatabasePerf : BasePatch
    {
        protected override bool IgnoreConfig => true;

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(GameDatabase), nameof(GameDatabase.GetModelPrefab));
            AddPatch(PatchType.Override, typeof(GameDatabase), nameof(GameDatabase.GetModelPrefabIn));
            AddPatch(PatchType.Override, typeof(GameDatabase), nameof(GameDatabase.GetModel));
            AddPatch(PatchType.Override, typeof(GameDatabase), nameof(GameDatabase.GetModelIn));
            AddPatch(PatchType.Override, typeof(GameDatabase), nameof(GameDatabase.GetModelFile), new[] { typeof(GameObject) });
            // we don't patch the GetModelFile(string) variant as it would require an additional dictionary,
            // is unused in stock and very unlikely to ever be used by anyone.

            AddPatch(PatchType.Override, typeof(GameDatabase), nameof(GameDatabase.GetTextureInfo));
            AddPatch(PatchType.Override, typeof(GameDatabase), nameof(GameDatabase.GetTextureInfoIn));
            AddPatch(PatchType.Override, typeof(GameDatabase), nameof(GameDatabase.GetTexture));
            AddPatch(PatchType.Override, typeof(GameDatabase), nameof(GameDatabase.GetTextureIn));
            // For the same reasons, we don't patch GetTextureFile(string)
        }

        static GameObject GameDatabase_GetModelPrefab_Override(GameDatabase gdb, string url)
        {
            if (url == null)
                return null;

            if (!KSPCFFastLoader.modelsByUrl.TryGetValue(url, out GameObject result))
            {
                // We need a fallback because models are also added from asset bundles, 
                // and because anyone could be adding models as the databaseModel list is public
                List<GameObject> models = gdb.databaseModel;
                for (int i = models.Count; i-- > 0;)
                {
                    if (models[i].name == url)
                    {
                        result = models[i];
                        KSPCFFastLoader.modelsByUrl.Add(url, result);
                        break;
                    }
                }
            }

            return result;
        }

        static GameObject GameDatabase_GetModelPrefabIn_Override(GameDatabase gdb, string url)
        {
            if (url == null)
                return null;

            if (!KSPCFFastLoader.modelsByDirectoryUrl.TryGetValue(url, out GameObject result))
            {
                // We need a fallback because models are also added from asset bundles, 
                // and because anyone could be adding models as the databaseModel list is public
                List<GameObject> models = gdb.databaseModel;
                for (int i = models.Count; i-- > 0;)
                {
                    string modelName = models[i].name;
                    if (modelName.Substring(0, modelName.LastIndexOf('/')) == url)
                    {
                        result = models[i];
                        KSPCFFastLoader.modelsByDirectoryUrl.Add(url, result);
                        break;
                    }
                }
            }

            return result;
        }

        static GameObject GameDatabase_GetModel_Override(GameDatabase gdb, string url)
        {
            GameObject prefab = GameDatabase_GetModelPrefab_Override(gdb, url);
            if (prefab.IsNullRef())
                return null;

            return UnityEngine.Object.Instantiate(prefab);
        }

        static GameObject GameDatabase_GetModelIn_Override(GameDatabase gdb, string url)
        {
            GameObject prefab = GameDatabase_GetModelPrefabIn_Override(gdb, url);
            if (prefab.IsNullRef())
                return null;

            return UnityEngine.Object.Instantiate(prefab);
        }

        static UrlFile GameDatabase_GetModelFile_Override(GameDatabase gdb, GameObject modelPrefab)
        {
            if (modelPrefab.IsNullRef())
                return null;

            if (!KSPCFFastLoader.urlFilesByModel.TryGetValue(modelPrefab, out UrlFile result))
            {
                // We need a fallback because models are also added from asset bundles, 
                // and because anyone could be adding models as the databaseModel list is public
                List<GameObject> models = gdb.databaseModel;
                for (int i = models.Count; i-- > 0;)
                {
                    if (models[i] == modelPrefab)
                    {
                        result = gdb.databaseModelFiles[i];
                        KSPCFFastLoader.urlFilesByModel.Add(modelPrefab, result);
                        break;
                    }
                }
            }

            return result;
        }

        internal static int txcallCount;
        internal static int txMissCount;

        static TextureInfo GameDatabase_GetTextureInfo_Override(GameDatabase gdb, string url)
        {
            txcallCount++;
            if (url == null)
                return null;

            if (gdb.flagSwaps.TryGetValue(url, out string newUrl))
                url = newUrl;

            if (!KSPCFFastLoader.texturesByUrl.TryGetValue(url, out TextureInfo result))
            {
                for (int i = gdb.databaseTexture.Count; i-- > 0;)
                {
                    if (string.Equals(url, gdb.databaseTexture[i].name, StringComparison.OrdinalIgnoreCase))
                    {
                        result = gdb.databaseTexture[i];
                        KSPCFFastLoader.texturesByUrl.Add(url, result);
                        txMissCount++;
                        break;
                    }
                }
            }
            return result;
        }

        static TextureInfo GameDatabase_GetTextureInfoIn_Override(GameDatabase gdb, string url, string textureName)
        {
            txcallCount++;
            if (url == null || textureName == null)
                return null;

            url = url.Substring(0, url.LastIndexOf('/') + 1) + textureName;

            if (gdb.flagSwaps.TryGetValue(url, out string newUrl))
                url = newUrl;

            if (!KSPCFFastLoader.texturesByUrl.TryGetValue(url, out TextureInfo result))
            {
                for (int i = gdb.databaseTexture.Count; i-- > 0;)
                {
                    if (string.Equals(url, gdb.databaseTexture[i].name, StringComparison.OrdinalIgnoreCase))
                    {
                        result = gdb.databaseTexture[i];
                        KSPCFFastLoader.texturesByUrl.Add(url, result);
                        txMissCount++;
                        break;
                    }
                }
            }

            return result;
        }

        static Texture2D GameDatabase_GetTexture_Override(GameDatabase gdb, string url, bool asNormalMap)
        {
            txcallCount++;
            if (url == null)
                return null;

            if (gdb.flagSwaps.TryGetValue(url, out string newUrl))
                url = newUrl;

            if (!KSPCFFastLoader.texturesByUrl.TryGetValue(url, out TextureInfo textureInfo))
            {
                for (int i = gdb.databaseTexture.Count; i-- > 0;)
                {
                    if (string.Equals(url, gdb.databaseTexture[i].name, StringComparison.OrdinalIgnoreCase))
                    {
                        textureInfo = gdb.databaseTexture[i];
                        KSPCFFastLoader.texturesByUrl.Add(url, textureInfo);
                        txMissCount++;
                        break;
                    }
                }
            }

            if (textureInfo == null)
                return null;

            return asNormalMap ? textureInfo.normalMap : textureInfo.texture;
        }

        static Texture2D GameDatabase_GetTextureIn_Override(GameDatabase gdb, string url, string textureName, bool asNormalMap)
        {
            txcallCount++;
            if (url == null || textureName == null)
                return null;

            url = url.Substring(0, url.LastIndexOf('/') + 1) + textureName;

            if (gdb.flagSwaps.TryGetValue(url, out string newUrl))
                url = newUrl;

            if (!KSPCFFastLoader.texturesByUrl.TryGetValue(url, out TextureInfo textureInfo))
            {
                for (int i = gdb.databaseTexture.Count; i-- > 0;)
                {
                    if (string.Equals(url, gdb.databaseTexture[i].name, StringComparison.OrdinalIgnoreCase))
                    {
                        textureInfo = gdb.databaseTexture[i];
                        KSPCFFastLoader.texturesByUrl.Add(url, textureInfo);
                        txMissCount++;
                        break;
                    }
                }
            }

            if (textureInfo == null)
                return null;

            return asNormalMap ? textureInfo.normalMap : textureInfo.texture;
        }
    }
}
