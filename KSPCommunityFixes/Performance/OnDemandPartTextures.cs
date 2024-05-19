using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using KSP.UI.Screens;
using KSPCommunityFixes.QoL;
using UnityEngine;
using static KSP.UI.Screens.Settings.SettingsSetup;
using static KSPCommunityFixes.Performance.KSPCFFastLoader;
using static KSPCommunityFixes.QoL.NoIVA;
using static ProceduralSpaceObject;

namespace KSPCommunityFixes.Performance
{
    public class OnDemandPartTextures : BasePatch
    {
        internal static Dictionary<string, List<string>> partsTextures;
        internal static Dictionary<AvailablePart, PrefabData> prefabsData = new Dictionary<AvailablePart, PrefabData>();

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            MethodInfo m_Instantiate = null;
            foreach (MethodInfo methodInfo in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (methodInfo.IsGenericMethod
                    && methodInfo.Name == nameof(UnityEngine.Object.Instantiate) 
                    && methodInfo.GetParameters().Length == 1)
                {
                    m_Instantiate = methodInfo.MakeGenericMethod(typeof(UnityEngine.Object));
                    break;
                }
            }

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                m_Instantiate,
                this, nameof(Instantiate_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(PartLoader), nameof(PartLoader.ParsePart)),
                this));
        }

        static void Instantiate_Prefix(UnityEngine.Object original)
        {
            if (original is Part part 
                && part.partInfo != null 
                && part.partInfo.partPrefab.IsNotNullRef() // skip instantiation of the special prefabs (kerbals, flag) during loading
                && prefabsData.TryGetValue(part.partInfo, out PrefabData prefabData)
                && !prefabData.areTexturesLoaded)
            {
                // load textures
                // set them on the prefab
                prefabData.areTexturesLoaded = true;
            }
        }

        static void PartLoader_ParsePart_Postfix(AvailablePart __result)
        {
            if (!partsTextures.TryGetValue(__result.name, out List<string> textures))
                return;

            Transform modelTransform = __result.partPrefab.transform.Find("model");
            if (modelTransform == null)
                return;

            PrefabData prefabData = null;

            foreach (Renderer renderer in modelTransform.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer.sharedMaterial.IsNullOrDestroyed())
                    continue;

                Material material = renderer.sharedMaterial;
                MaterialTextures materialTextures = null;

                if (material.HasProperty("_MainTex"))
                {
                    Texture currentTex = material.GetTexture("_MainTex");
                    if (currentTex.IsNotNullRef())
                    {
                        string currentTexName = currentTex.name;
                        if (OnDemandTextureInfo.texturesByUrl.TryGetValue(currentTexName, out OnDemandTextureInfo textureInfo))
                        {
                            if (prefabData == null)
                                prefabData = new PrefabData();

                            if (materialTextures == null)
                                materialTextures = new MaterialTextures(material);

                            prefabData.materials.Add(materialTextures);
                            materialTextures.mainTex = textureInfo;
                        }

                    }
                }

                if (material.HasProperty("_BumpMap"))
                {
                    Texture currentTex = material.GetTexture("_BumpMap");
                    if (currentTex.IsNotNullRef())
                    {
                        string currentTexName = currentTex.name;
                        if (OnDemandTextureInfo.texturesByUrl.TryGetValue(currentTexName, out OnDemandTextureInfo textureInfo))
                        {
                            if (prefabData == null)
                                prefabData = new PrefabData();

                            if (materialTextures == null)
                                materialTextures = new MaterialTextures(material);

                            prefabData.materials.Add(materialTextures);
                            materialTextures.bumpMap = textureInfo;
                        }

                    }
                }

                if (material.HasProperty("_Emissive"))
                {
                    Texture currentTex = material.GetTexture("_Emissive");
                    if (currentTex.IsNotNullRef())
                    {
                        string currentTexName = currentTex.name;
                        if (OnDemandTextureInfo.texturesByUrl.TryGetValue(currentTexName, out OnDemandTextureInfo textureInfo))
                        {
                            if (prefabData == null)
                                prefabData = new PrefabData();

                            if (materialTextures == null)
                                materialTextures = new MaterialTextures(material);

                            prefabData.materials.Add(materialTextures);
                            materialTextures.emissive = textureInfo;
                        }

                    }
                }

                if (material.HasProperty("_SpecMap"))
                {
                    Texture currentTex = material.GetTexture("_SpecMap");
                    if (currentTex.IsNotNullRef())
                    {
                        string currentTexName = currentTex.name;
                        if (OnDemandTextureInfo.texturesByUrl.TryGetValue(currentTexName, out OnDemandTextureInfo textureInfo))
                        {
                            if (prefabData == null)
                                prefabData = new PrefabData();

                            if (materialTextures == null)
                                materialTextures = new MaterialTextures(material);

                            prefabData.materials.Add(materialTextures);
                            materialTextures.specMap = textureInfo;
                        }

                    }
                }
            }

            if (prefabData != null)
                prefabsData[__result] = prefabData;
        }

        private static readonly char[] textureSplitChars = { ':', ',', ';' };

        public static void GetTextures(out HashSet<string> allPartTextures)
        {
            Dictionary<string, List<string>> modelParts = new Dictionary<string, List<string>>();
            Dictionary<string, List<TextureReplacement>> partTextureReplacements = new Dictionary<string, List<TextureReplacement>>();

            foreach (UrlDir.UrlConfig urlConfig in GameDatabase.Instance.root.AllConfigs)
            {
                if (urlConfig.type != "PART")
                    continue;

                bool hasModelInModelNode = false;

                string partName = urlConfig.config.GetValue("name");
                if (partName == null)
                    continue;

                partName = partName.Replace('_', '.');

                foreach (ConfigNode partNode in urlConfig.config.nodes)
                {
                    if (partNode.name == "MODEL")
                    {
                        List<TextureReplacement> texturePaths = null;
                        foreach (ConfigNode.Value modelValue in partNode.values)
                        {
                            if (modelValue.name == "model")
                            {
                                hasModelInModelNode = true;

                                if (!modelParts.TryGetValue(modelValue.value, out List<string> parts))
                                {
                                    parts = new List<string>();
                                    modelParts[modelValue.value] = parts;
                                }

                                parts.Add(partName);
                            }
                            else if (modelValue.name == "texture")
                            {
                                string[] array = modelValue.value.Split(textureSplitChars, StringSplitOptions.RemoveEmptyEntries);
                                if (array.Length != 2)
                                    continue;

                                if (texturePaths == null)
                                    texturePaths = new List<TextureReplacement>();

                                texturePaths.Add(new TextureReplacement(array[0].Trim(), array[1].Trim()));
                            }
                        }

                        if (texturePaths != null)
                            partTextureReplacements[partName] = texturePaths;
                    }
                }

                if (!hasModelInModelNode)
                {
                    foreach (UrlDir.UrlFile urlFile in urlConfig.parent.parent.files)
                    {
                        if (urlFile.fileExtension == "mu")
                        {
                            if (!modelParts.TryGetValue(urlFile.url, out List<string> parts))
                            {
                                parts = new List<string>();
                                modelParts[urlFile.url] = parts;
                            }

                            parts.Add(partName);
                            break; // only first model found should be added
                        }
                    }
                }
            }

            partsTextures = new Dictionary<string, List<string>>(500);
            allPartTextures = new HashSet<string>(500);

            HashSet<string> allTextures = new HashSet<string>(2000);
            HashSet<string> allReplacedTextures = new HashSet<string>(500);
            List<string> texturePathsBuffer = new List<string>();
            List<string> textureFileNameBuffer = new List<string>();

            foreach (UrlDir.UrlFile urlFile in GameDatabase.Instance.root.AllFiles)
            {
                if (urlFile.fileType == UrlDir.FileType.Texture)
                {
                    allTextures.Add(urlFile.url);
                    continue;
                }

                if (urlFile.fileType != UrlDir.FileType.Model)
                    continue;

                if (!modelParts.TryGetValue(urlFile.url, out List<string> parts))
                    continue;

                foreach (string textureFile in MuParser.GetModelTextures(urlFile.fullPath))
                {
                    string textureFileName = Path.GetFileNameWithoutExtension(textureFile);
                    textureFileNameBuffer.Add(textureFileName);
                    texturePathsBuffer.Add(urlFile.parent.url + "/" + textureFileName);
                }

                if (texturePathsBuffer.Count == 0)
                {
                    modelParts.Remove(urlFile.url);
                    continue;
                }

                foreach (string part in parts)
                {
                    if (partTextureReplacements.TryGetValue(part, out List<TextureReplacement> textureReplacements))
                    {
                        foreach (TextureReplacement textureReplacement in textureReplacements)
                        {
                            for (int i = 0; i < textureFileNameBuffer.Count; i++)
                            {
                                if (textureReplacement.textureName == textureFileNameBuffer[i])
                                {
                                    allReplacedTextures.Add(texturePathsBuffer[i]);
                                    texturePathsBuffer[i] = textureReplacement.replacementUrl;
                                }
                            }
                        }
                    }

                    if (!partsTextures.TryGetValue(part, out List<string> textures))
                    {
                        textures = new List<string>(texturePathsBuffer);
                        partsTextures[part] = textures;
                    }
                    else
                    {
                        textures.AddRange(texturePathsBuffer);
                    }
                }

                texturePathsBuffer.Clear();
                textureFileNameBuffer.Clear();
            }

            List<string> stringBuffer = new List<string>();
            foreach (KeyValuePair<string, List<string>> partTextures in partsTextures)
            {
                for (int i = partTextures.Value.Count; i-- > 0;)
                {
                    string texture = partTextures.Value[i];
                    if (!allTextures.Contains(texture))
                    {
                        partTextures.Value.RemoveAt(i);
                    }
                    else
                    {
                        allPartTextures.Add(texture);
                    }
                }

                if (partTextures.Value.Count == 0)
                    stringBuffer.Add(partTextures.Key);
            }

            for (int i = stringBuffer.Count; i-- > 0;)
                partsTextures.Remove(stringBuffer[i]);

            stringBuffer.Clear();
            foreach (string replacedTexture in allReplacedTextures)
                if (allPartTextures.Contains(replacedTexture))
                    stringBuffer.Add(replacedTexture);

            for (int i = stringBuffer.Count; i-- > 0;)
                allReplacedTextures.Remove(stringBuffer[i]);
        }
    }


    //[KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class OnDemandPartTexturesLoader// : MonoBehaviour
    {




        // 1
        // build a <PartPrefab, List<Texture2D>> dictionary
        // 1.a.
        // - Parse part configs
        //   - find MODEL > model reference
        //   - find MODEL > texture refernces
        //   - find ModulePartVariants > TEXTURE references
        // - In all found model references :
        //   - find all texture references
        // Patch Part.Awake(), after RelinkPrefab()
        //   - swap the model textures

        // overview of where textures can come from :
        // - defined in the model(s) materials
        //   - PART > "mesh" : depreciated/unused
        //   - If no PART > MODEL node : the first found (as ordered in GameDatabase.databaseModel) *.mu model placed in the same directory as the part config (cfg.parent.parent.url)
        //   - If PART > MODEL node(s) : the model defined in the "model" value
        //   - not sure if it is possible to set a path to the texture in the model ? I don't think so, but...
        // - defined as a texture replacement in the PART > MODEL node
        //   - the replacement is done on the prefab renderers sharedMaterial
        //   - as far as I can tell, this require (a potentially dummy) texture with the same name as what is backed in the model to be sitting next to the model in the same directory


    }

    internal class TextureReplacement
    {
        public string textureName;
        public string replacementUrl;

        public TextureReplacement(string textureName, string replacementUrl)
        {
            this.textureName = textureName;
            this.replacementUrl = replacementUrl;
        }
    }

    internal class PrefabData
    {
        public bool areTexturesLoaded;
        public List<MaterialTextures> materials;

        public void LoadTextures()
        {

        }
    }

    internal class MaterialTextures
    {
        private Material material;
        public OnDemandTextureInfo mainTex;
        public OnDemandTextureInfo bumpMap;
        public OnDemandTextureInfo emissive;
        public OnDemandTextureInfo specMap;

        public MaterialTextures(Material material)
        {
            this.material = material;
        }

        public void LoadTextures()
        {
            if (mainTex != null)
            {
                mainTex.Load();
                material.SetTexture("_MainTex", mainTex.texture);
            }

            if (bumpMap != null)
            {
                bumpMap.Load();
                material.SetTexture("_BumpMap", bumpMap.texture);
            }

            if (emissive != null)
            {
                emissive.Load();
                material.SetTexture("_Emissive", emissive.texture);
            }

            if (specMap != null)
            {
                specMap.Load();
                material.SetTexture("_SpecMap", specMap.texture);
            }
        }
    }

    internal class OnDemandTextureInfo : GameDatabase.TextureInfo
    {
        public static Dictionary<string, OnDemandTextureInfo> texturesByUrl = new Dictionary<string, OnDemandTextureInfo>();

        private Texture2D dummyTexture;

        private bool isLoaded;
        private RawAsset.AssetType textureType;

        public OnDemandTextureInfo(UrlDir.UrlFile file, RawAsset.AssetType textureType, bool isNormalMap = false, bool isReadable = false, bool isCompressed = false, Texture2D texture = null) 
            : base(file, texture, isNormalMap, isReadable, isCompressed)
        {
            name = file.url;
            this.textureType = textureType;
            dummyTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            dummyTexture.Apply(false, true);
            dummyTexture.name = name;
            this.texture = dummyTexture;
            texturesByUrl.Add(name, this);
        }

        public void Load()
        {

        }
    }
}
