using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.UI.Screens.Flight;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.QoL
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class NoIVA : MonoBehaviour
    {
        public static string LOC_KeepAll = "Keep all";
        public static string LOC_DisableAll = "Disable all";
        public static string LOC_UsePlaceholder = "Use placeholder";
        public static string LOC_SettingsTitle = "IVA (interior view)";
        public static string LOC_SettingsTooltip =
            "Disable IVA functionality: reduce RAM/VRAM usage and increase FPS." +
            "\n\t-Disable all : disable IVA" +
            "\n\t-Use placeholder : disable IVA but keep crew portraits" +
            "\n<i>Changes will take effect after relaunching KSP</i>";

        public enum PatchState {disabled, noIVA, usePlaceholder}

        private static char[] textureSplitChars = new char[3] {':', ',', ';'};

        public static PatchState patchState = PatchState.disabled;
        private static bool debugPrunning = false;

        private Stopwatch watch = new Stopwatch();

        public static void SwitchPatchState(float f)
        {
            switch (Mathf.Round(Mathf.Clamp(f, 0f, 2f)))
            {
                case 0f: patchState = PatchState.disabled; break;
                case 1f: patchState = PatchState.noIVA; break;
                case 2f: patchState = PatchState.usePlaceholder; break;
            }
        }

        public static float PatchStateToFloat()
        {
            switch (patchState)
            {
                case PatchState.disabled: return 0f;
                case PatchState.noIVA: return 1f;
                case PatchState.usePlaceholder: return 2f;
                default: return 0f;
            }
        }

        public static string PatchStateTitle()
        {
            switch (patchState)
            {
                case PatchState.disabled: return LOC_KeepAll;
                case PatchState.noIVA: return LOC_DisableAll;
                case PatchState.usePlaceholder: return LOC_UsePlaceholder;
                default: return string.Empty;
            }
        }

        public static void SaveSettings()
        {
            string pluginDataPath = Path.Combine(KSPCommunityFixes.ModPath, BasePatch.pluginData);

            if (!Directory.Exists(pluginDataPath))
                Directory.CreateDirectory(pluginDataPath);

            ConfigNode patchNode = new ConfigNode(nameof(NoIVA));
            patchNode.AddValue(nameof(patchState), patchState.ToString());
            ConfigNode topNode = new ConfigNode();
            topNode.AddNode(patchNode);
            topNode.Save(Path.Combine(pluginDataPath, nameof(NoIVA) + ".cfg"));
        }

        private void Start()
        {
            patchState = PatchState.disabled;
#if DEBUG
            patchState = PatchState.usePlaceholder;
            debugPrunning = true;
#endif
            string path = Path.Combine(KSPCommunityFixes.ModPath, BasePatch.pluginData, nameof(NoIVA) + ".cfg");

            if (File.Exists(path))
            {
                ConfigNode node = ConfigNode.Load(path);
                if (node?.nodes[0] != null)
                {
                    node.nodes[0].TryGetEnum(nameof(patchState), ref patchState, PatchState.disabled);
                    node.nodes[0].TryGetValue(nameof(debugPrunning), ref debugPrunning);
                }
            }

            if (patchState == PatchState.disabled)
            {
                Destroy(this);
                return;
            }

            watch.Start();
            HashSet<string> prunedModelUrls = new HashSet<string>(200);
            HashSet<string> whiteListedModelUrls = new HashSet<string>(1000);
            HashSet<UrlDir.UrlFile> prunedModelFiles = new HashSet<UrlDir.UrlFile>(200);
            HashSet<UrlDir.UrlFile> whiteListedModelFiles = new HashSet<UrlDir.UrlFile>(1000);
            HashSet<string> prunedTextureUrls = new HashSet<string>(500);
            HashSet<string> whiteListedTextureUrls = new HashSet<string>(2500);
            int prunedModelsCount = 0;
            int prunedTexturesCount = 0;

            foreach (UrlDir.UrlConfig urlConfig in GameDatabase.Instance.root.AllConfigs)
            {
                bool isInternal = urlConfig.type == "INTERNAL";
                bool isPart = urlConfig.type == "PART";
                bool isProp = urlConfig.type == "PROP";

                if (!isInternal && !isPart && !isProp)
                    continue;

                if (isInternal && patchState == PatchState.usePlaceholder)
                {
                    string name = urlConfig.config.GetValue("name");
                    if (name == "Placeholder")
                        continue;
                }

                bool hasModelNode = false;
                foreach (ConfigNode modelNode in urlConfig.config.GetNodes("MODEL"))
                {
                    foreach (string modelPath in modelNode.GetValues("model"))
                    {
                        hasModelNode = true;
                        if (isPart)
                            whiteListedModelUrls.Add(modelPath);
                        else
                            prunedModelUrls.Add(modelPath);
                    }
                    foreach (string texturePath in modelNode.GetValues("texture"))
                    {
                        string[] array = texturePath.Split(textureSplitChars, StringSplitOptions.RemoveEmptyEntries);
                        if (array.Length != 2)
                            continue;

                        if (isPart)
                            whiteListedTextureUrls.Add(array[1].Trim());
                        else
                            prunedTextureUrls.Add(array[1].Trim());
                    }
                }

                if (!hasModelNode)
                {
                    foreach (UrlDir.UrlFile urlFile in urlConfig.parent.parent.files)
                    {
                        if (urlFile.fileExtension == "mu")
                        {
                            if (isPart)
                            {
                                whiteListedModelFiles.Add(urlFile);
                            }
                            else
                            {
                                prunedModelFiles.Add(urlFile);
                                urlFile._fileExtension = "disabled";
                                urlFile._fileType = UrlDir.FileType.Unknown;
                                prunedModelsCount++;

                                if (debugPrunning)
                                    Debug.Log($"[KSPCF:NoIVA] Pruning model {urlFile.url}");
                            }
                        }
                    }
                }
            }

            foreach (UrlDir.UrlFile urlFile in GameDatabase.Instance.root.AllFiles)
            {
                if (urlFile.fileType == UrlDir.FileType.Model)
                {
                    if (whiteListedModelUrls.Contains(urlFile.url))
                    {
                        whiteListedModelFiles.Add(urlFile);
                        continue;
                    }

                    if (prunedModelUrls.Contains(urlFile.url))
                    {
                        prunedModelFiles.Add(urlFile);
                        urlFile._fileExtension = "disabled";
                        urlFile._fileType = UrlDir.FileType.Unknown;
                        prunedModelsCount++;

                        if (debugPrunning)
                            Debug.Log($"[KSPCF:NoIVA] Pruning model {urlFile.url}");
                    }
                }
            }

            foreach (UrlDir.UrlFile urlFile in prunedModelFiles)
            {
                foreach (string textureUrl in MuParser.GetModelTextures(urlFile.fullPath))
                {
                    prunedTextureUrls.Add(urlFile.parent.url + "/" + Path.GetFileNameWithoutExtension(textureUrl));
                }
            }

            foreach (UrlDir.UrlFile urlFile in whiteListedModelFiles)
            {
                foreach (string textureUrl in MuParser.GetModelTextures(urlFile.fullPath))
                {
                    prunedTextureUrls.Remove(urlFile.parent.url + "/" + Path.GetFileNameWithoutExtension(textureUrl));
                }
            }

            foreach (UrlDir.UrlFile urlFile in GameDatabase.Instance.root.AllFiles)
            {
                if (urlFile.fileType == UrlDir.FileType.Texture && prunedTextureUrls.Contains(urlFile.url))
                {
                    urlFile._fileExtension = "disabled";
                    urlFile._fileType = UrlDir.FileType.Unknown;
                    prunedTexturesCount++;

                    if (debugPrunning)
                        Debug.Log($"[KSPCF:NoIVA] Pruning texture {urlFile.url}");
                }
            }

            watch.Stop();
            Debug.Log($"[KSPCF:NoIVA] {prunedModelsCount} IVA models and {prunedTexturesCount} IVA textures pruned in {watch.ElapsedMilliseconds / 1000.0}s");
        }

        public void ModuleManagerPostLoad()
        {
            if (patchState == PatchState.usePlaceholder)
            {
                UrlDir.UrlConfig placeholderBase = null;
                foreach (UrlDir.UrlConfig urlConfig in GameDatabase.Instance.root.AllConfigs)
                {
                    if (urlConfig.type == "INTERNAL" || urlConfig.type == "PROP")
                    {
                        string name = urlConfig.config.GetValue("name");
                        if (name == "Placeholder")
                        {
                            placeholderBase = urlConfig;
                            continue;
                        }

                        urlConfig._type = "DISABLED_IVA";
                    }
                }

                // Create additional placeholder INTERNAL definitions for every seat count from 1 to 15.
                // This is mainly to stop KSP complaining about IVA seat count not matching part crew capacity,
                // I don't think not doing it would actually causes any issue.
                UrlDir placeholderDir = placeholderBase.parent.parent;
                FileInfo fileInfo = new FileInfo(placeholderBase.parent.fullPath);
                UrlDir.UrlFile[] placeholders = new UrlDir.UrlFile[15];
                for (int i = 1; i <= 15; i++)
                {
                    UrlDir.UrlFile p = new UrlDir.UrlFile(placeholderDir, fileInfo);
                    p.configs[0].config._nodes.nodes.RemoveRange(i, 16 - i);
                    p.configs[0].config.SetValue("name", "PlaceholderS" + i, true);
                    placeholders[i - 1] = p;
                    placeholderDir._files.Add(p);
                }

                foreach (UrlDir.UrlConfig urlConfig in GameDatabase.Instance.root.AllConfigs)
                {
                    if (urlConfig.type == "PART")
                    {
                        ConfigNode internalNode = urlConfig.config.GetNode("INTERNAL");
                        if (internalNode != null)
                        {
                            float crewCapacity = 0;
                            if (!urlConfig.config.TryGetValue("CrewCapacity", ref crewCapacity) || crewCapacity < 1 || crewCapacity > 15)
                            {
                                internalNode.SetValue("name", "Placeholder", true);
                                continue;
                            }

                            internalNode.SetValue("name", "PlaceholderS" + crewCapacity, true);
                        }
                    }
                }

                // Disable the IVA button on the crew portrait
                KSPCommunityFixes.Harmony.Patch(
                    AccessTools.Method(typeof(KerbalPortrait), nameof(KerbalPortrait.Setup), new Type[] { typeof(Kerbal), typeof(KerbalEVA), typeof(RectTransform) }),
                    null, new HarmonyMethod(AccessTools.Method(typeof(NoIVA), nameof(KerbalPortrait_Setup_Postfix))));

                // Disable the IVA overlay toggle next to the crew portrait gallery
                KSPCommunityFixes.Harmony.Patch(
                    AccessTools.Method(typeof(KerbalPortraitGallery), nameof(KerbalPortraitGallery.UIControlsUpdate)),
                    null, new HarmonyMethod(AccessTools.Method(typeof(NoIVA), nameof(KerbalPortraitGallery_UIControlsUpdate_Postfix))));
            }
            else if (patchState == PatchState.noIVA)
            {
                foreach (UrlDir.UrlConfig urlConfig in GameDatabase.Instance.root.AllConfigs)
                {
                    if (urlConfig.type == "PART")
                    {
                        urlConfig.config.RemoveNodes("INTERNAL");
                    }
                    else if (urlConfig.type == "INTERNAL" || urlConfig.type == "PROP")
                    {
                        urlConfig._type = "DISABLED_IVA";
                    }
                }
            }

            if (patchState != PatchState.disabled)
            {
                // prevent going to IVA mode at all.
                KSPCommunityFixes.Harmony.Patch(
                    AccessTools.Method(typeof(CameraManager), nameof(CameraManager.SetCameraIVA), new Type[] {typeof(Kerbal), typeof(bool)}),
                    new HarmonyMethod(AccessTools.Method(typeof(NoIVA), nameof(CameraManager_SetCameraIVA_Prefix))));
            }

            Destroy(this);
        }

        static bool CameraManager_SetCameraIVA_Prefix(out bool __result)
        {
            __result = false;
            return false;
        }

        static void KerbalPortrait_Setup_Postfix(KerbalPortrait __instance)
        {
            if (__instance.eventSetupDone)
            {
                __instance.ivaButton.gameObject.SetActive(false);
                __instance.evaButton.transform.localPosition = __instance.ivaButton.transform.localPosition;
                __instance.ivaTooltip.continuousUpdate = false;
            }
        }

        static void KerbalPortraitGallery_UIControlsUpdate_Postfix(KerbalPortraitGallery __instance)
        {
            __instance.IVAOverlayButton.gameObject.SetActive(false);
        }
    }

    public static class MuParser
    {
        private static int currentMuFileVersion;

        public static IEnumerable<string> GetModelTextures(string modelFilePath)
        {
            BinaryReader binaryReader = new BinaryReader(File.Open(modelFilePath, FileMode.Open));
            if (binaryReader == null)
            {
                Debug.LogError("File error");
                yield break;
            }

            if (binaryReader.ReadInt32() != 76543)
            {
                Debug.LogError($"File '{modelFilePath}' is an incorrect type.");
                binaryReader.Close();
                yield break;
            }

            try
            {
                currentMuFileVersion = binaryReader.ReadInt32();
                binaryReader.SkipString();

                foreach (string texture in GetModelTexturesRecursive(binaryReader))
                    yield return texture;
            }
            finally
            {
                binaryReader.Close();
            }
        }

        private static IEnumerable<string> GetModelTexturesRecursive(BinaryReader br)
        {
            br.SkipString();
            br.SkipSingle(10);

            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                int switchInt = br.ReadInt32();
                switch (switchInt)
                {
                    case 0:
                        foreach (string texture in GetModelTexturesRecursive(br))
                            yield return texture;
                        break;
                    case 2: // ReadAnimation
                        int count = br.ReadInt32();
                        while (count-- > 0)
                        {
                            br.SkipString();
                            br.SkipSingleOrInt32(7);
                            int count2 = br.ReadInt32();
                            while (count2-- > 0)
                            {
                                br.SkipString();
                                br.SkipString();
                                br.SkipInt32(3);
                                br.SkipSingleOrInt32(5 * br.ReadInt32());
                            }
                        }
                        br.SkipString();
                        br.SkipBoolean();
                        break;
                    case 3: // meshcollider
                        {
                            br.SkipBoolean();
                            SkipMesh(br);
                            break;
                        }
                    case 4: //SphereCollider
                        {
                            br.SkipSingle(4);
                            break;
                        }
                    case 5: // CapsuleCollider
                        {
                            br.SkipSingleOrInt32(5);
                            break;
                        }
                    case 6: //BoxCollider
                        {
                            br.SkipSingle(6);
                            break;
                        }
                    case 7: // MeshFilter mesh
                        SkipMesh(br);
                        break;
                    case 8: // ReadMeshRenderer
                        if (currentMuFileVersion >= 1)
                            br.SkipBoolean(2);

                        br.SkipInt32(br.ReadInt32());
                        break;
                    case 9: // ReadSkinnedMeshRenderer
                        br.SkipInt32(br.ReadInt32());
                        br.SkipSingleOrInt32(7);
                        br.SkipBoolean();
                        int count6 = br.ReadInt32();
                        while (count6-- > 0)
                            br.SkipString();

                        SkipMesh(br);
                        break;
                    case 10: //Material
                        {
                            int count4 = br.ReadInt32();
                            if (currentMuFileVersion < 4)
                                while (count4-- > 0)
                                    SkipMaterial(br);
                            else
                                while (count4-- > 0)
                                    SkipMaterial4(br);

                            break;
                        }
                    case 12:
                        int count5 = br.ReadInt32();
                        while (count5-- > 0)
                        {
                            //string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(br.ReadString());
                            //TextureType textureType = (TextureType)br.ReadInt32();
                            //string url = file.parent.url + "/" + fileNameWithoutExtension;
                            //Texture2D texture = GameDatabase.Instance.GetTexture(url, textureType == TextureType.NormalMap);
                            yield return br.ReadString();
                            br.SkipInt32();
                        }
                        break;
                    case 23: // ReadLight
                        br.SkipSingleOrInt32(currentMuFileVersion > 1 ? 9 : 8);
                        break;
                    case 24: // ReadTagAndLayer
                        br.SkipString();
                        br.SkipInt32();
                        break;
                    case 25: // meshCollider
                        {
                            br.SkipBoolean(2);
                            SkipMesh(br);
                            break;
                        }
                    case 26: //SphereCollider
                        {
                            br.SkipBoolean();
                            br.SkipSingle(4);
                            break;
                        }
                    case 27: //CapsuleCollider
                        {
                            br.SkipBoolean();
                            br.SkipSingleOrInt32(6);
                            break;
                        }
                    case 28: //BoxCollider
                        {
                            br.SkipBoolean();
                            br.SkipSingle(6);
                            break;
                        }
                    case 29: //WheelCollider
                        {
                            br.SkipSingle(19);
                            break;
                        }
                    case 30: //ReadCamera
                        br.SkipSingleOrInt32(6);
                        br.SkipBoolean();
                        br.SkipSingle(4);
                        break;
                    case 31: //ReadParticles
                        br.SkipBoolean();
                        br.SkipSingleOrInt32(7 + 4);
                        br.SkipBoolean();
                        br.SkipSingleOrInt32(18);
                        br.SkipBoolean(2);
                        br.SkipSingle(5 * 4);
                        br.SkipSingle(14);
                        br.SkipBoolean(2);
                        br.SkipSingleOrInt32(3 + 5);
                        break;
                    case 1:
                        yield break;
                }
            }
        }

        private static void SkipMesh(BinaryReader br)
        {
            if (br.ReadInt32() != 13)
                return;

            int count = br.ReadInt32();
            br.SkipInt32();
            int entryType;
            while ((entryType = br.ReadInt32()) != 22)
            {
                switch (entryType)
                {
                    case 0x20: // MeshVertexColors
                        br.SkipBytes(4 * count);
                        break;
                    case 14: //MeshVerts
                    case 17: //MeshNormals
                        br.SkipSingle(3 * count);
                        break;
                    case 0xF: //MeshUV
                    case 0x10: //MeshUV2
                        br.SkipSingle(2 * count);
                        break;
                    case 18: // MeshTangents
                        br.SkipSingle(4 * count);
                        break;
                    case 19: // MeshTriangles
                        br.SkipInt32(br.ReadInt32());
                        break;
                    case 20: // MeshBoneWeights
                        br.SkipSingle(4 * count);
                        br.SkipInt32(4 * count);
                        break;
                    case 21: // MeshBindPoses
                        br.SkipSingle(16 * br.ReadInt32());
                        break;
                }
            }
        }

        private enum ShaderType
        {
            Custom,
            Diffuse,
            Specular,
            Bumped,
            BumpedSpecular,
            Emissive,
            EmissiveSpecular,
            EmissiveBumpedSpecular,
            AlphaCutout,
            AlphaCutoutBumped,
            Alpha,
            AlphaSpecular,
            AlphaUnlit,
            Unlit,
            ParticleAlpha,
            ParticleAdditive,
            BumpedSpecularMap
        }

        private static void SkipMaterial(BinaryReader br)
        {
            br.SkipString();
            ShaderType shaderType = (ShaderType)br.ReadInt32();
            switch (shaderType)
            {
                default:
                    SkipMaterialTexture(br);
                    break;
                case ShaderType.Specular:
                    SkipMaterialTexture(br);
                    br.SkipSingle(5);
                    break;
                case ShaderType.Bumped:
                    SkipMaterialTexture(br);
                    SkipMaterialTexture(br);
                    break;
                case ShaderType.BumpedSpecular:
                    SkipMaterialTexture(br);
                    SkipMaterialTexture(br);
                    br.SkipSingle(5);
                    break;
                case ShaderType.Emissive:
                    SkipMaterialTexture(br);
                    SkipMaterialTexture(br);
                    br.SkipSingle(4);
                    break;
                case ShaderType.EmissiveSpecular:
                    SkipMaterialTexture(br);
                    br.SkipSingle(5);
                    SkipMaterialTexture(br);
                    br.SkipSingle(4);
                    break;
                case ShaderType.EmissiveBumpedSpecular:
                    SkipMaterialTexture(br);
                    SkipMaterialTexture(br);
                    br.SkipSingle(5);
                    SkipMaterialTexture(br);
                    br.SkipSingle(4);
                    break;
                case ShaderType.AlphaCutout:
                    SkipMaterialTexture(br);
                    br.SkipSingle();
                    break;
                case ShaderType.AlphaCutoutBumped:
                    SkipMaterialTexture(br);
                    SkipMaterialTexture(br);
                    br.SkipSingle();
                    break;
                case ShaderType.Alpha:
                    SkipMaterialTexture(br);
                    break;
                case ShaderType.AlphaSpecular:
                    SkipMaterialTexture(br);
                    br.SkipSingle(6);
                    break;
                case ShaderType.AlphaUnlit:
                    SkipMaterialTexture(br);
                    br.SkipSingle(4);
                    break;
                case ShaderType.Unlit:
                    SkipMaterialTexture(br);
                    br.SkipSingle(4);
                    break;
                case ShaderType.ParticleAlpha:
                    SkipMaterialTexture(br);
                    br.SkipSingle(5);
                    break;
                case ShaderType.ParticleAdditive:
                    SkipMaterialTexture(br);
                    br.SkipSingle(5);
                    break;
                case ShaderType.BumpedSpecularMap:
                    SkipMaterialTexture(br);
                    SkipMaterialTexture(br);
                    SkipMaterialTexture(br);
                    br.SkipSingle(2);
                    break;
            }
        }

        private static void SkipMaterialTexture(BinaryReader br)
        {
            br.SkipSingleOrInt32(5);
        }

        private static void SkipMaterial4(BinaryReader br)
        {
            br.SkipString();
            br.SkipString();
            int count = br.ReadInt32();
            while (count-- > 0)
            {
                br.SkipString();
                switch (br.ReadInt32())
                {
                    case 0:
                    case 1:
                        br.SkipSingle(4);
                        break;
                    case 2:
                    case 3:
                        br.SkipSingle();
                        break;
                    case 4:
                        br.SkipSingleOrInt32(5);
                        break;
                }
            }
        }
    }

    public static class BinaryReaderExtensions
    {
        public static int Read7BitEncodedInt(this BinaryReader br)
        {
            int num = 0;
            int num2 = 0;
            byte b;
            do
            {
                if (num2 == 35)
                {
                    throw new FormatException("Too many bytes in what should have been a 7 bit encoded Int32.");
                }
                b = br.ReadByte();
                num |= (b & 0x7F) << num2;
                num2 += 7;
            }
            while ((b & 0x80u) != 0);
            return num;
        }

        public static void SkipString(this BinaryReader br)
        {
            int size = br.Read7BitEncodedInt();

            if (size < 0)
                throw new IOException("BinaryReader encountered an invalid string length of {0} characters.", size);

            if (size == 0)
                return;

            br.SkipBytes(size);
        }

        // note : incrementing Position is faster than using BaseStream.Seek()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SkipBytes(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SkipInt32(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count * 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SkipSingle(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count * 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SkipSingleOrInt32(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count * 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SkipBoolean(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count;
        }
    }

}
