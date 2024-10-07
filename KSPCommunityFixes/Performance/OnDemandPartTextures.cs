using DDSHeaders;
using HarmonyLib;
using KSPCommunityFixes.QoL;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using static KSPCommunityFixes.Performance.KSPCFFastLoader;

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

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(Part), nameof(Part.Awake)),
                this));
        }

        static void Part_Awake_Postfix(Part __instance)
        {
            if (__instance.partInfo == null)
                return;

            if (!prefabsData.TryGetValue(__instance.partInfo, out PrefabData prefabData))
                return;

            if (prefabData.areTexturesLoaded)
                return;

            prefabData.LoadTextures();
        }

        static void Instantiate_Prefix(UnityEngine.Object original)
        {
            //if (original is Part part 
            //    && part.partInfo != null 
            //    && part.partInfo.partPrefab.IsNotNullRef() // skip instantiation of the special prefabs (kerbals, flag) during loading
            //    && prefabsData.TryGetValue(part.partInfo, out PrefabData prefabData)
            //    && !prefabData.areTexturesLoaded)
            //{
            //    // load textures
            //    // set them on the prefab
            //    prefabData.areTexturesLoaded = true;
            //}
        }

        static void PartLoader_ParsePart_Postfix(AvailablePart __result)
        {
            if (!partsTextures.TryGetValue(__result.name, out List<string> textures))
                return;

            Transform modelTransform = __result.partPrefab.transform.Find("model");
            if (modelTransform == null)
                return;


            HashSet<string> modelTextures = new HashSet<string>();
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
                            modelTextures.Add(currentTexName);
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
                            modelTextures.Add(currentTexName);
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
                            modelTextures.Add(currentTexName);
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
                            modelTextures.Add(currentTexName);
                        }

                    }
                }
            }

            if (prefabData != null)
            {
                prefabsData[__result] = prefabData;

                if (partsTextures.TryGetValue(__result.name, out List<string> partTextures))
                {
                    foreach (string partTexture in partTextures)
                    {
                        if (modelTextures.Contains(partTexture))
                            continue;

                        if (OnDemandTextureInfo.texturesByUrl.TryGetValue(partTexture, out OnDemandTextureInfo textureInfo))
                        {
                            prefabData.additonalTextures.Add(textureInfo);
                        }
                    }
                }
            }
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


    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class OnDemandPartTexturesLoader : MonoBehaviour
    {
        public static OnDemandPartTexturesLoader instance;

        void Start()
        {
            DontDestroyOnLoad(this);
            instance = this;
        }
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
        public List<MaterialTextures> materials = new List<MaterialTextures>();
        public HashSet<OnDemandTextureInfo> additonalTextures = new HashSet<OnDemandTextureInfo>();

        public void LoadTextures()
        {
            foreach (MaterialTextures materialTextures in materials)
            {
                materialTextures.LoadTextures();
            }

            foreach (OnDemandTextureInfo textureInfo in additonalTextures)
            {
                if (!textureInfo.isLoaded)
                {
                    textureInfo.Load();
                }
            }

            areTexturesLoaded = true;
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
                if (mainTex.isLoaded)
                    material.SetTexture("_MainTex", mainTex.texture);
                else
                    OnDemandPartTexturesLoader.instance.StartCoroutine(LoadMainTex());
            }

            if (bumpMap != null)
            {
                if (bumpMap.isLoaded)
                    material.SetTexture("_BumpMap", bumpMap.texture);
                else
                    OnDemandPartTexturesLoader.instance.StartCoroutine(LoadBumpMap());
            }

            if (emissive != null)
            {
                if (emissive.isLoaded)
                    material.SetTexture("_Emissive", emissive.texture);
                else
                    OnDemandPartTexturesLoader.instance.StartCoroutine(LoadEmissive());
            }

            if (specMap != null)
            {
                if (specMap.isLoaded)
                    material.SetTexture("_SpecMap", specMap.texture);
                else
                    OnDemandPartTexturesLoader.instance.StartCoroutine(LoadSpecMap());
            }
        }

        private IEnumerator LoadMainTex()
        {
            mainTex.Load();

            while (!mainTex.isLoaded)
                yield return null;

            material.SetTexture("_MainTex", mainTex.texture);
        }

        private IEnumerator LoadBumpMap()
        {
            bumpMap.Load();

            while (!bumpMap.isLoaded)
                yield return null;

            material.SetTexture("_BumpMap", bumpMap.texture);
        }

        private IEnumerator LoadEmissive()
        {
            emissive.Load();

            while (!emissive.isLoaded)
                yield return null;

            material.SetTexture("_Emissive", emissive.texture);
        }

        private IEnumerator LoadSpecMap()
        {
            specMap.Load();

            while (!specMap.isLoaded)
                yield return null;

            material.SetTexture("_SpecMap", specMap.texture);
        }
    }

    internal class OnDemandTextureInfo : GameDatabase.TextureInfo
    {
        public static Dictionary<string, OnDemandTextureInfo> texturesByUrl = new Dictionary<string, OnDemandTextureInfo>();

        private Texture2D dummyTexture;

        public bool isLoaded;
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
            switch (textureType)
            {
                case RawAsset.AssetType.TextureDDS:
                    LoadDDS();
                    break;
                case RawAsset.AssetType.TextureJPG:
                    break;
                case RawAsset.AssetType.TextureMBM:
                    break;
                case RawAsset.AssetType.TexturePNG:
                    break;
                case RawAsset.AssetType.TexturePNGCached:
                    break;
                case RawAsset.AssetType.TextureTGA:
                    break;
                case RawAsset.AssetType.TextureTRUECOLOR:
                    break;
            }
        }

        private void LoadDDS()
        {
            FileStream fileStream = new FileStream(file.fullPath, FileMode.Open);
            BinaryReader binaryReader = new BinaryReader(fileStream);

            if (binaryReader.ReadUInt32() != DDSValues.uintMagic)
            {
                binaryReader.Dispose();
                fileStream.Dispose();
                return;
            }
            DDSHeader dDSHeader = new DDSHeader(binaryReader);
            bool mipChain = (dDSHeader.dwCaps & DDSPixelFormatCaps.MIPMAP) != 0;
            bool isNormalMap = (dDSHeader.ddspf.dwFlags & 0x80000u) != 0 || (dDSHeader.ddspf.dwFlags & 0x80000000u) != 0;

            DDSFourCC ddsFourCC = (DDSFourCC)dDSHeader.ddspf.dwFourCC;
            GraphicsFormat graphicsFormat = GraphicsFormat.None;

            switch (ddsFourCC)
            {
                case DDSFourCC.DXT1:
                    graphicsFormat = GraphicsFormatUtility.GetGraphicsFormat(TextureFormat.DXT1, true);
                    break;
                case DDSFourCC.DXT5:
                    graphicsFormat = GraphicsFormatUtility.GetGraphicsFormat(TextureFormat.DXT5, true);
                    break;
                case DDSFourCC.BC4U_ATI:
                case DDSFourCC.BC4U:
                    graphicsFormat = GraphicsFormat.R_BC4_UNorm;
                    break;
                case DDSFourCC.BC4S:
                    graphicsFormat = GraphicsFormat.R_BC4_SNorm;
                    break;
                case DDSFourCC.BC5U_ATI:
                case DDSFourCC.BC5U:
                    graphicsFormat = GraphicsFormat.RG_BC5_UNorm;
                    break;
                case DDSFourCC.BC5S:
                    graphicsFormat = GraphicsFormat.RG_BC5_SNorm;
                    break;
                case DDSFourCC.R16G16B16A16_UNORM:
                    graphicsFormat = GraphicsFormat.R16G16B16A16_UNorm;
                    break;
                case DDSFourCC.R16G16B16A16_SNORM:
                    graphicsFormat = GraphicsFormat.R16G16B16A16_SNorm;
                    break;
                case DDSFourCC.R16_FLOAT:
                    graphicsFormat = GraphicsFormat.R16_SFloat;
                    break;
                case DDSFourCC.R16G16_FLOAT:
                    graphicsFormat = GraphicsFormat.R16G16_SFloat;
                    break;
                case DDSFourCC.R16G16B16A16_FLOAT:
                    graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                    break;
                case DDSFourCC.R32_FLOAT:
                    graphicsFormat = GraphicsFormat.R32_SFloat;
                    break;
                case DDSFourCC.R32G32_FLOAT:
                    graphicsFormat = GraphicsFormat.R32G32_SFloat;
                    break;
                case DDSFourCC.R32G32B32A32_FLOAT:
                    graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat;
                    break;
                case DDSFourCC.DX10:
                    DDSHeaderDX10 dx10Header = new DDSHeaderDX10(binaryReader);
                    switch (dx10Header.dxgiFormat)
                    {
                        case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM:
                            graphicsFormat = GraphicsFormat.RGBA_DXT1_UNorm;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB:
                            graphicsFormat = GraphicsFormat.RGBA_DXT1_SRGB;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM:
                            graphicsFormat = GraphicsFormat.RGBA_DXT5_UNorm;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB:
                            graphicsFormat = GraphicsFormat.RGBA_DXT5_SRGB;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM:
                            graphicsFormat = GraphicsFormat.R_BC4_SNorm;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM:
                            graphicsFormat = GraphicsFormat.R_BC4_UNorm;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM:
                            graphicsFormat = GraphicsFormat.RG_BC5_SNorm;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM:
                            graphicsFormat = GraphicsFormat.RG_BC5_UNorm;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM:
                            graphicsFormat = GraphicsFormat.RGBA_BC7_UNorm;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB:
                            graphicsFormat = GraphicsFormat.RGBA_BC7_SRGB;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16:
                            graphicsFormat = GraphicsFormat.RGB_BC6H_SFloat;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16:
                            graphicsFormat = GraphicsFormat.RGB_BC6H_UFloat;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM:
                            graphicsFormat = GraphicsFormat.R16G16B16A16_UNorm;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM:
                            graphicsFormat = GraphicsFormat.R16G16B16A16_SNorm;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT:
                            graphicsFormat = GraphicsFormat.R16_SFloat;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT:
                            graphicsFormat = GraphicsFormat.R16G16_SFloat;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT:
                            graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT:
                            graphicsFormat = GraphicsFormat.R32_SFloat;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT:
                            graphicsFormat = GraphicsFormat.R32G32_SFloat;
                            break;
                        case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT:
                            graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat;
                            break;
                        default:
                            //SetError($"DDS: The '{dx10Header.dxgiFormat}' DXT10 format isn't supported");
                            break;
                    }
                    break;
                case DDSFourCC.DXT2:
                case DDSFourCC.DXT3:
                case DDSFourCC.DXT4:
                case DDSFourCC.RGBG:
                case DDSFourCC.GRGB:
                case DDSFourCC.UYVY:
                case DDSFourCC.YUY2:
                case DDSFourCC.CxV8U8:
                    //SetError($"DDS: The '{ddsFourCC}' format isn't supported, use DXT1 for RGB textures or DXT5 for RGBA textures");
                    break;
                default:
                    //SetError($"DDS: Unknown dwFourCC format '0x{ddsFourCC:X}'");
                    break;
            }

            if (graphicsFormat != GraphicsFormat.None)
            {
                if (!SystemInfo.IsFormatSupported(graphicsFormat, FormatUsage.Sample))
                {
                    if (SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX &&
                        (graphicsFormat == GraphicsFormat.RGBA_BC7_UNorm
                         || graphicsFormat == GraphicsFormat.RGBA_BC7_SRGB
                         || graphicsFormat == GraphicsFormat.RGB_BC6H_SFloat
                         || graphicsFormat == GraphicsFormat.RGB_BC6H_UFloat))
                    {
                        //SetError($"DDS: The '{graphicsFormat}' format is not supported on MacOS");
                    }
                    else
                    {
                        //SetError($"DDS: The '{graphicsFormat}' format is not supported by your GPU or OS");
                    }
                }
                else
                {
                    texture = new Texture2D((int)dDSHeader.dwWidth, (int)dDSHeader.dwHeight, graphicsFormat, mipChain ? TextureCreationFlags.MipChain : TextureCreationFlags.None);
                    if (texture.IsNullOrDestroyed())
                    {
                        //SetError($"DDS: Failed to load texture, unknown error");
                    }
                    else
                    {
                        byte[] ddsData = binaryReader.ReadBytes((int)(binaryReader.BaseStream.Length - binaryReader.BaseStream.Position));
                        texture.LoadRawTextureData(ddsData);
                        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                        isLoaded = true;
                    }
                }
            }
        }
    }
}
