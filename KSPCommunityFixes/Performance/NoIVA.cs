using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KSPCommunityFixes.Performance
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class NoIVA : MonoBehaviour
    {
        private static char[] textureSplitChars = new char[3] {':', ',', ';'};

        private Stopwatch watch = new Stopwatch();

        private void Start()
        {
            watch.Start();
            HashSet<string> modelUrls = new HashSet<string>(200);
            HashSet<UrlDir.UrlFile> modelFiles = new HashSet<UrlDir.UrlFile>(200);
            HashSet<string> textureUrls = new HashSet<string>(500);
            
            foreach (UrlDir.UrlConfig urlConfig in GameDatabase.Instance.root.AllConfigs)
            {
                if (urlConfig.type == "INTERNAL" || urlConfig.type == "PROP")
                {
                    string name = urlConfig.config.GetValue("name");
                    if (name == "Placeholder")
                        continue;

                    bool hasModelNode = false;
                    foreach (ConfigNode modelNode in urlConfig.config.GetNodes("MODEL"))
                    {
                        foreach (string modelPath in modelNode.GetValues("model"))
                        {
                            modelUrls.Add(modelPath);
                            hasModelNode = true;
                        }
                        foreach (string texturePath in modelNode.GetValues("texture"))
                        {
                            string[] array = texturePath.Split(textureSplitChars, StringSplitOptions.RemoveEmptyEntries);
                            if (array.Length != 2)
                                continue;

                            textureUrls.Add(array[1].Trim());
                        }
                    }

                    if (!hasModelNode)
                    {
                        foreach (UrlDir.UrlFile urlFile in urlConfig.parent.parent.files)
                        {
                            if (urlFile.fileExtension == "mu")
                            {
                                modelFiles.Add(urlFile);
                                urlFile._fileExtension = "disabled";
                                urlFile._fileType = UrlDir.FileType.Unknown;
                            }
                        }
                    }
                }
            }

            foreach (UrlDir.UrlFile urlFile in GameDatabase.Instance.root.AllFiles)
            {
                if (urlFile.fileType == UrlDir.FileType.Model && modelUrls.Contains(urlFile.url))
                {
                    modelFiles.Add(urlFile);
                    urlFile._fileExtension = "disabled";
                    urlFile._fileType = UrlDir.FileType.Unknown;
                }
            }

            foreach (UrlDir.UrlFile urlFile in modelFiles)
            {
                foreach (string textureUrl in MuParser.GetModelTextures(urlFile.fullPath))
                {
                    textureUrls.Add(urlFile.parent.url + "/" + Path.GetFileNameWithoutExtension(textureUrl));
                }
            }

            foreach (UrlDir.UrlFile urlFile in GameDatabase.Instance.root.AllFiles)
            {
                if (urlFile.fileType == UrlDir.FileType.Texture && textureUrls.Contains(urlFile.url))
                {
                    urlFile._fileExtension = "disabled";
                    urlFile._fileType = UrlDir.FileType.Unknown;
                }
            }

            watch.Stop();
            Debug.Log($"[NOIVA] Assets stripped in {watch.ElapsedMilliseconds/1000.0}s");
        }

        public void ModuleManagerPostLoad()
        {
            watch.Restart();
            foreach (UrlDir.UrlConfig urlConfig in GameDatabase.Instance.root.AllConfigs)
            {
                if (urlConfig.type == "PART")
                {
                    ConfigNode internalNode = urlConfig.config.GetNode("INTERNAL");
                    if (internalNode != null)
                        internalNode.SetValue("name", "Placeholder", true);
                }
                else if (urlConfig.type == "INTERNAL" || urlConfig.type == "PROP")
                {
                    string name = urlConfig.config.GetValue("name");
                    if (name == "Placeholder")
                        continue;

                    urlConfig._type = "DISABLED_IVA";
                }
            }
            watch.Stop();
            Debug.Log($"[NOIVA] Parts patched in {watch.ElapsedMilliseconds / 1000.0}s");
            Destroy(this);
        }
    }

    public static class MuParser
    {
        private static int currentMuFileVersion;

        public static List<string> GetModelTextures(string modelFilePath)
        {
            BinaryReader binaryReader = new BinaryReader(File.Open(modelFilePath, FileMode.Open));
            if (binaryReader == null)
            {
                Debug.LogError("File error");
                return null;
            }

            if (binaryReader.ReadInt32() != 76543)
            {
                Debug.LogError($"File '{modelFilePath}' is an incorrect type.");
                binaryReader.Close();
                return null;
            }

            currentMuFileVersion = binaryReader.ReadInt32();
            binaryReader.SkipString();

            List<string> textures = new List<string>();

            GetModelTexturesRecursive(binaryReader, textures);
            binaryReader.Close();

            return textures;
        }

        private static void GetModelTexturesRecursive(BinaryReader br, List<string> textures)
        {
            br.SkipString();
            br.SkipSingle(10);

            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                int switchInt = br.ReadInt32();
                switch (switchInt)
                {
                    case 0:
                        GetModelTexturesRecursive(br, textures);
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
                            textures.Add(br.ReadString());
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
                        return;
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

        public enum ShaderType
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

        public static void SkipBytes(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count;
            //br.BaseStream.Seek(count, SeekOrigin.Current);
        }

        public static void SkipInt32(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count * 4;
            //br.BaseStream.Seek(count * 4, SeekOrigin.Current);
        }

        public static void SkipSingle(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count * 4;
            //br.BaseStream.Seek(count * 4, SeekOrigin.Current);
        }

        public static void SkipSingleOrInt32(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count * 4;
            //br.BaseStream.Seek(count * 4, SeekOrigin.Current);
        }

        public static void SkipBoolean(this BinaryReader br, int count = 1)
        {
            br.BaseStream.Position += count;
            //br.BaseStream.Seek(count, SeekOrigin.Current);
        }
    }

}
