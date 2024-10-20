using PartToolsLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPCommunityFixes.Library
{
    // For reference on my 5800X3D/DDR4, when disk reading isn't a factor and for loading 485MB worth of models :
    // - The stock parser has a throughput of 120 MB/s
    // - The KSPCF parser has a throughput of 290 MB/s
    // Roadblocks to further optimizations :
    // - We can't avoid making a copy of the mesh data, avoiding that would require Unity accepting Span<T> in the various Mesh.Set*() methods
    // - For some mesh data, the mu data layout doesn't match a continuous array of structs, so we have to parse those structs one by one
    // - Animation parsing is inerently slow due to two strings having to be parsed for every curve, plus other hard to overcome inefficiencies 
    // - Ideally, the dummy* data structures should avoid manipulating strings, would use dictionaries instead of lists, and could be reused instead of re-instantatied for every model.
    // Overall, further optimization probably won't be beneficial anyway, we will almost always be bottlenecked by disk read speed.

    /// <summary>
    /// Reimplementation of the stock mu model format parser (PartReader). Roughly 60% faster.
    /// </summary>
    internal class MuParser
    {
        private static readonly UTF8Encoding decoder = new UTF8Encoding();
        private static char[] charBuffer;

        private static int[] intBuffer;
        private static Vector2[] vector2Buffer;
        private static Vector3[] vector3Buffer;
        private static Vector4[] vector4Buffer;
        private static Color32[] color32Buffer;
        private static Keyframe[][] keyFrameBuffers;

        private static List<PartReader.MaterialDummy> matDummies;
        private static PartReader.TextureDummyList textureDummies;
        private static List<PartReader.BonesDummy> boneDummies;

        private static string modelDirectoryUrl;
        private static byte[] data;
        private static unsafe byte* dataPtr;
        private static int dataLength;
        private static int index;

        private static int version;

        /// <summary>
        /// Parse a mu model into a GameObject hierarchy
        /// </summary>
        /// <param name="modelDirectoryUrl">GameData relative path to the folder containing the model. For a model known by its <see cref="T:UrlDir.UrlFile"/>, this will be the <c>urlFile.parent.url</c> value.</param>
        /// <param name="data">A byte array containing the raw model file data</param>
        /// <param name="dataLength">The length of the data in the array. Ignored if zero.</param>
        public static unsafe GameObject Parse(string modelDirectoryUrl, byte[] data, int dataLength = 0)
        {
            if (matDummies == null)
                matDummies = new List<PartReader.MaterialDummy>();

            if (textureDummies == null)
                textureDummies = new PartReader.TextureDummyList();

            if (boneDummies == null)
                boneDummies = new List<PartReader.BonesDummy>();

            GameObject model;

            GCHandle pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);

            try
            {
                MuParser.modelDirectoryUrl = modelDirectoryUrl;
                MuParser.data = data;
                MuParser.dataLength = dataLength <= 0 ? data.Length : dataLength;
                dataPtr = (byte*)pinnedArray.AddrOfPinnedObject();
                index = 0;

                if (ReadInt() != 76543)
                    throw new Exception("Invalid mu file");

                version = ReadInt();
                SkipString();

                model = ReadChild(null);
                AffectSkinnedMeshRenderersBones(model);
            }
            catch (Exception e)
            {
                model = null;
                Debug.LogError($"Model {modelDirectoryUrl} error: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                pinnedArray.Free();
                MuParser.matDummies.Clear();
                MuParser.boneDummies.Clear();
                MuParser.textureDummies.Clear();
                MuParser.modelDirectoryUrl = null;
                MuParser.data = null;
                MuParser.dataPtr = null;
                MuParser.dataLength = 0;
                MuParser.index = 0;
                MuParser.version = 0;
            }

            return model;
        }

        /// <summary>
        /// Call this to release the memory used by the static buffers.
        /// This is safe to use at any point.
        /// </summary>
        public static void ReleaseBuffers()
        {
            intBuffer = null;
            vector2Buffer = null;
            vector3Buffer = null;
            vector4Buffer = null;
            color32Buffer = null;
            keyFrameBuffers = null;
            charBuffer = null;
            matDummies = null;
            textureDummies = null;
            boneDummies = null;
        }

        #region Core methods

        private static GameObject ReadChild(Transform parent)
        {
            GameObject gameObject = new GameObject(ReadString());

            gameObject.transform.parent = parent;
            gameObject.transform.localPosition = ReadVector3();
            gameObject.transform.localRotation = ReadQuaternion();
            gameObject.transform.localScale = ReadVector3();

            while (index < dataLength)
            {
                switch (ReadInt())
                {
                    case 0:
                        ReadChild(gameObject.transform);
                        break;
                    case 2:
                        ReadAnimation(gameObject);
                        break;
                    case 3:
                        ReadMeshCollider(gameObject);
                        break;
                    case 4:
                        ReadSphereCollider(gameObject);
                        break;
                    case 5:
                        ReadCapsuleCollider(gameObject);
                        break;
                    case 6:
                        ReadBoxCollider(gameObject);
                        break;
                    case 7:
                        ReadMeshFilter(gameObject);
                        break;
                    case 8:
                        ReadMeshRenderer(gameObject);
                        break;
                    case 9:
                        ReadSkinnedMeshRenderer(gameObject);
                        break;
                    case 10:
                        ReadMaterials();
                        break;
                    case 12:
                        ReadTextures(gameObject);
                        break;
                    case 23:
                        ReadLight(gameObject);
                        break;
                    case 24:
                        ReadTagAndLayer(gameObject);
                        break;
                    case 25:
                        ReadMeshCollider2(gameObject);
                        break;
                    case 26:
                        ReadSphereCollider2(gameObject);
                        break;
                    case 27:
                        ReadCapsuleCollider2(gameObject);
                        break;
                    case 28:
                        ReadBoxCollider2(gameObject);
                        break;
                    case 29:
                        ReadWheelCollider(gameObject);
                        break;
                    case 30:
                        ReadCamera(gameObject);
                        break;
                    case 31:
                        ReadParticles(gameObject);
                        break;
                    case 1:
                        return gameObject;
                }
            }
            return gameObject;
        }

        private static void AffectSkinnedMeshRenderersBones(GameObject model)
        {
            if (boneDummies.Count > 0)
            {
                int i = 0;
                for (int count = boneDummies.Count; i < count; i++)
                {
                    Transform[] array = new Transform[boneDummies[i].bones.Count];
                    int j = 0;
                    for (int count2 = boneDummies[i].bones.Count; j < count2; j++)
                    {
                        array[j] = FindChildByName(model.transform, boneDummies[i].bones[j]);
                    }
                    boneDummies[i].smr.bones = array;
                }
            }
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            if (parent.name == name)
            {
                return parent;
            }
            foreach (Transform item in parent)
            {
                Transform transform = FindChildByName(item, name);
                if (transform != null)
                {
                    return transform;
                }
            }
            return null;
        }

        #endregion

        #region Component parsers

        private static void ReadAnimation(GameObject o)
        {
            Animation animation = o.AddComponent<Animation>();
            int clipCount = ReadInt();
            bool isInvalid = false;
            for (int i = 0; i < clipCount; i++)
            {
                AnimationClip animationClip = new AnimationClip();
                animationClip.legacy = true;
                string clipName = ReadString();
                animationClip.localBounds = new Bounds(ReadVector3(), ReadVector3());
                animationClip.wrapMode = (WrapMode)ReadInt();

                int curveCount = ReadInt();
                for (int j = 0; j < curveCount; j++)
                {
                    string curvePath = ReadString();
                    string curveProperty = ReadString();
                    Type curveType = null;
                    switch (ReadInt())
                    {
                        case 0:
                            curveType = typeof(Transform);
                            break;
                        case 1:
                            curveType = typeof(Material);
                            break;
                        case 2:
                            curveType = typeof(Light);
                            break;
                        case 3:
                            curveType = typeof(AudioSource);
                            break;
                    }
                    WrapMode preWrapMode = (WrapMode)ReadInt();
                    WrapMode postWrapMode = (WrapMode)ReadInt();

                    int keyFrameCount = ReadInt();
                    Keyframe[] keyFrames = GetKeyFrameBuffer(keyFrameCount);
                    for (int k = 0; k < keyFrameCount; k++)
                        keyFrames[k] = ReadKeyFrame();

                    AnimationCurve animationCurve = new AnimationCurve(keyFrames);
                    animationCurve.preWrapMode = preWrapMode;
                    animationCurve.postWrapMode = postWrapMode;

                    if (clipName == null || curvePath == null || curveType == null || curveProperty == null)
                    {
                        isInvalid = true;
                        Debug.LogWarning($"{clipName ?? "Null clipName"} : {curvePath ?? "Null curvePath"}, {(curveType == null ? "Null curveType" : curveType.ToString())}, {curveProperty ?? "Null curveProperty"}");
                        continue;
                    }

                    animationClip.SetCurve(curvePath, curveType, curveProperty, animationCurve);
                }
                if (!isInvalid)
                {
                    animation.AddClip(animationClip, clipName);
                }
            }
            string defaultclipName = ReadString();
            if (defaultclipName != string.Empty && !isInvalid)
                animation.clip = animation.GetClip(defaultclipName);

            animation.playAutomatically = ReadBool();
        }

        /// <summary>
        /// Usually, the curve will have less than 10 keyframes, this method will return a
        /// cached buffer instead of instantiatiating a new one in such cases.
        /// </summary>
        private static Keyframe[] GetKeyFrameBuffer(int keyFrameCount)
        {
            if (keyFrameBuffers == null)
            {
                keyFrameBuffers = new Keyframe[10][];
                keyFrameBuffers[0] = new Keyframe[0];
                keyFrameBuffers[1] = new Keyframe[1];
                keyFrameBuffers[2] = new Keyframe[2];
                keyFrameBuffers[3] = new Keyframe[3];
                keyFrameBuffers[4] = new Keyframe[4];
                keyFrameBuffers[5] = new Keyframe[5];
                keyFrameBuffers[6] = new Keyframe[6];
                keyFrameBuffers[7] = new Keyframe[7];
                keyFrameBuffers[8] = new Keyframe[8];
                keyFrameBuffers[9] = new Keyframe[9];
            }

            if (keyFrameCount < 10)
                return keyFrameBuffers[keyFrameCount];

            return new Keyframe[keyFrameCount];
        }

        private static void ReadMeshCollider(GameObject o)
        {
            MeshCollider meshCollider = o.AddComponent<MeshCollider>();
            SkipBool(); // this is actually the "convex" property, but it is always forced to true
            meshCollider.convex = true;
            meshCollider.sharedMesh = ReadMesh();
        }

        private static void ReadSphereCollider(GameObject o)
        {
            SphereCollider sphereCollider = o.AddComponent<SphereCollider>();
            sphereCollider.radius = ReadFloat();
            sphereCollider.center = ReadVector3();
        }

        private static void ReadCapsuleCollider(GameObject o)
        {
            CapsuleCollider capsuleCollider = o.AddComponent<CapsuleCollider>();
            capsuleCollider.radius = ReadFloat();
            capsuleCollider.direction = ReadInt();
            capsuleCollider.center = ReadVector3();
        }

        private static void ReadBoxCollider(GameObject o)
        {
            BoxCollider boxCollider = o.AddComponent<BoxCollider>();
            boxCollider.size = ReadVector3();
            boxCollider.center = ReadVector3();
        }

        private static void ReadMeshFilter(GameObject o)
        {
            o.AddComponent<MeshFilter>().sharedMesh = ReadMesh();
        }

        private static void ReadMeshRenderer(GameObject o)
        {
            MeshRenderer meshRenderer = o.AddComponent<MeshRenderer>();
            if (version >= 1)
            {
                meshRenderer.shadowCastingMode = ReadBool() ? ShadowCastingMode.On : ShadowCastingMode.Off;
                meshRenderer.receiveShadows = ReadBool();
            }
            int rendererCount = ReadInt();
            for (int i = 0; i < rendererCount; i++)
            {
                int materialCount = ReadInt();
                while (materialCount >= matDummies.Count)
                    matDummies.Add(new PartReader.MaterialDummy());

                matDummies[materialCount].renderers.Add(meshRenderer);
            }
        }

        private static void ReadSkinnedMeshRenderer(GameObject o)
        {
            SkinnedMeshRenderer skinnedMeshRenderer = o.AddComponent<SkinnedMeshRenderer>();
            int rendererCount = ReadInt();
            for (int i = 0; i < rendererCount; i++)
            {
                int materialCount = ReadInt();
                while (materialCount >= matDummies.Count)
                {
                    matDummies.Add(new PartReader.MaterialDummy());
                }
                matDummies[materialCount].renderers.Add(skinnedMeshRenderer);
            }
            skinnedMeshRenderer.localBounds = new Bounds(ReadVector3(), ReadVector3());
            skinnedMeshRenderer.quality = (SkinQuality)ReadInt();
            skinnedMeshRenderer.updateWhenOffscreen = ReadBool();
            int num3 = ReadInt();

            PartReader.BonesDummy bonesDummy = new PartReader.BonesDummy();
            bonesDummy.smr = skinnedMeshRenderer;
            for (int j = 0; j < num3; j++)
            {
                bonesDummy.bones.Add(ReadString());
            }
            boneDummies.Add(bonesDummy);
            skinnedMeshRenderer.sharedMesh = ReadMesh();
        }

        private static void ReadMaterials()
        {
            int materialCount = ReadInt();
            for (int i = 0; i < materialCount; i++)
            {
                PartReader.MaterialDummy materialDummy = matDummies[i];
                Material material = version < 4 ? ReadMaterial() : ReadMaterial4();

                for (int j = materialDummy.renderers.Count; j-- > 0;)
                    materialDummy.renderers[j].sharedMaterial = material;

                for (int j = materialDummy.particleEmitters.Count; j-- > 0;)
                    materialDummy.particleEmitters[j].material = material;
            }
        }

        private static Material ReadMaterial()
        {
            string name = ReadString();
            ShaderType shaderType = (ShaderType)ReadInt();
            Material material = new Material(ShaderHelpers.GetShader(shaderType));
            switch (shaderType)
            {
                default:
                    
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    break;
                case ShaderType.Specular:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    material.SetColor(ShaderHelpers.SpecColorPropId, ReadColor());
                    material.SetFloat(ShaderHelpers.ShininessPropId, ReadFloat());
                    break;
                case ShaderType.Bumped:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    ReadMaterialTexture(material, "_BumpMap", ShaderHelpers.BumpMapPropId);
                    break;
                case ShaderType.BumpedSpecular:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    ReadMaterialTexture(material, "_BumpMap", ShaderHelpers.BumpMapPropId);
                    material.SetColor(ShaderHelpers.SpecColorPropId, ReadColor());
                    material.SetFloat(ShaderHelpers.ShininessPropId, ReadFloat());
                    break;
                case ShaderType.Emissive:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    ReadMaterialTexture(material, "_Emissive", ShaderHelpers.EmissivePropId);
                    material.SetColor(PropertyIDs._EmissiveColor, ReadColor());
                    break;
                case ShaderType.EmissiveSpecular:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    material.SetColor(ShaderHelpers.SpecColorPropId, ReadColor());
                    material.SetFloat(ShaderHelpers.ShininessPropId, ReadFloat());
                    ReadMaterialTexture(material, "_Emissive", ShaderHelpers.EmissivePropId);
                    material.SetColor(PropertyIDs._EmissiveColor, ReadColor());
                    break;
                case ShaderType.EmissiveBumpedSpecular:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    ReadMaterialTexture(material, "_BumpMap", ShaderHelpers.BumpMapPropId);
                    material.SetColor(ShaderHelpers.SpecColorPropId, ReadColor());
                    material.SetFloat(ShaderHelpers.ShininessPropId, ReadFloat());
                    ReadMaterialTexture(material, "_Emissive", ShaderHelpers.EmissivePropId);
                    material.SetColor(PropertyIDs._EmissiveColor, ReadColor());
                    break;
                case ShaderType.AlphaCutout:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    material.SetFloat(ShaderHelpers.CutoffPropId, ReadFloat());
                    break;
                case ShaderType.AlphaCutoutBumped:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    ReadMaterialTexture(material, "_BumpMap", ShaderHelpers.BumpMapPropId);
                    material.SetFloat(ShaderHelpers.CutoffPropId, ReadFloat());
                    break;
                case ShaderType.Alpha:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    break;
                case ShaderType.AlphaSpecular:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    material.SetFloat(ShaderHelpers.GlossPropId, ReadFloat());
                    material.SetColor(ShaderHelpers.SpecColorPropId, ReadColor());
                    material.SetFloat(ShaderHelpers.ShininessPropId, ReadFloat());
                    break;
                case ShaderType.AlphaUnlit:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    material.SetColor(ShaderHelpers.ColorPropId, ReadColor());
                    break;
                case ShaderType.Unlit:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    material.SetColor(ShaderHelpers.ColorPropId, ReadColor());
                    break;
                case ShaderType.ParticleAlpha:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    material.SetColor(ShaderHelpers.ColorPropId, ReadColor());
                    material.SetFloat(ShaderHelpers.InvFadePropId, ReadFloat());
                    break;
                case ShaderType.ParticleAdditive:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    material.SetColor(ShaderHelpers.ColorPropId, ReadColor());
                    material.SetFloat(ShaderHelpers.InvFadePropId, ReadFloat());
                    break;
                case ShaderType.BumpedSpecularMap:
                    ReadMaterialTexture(material, "_MainTex", ShaderHelpers.MainTexPropId);
                    ReadMaterialTexture(material, "_BumpMap", ShaderHelpers.BumpMapPropId);
                    ReadMaterialTexture(material, "_SpecMap", ShaderHelpers.SpecMapPropId);
                    material.SetFloat(ShaderHelpers.SpecTintPropId, ReadFloat());
                    material.SetFloat(ShaderHelpers.ShininessPropId, ReadFloat());
                    break;
            }

            material.name = name;
            return material;
        }

        private static Material ReadMaterial4()
        {
            string matName = ReadString();
            string shaderName = ReadString();
            int propertyCount = ReadInt();
            Material material = new Material(Shader.Find(shaderName));
            material.name = matName;
            for (int i = 0; i < propertyCount; i++)
            {
                string text = ReadString();
                switch (ReadInt())
                {
                    case 0:
                        material.SetColor(text, ReadColor());
                        break;
                    case 1:
                        material.SetVector(text, ReadVector4());
                        break;
                    case 2:
                        material.SetFloat(text, ReadFloat());
                        break;
                    case 3:
                        material.SetFloat(text, ReadFloat());
                        break;
                    case 4:
                        ReadMaterialTexture(material, text, Shader.PropertyToID(text));
                        break;
                }
            }
            return material;
        }

        private static void ReadMaterialTexture(Material mat, string textureName, int textureId)
        {
            // we would need to reimplement the whole texture/material dummy thing to get ride of
            // having to use the texture name. Probably doesn't matter much...
            textureDummies.AddTextureDummy(ReadInt(), mat, textureName);
            mat.SetTextureScale(textureId, ReadVector2());
            mat.SetTextureOffset(textureId, ReadVector2());
        }

        private static void ReadTextures(GameObject o)
        {
            int texCount = ReadInt();
            if (texCount != textureDummies.Count)
            {
                Debug.LogError("TextureError: " + texCount + " " + textureDummies.Count);
                return;
            }
            for (int i = 0; i < texCount; i++)
            {
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(ReadString());
                TextureType textureType = (TextureType)ReadInt();
                string url = modelDirectoryUrl + "/" + fileNameWithoutExtension;
                Texture2D texture = GameDatabase.Instance.GetTexture(url, textureType == TextureType.NormalMap);
                if (texture.IsNullOrDestroyed())
                {
                    Debug.LogError($"Texture '{url}' not found!");
                    continue;
                }
                int j = 0;
                for (int materialCount = textureDummies[i].Count; j < materialCount; j++)
                {
                    PartReader.TextureMaterialDummy textureMaterialDummy = textureDummies[i][j];
                    int k = 0;
                    for (int texPropCount = textureMaterialDummy.shaderName.Count; k < texPropCount; k++)
                    {
                        string texProperty = textureMaterialDummy.shaderName[k];
                        textureMaterialDummy.material.SetTexture(texProperty, texture);
                    }
                }
            }
        }

        private static void ReadLight(GameObject o)
        {
            Light light = o.AddComponent<Light>();

            light.type = (LightType)ReadInt();
            light.intensity = ReadFloat();
            light.range = ReadFloat();
            light.color = ReadColor();
            light.cullingMask = ReadInt();

            if (version > 1)
                light.spotAngle = ReadFloat();
        }

        private static void ReadTagAndLayer(GameObject o)
        {
            o.tag = ReadString();
            o.layer = ReadInt();
        }

        private static void ReadMeshCollider2(GameObject o)
        {
            MeshCollider meshCollider = o.AddComponent<MeshCollider>();
            meshCollider.convex = true;
            meshCollider.isTrigger = ReadBool();
            SkipBool(); // this is actually the "convex" property, but it is always forced to true;
            meshCollider.sharedMesh = ReadMesh();
        }

        private static void ReadSphereCollider2(GameObject o)
        {
            SphereCollider sphereCollider = o.AddComponent<SphereCollider>();
            sphereCollider.isTrigger = ReadBool();
            sphereCollider.radius = ReadFloat();
            sphereCollider.center = ReadVector3();
        }

        private static void ReadCapsuleCollider2(GameObject o)
        {
            CapsuleCollider capsuleCollider = o.AddComponent<CapsuleCollider>();
            capsuleCollider.isTrigger = ReadBool();
            capsuleCollider.radius = ReadFloat();
            capsuleCollider.height = ReadFloat();
            capsuleCollider.direction = ReadInt();
            capsuleCollider.center = ReadVector3();
        }

        private static void ReadBoxCollider2(GameObject o)
        {
            BoxCollider boxCollider = o.AddComponent<BoxCollider>();
            boxCollider.isTrigger = ReadBool();
            boxCollider.size = ReadVector3();
            boxCollider.center = ReadVector3();
        }

        private static void ReadWheelCollider(GameObject o)
        {
            WheelCollider wheelCollider = o.AddComponent<WheelCollider>();
            wheelCollider.mass = ReadFloat();
            wheelCollider.radius = ReadFloat();
            wheelCollider.suspensionDistance = ReadFloat();
            wheelCollider.center = ReadVector3();
            wheelCollider.suspensionSpring = new JointSpring
            {
                spring = ReadFloat(),
                damper = ReadFloat(),
                targetPosition = ReadFloat()
            };
            wheelCollider.forwardFriction = new WheelFrictionCurve
            {
                extremumSlip = ReadFloat(),
                extremumValue = ReadFloat(),
                asymptoteSlip = ReadFloat(),
                asymptoteValue = ReadFloat(),
                stiffness = ReadFloat()
            };
            wheelCollider.sidewaysFriction = new WheelFrictionCurve
            {
                extremumSlip = ReadFloat(),
                extremumValue = ReadFloat(),
                asymptoteSlip = ReadFloat(),
                asymptoteValue = ReadFloat(),
                stiffness = ReadFloat()
            };
            wheelCollider.enabled = false;
        }

        private static void ReadCamera(GameObject o)
        {
            Camera camera = o.AddComponent<Camera>();
            camera.clearFlags = (CameraClearFlags)ReadInt();
            camera.backgroundColor = ReadColor();
            camera.cullingMask = ReadInt();
            camera.orthographic = ReadBool();
            camera.fieldOfView = ReadFloat();
            camera.nearClipPlane = ReadFloat();
            camera.farClipPlane = ReadFloat();
            camera.depth = ReadFloat();
            camera.allowHDR = false;
            camera.enabled = false;
        }

        private static void ReadParticles(GameObject o)
        {
            KSPParticleEmitter kSPParticleEmitter = o.AddComponent<KSPParticleEmitter>();
            kSPParticleEmitter.emit = ReadBool();
            kSPParticleEmitter.shape = (KSPParticleEmitter.EmissionShape)ReadInt();
            kSPParticleEmitter.shape3D.x = ReadFloat();
            kSPParticleEmitter.shape3D.y = ReadFloat();
            kSPParticleEmitter.shape3D.z = ReadFloat();
            kSPParticleEmitter.shape2D.x = ReadFloat();
            kSPParticleEmitter.shape2D.y = ReadFloat();
            kSPParticleEmitter.shape1D = ReadFloat();
            kSPParticleEmitter.color = ReadColor();
            kSPParticleEmitter.useWorldSpace = ReadBool();
            kSPParticleEmitter.minSize = ReadFloat();
            kSPParticleEmitter.maxSize = ReadFloat();
            kSPParticleEmitter.minEnergy = ReadFloat();
            kSPParticleEmitter.maxEnergy = ReadFloat();
            kSPParticleEmitter.minEmission = ReadInt();
            kSPParticleEmitter.maxEmission = ReadInt();
            kSPParticleEmitter.worldVelocity.x = ReadFloat();
            kSPParticleEmitter.worldVelocity.y = ReadFloat();
            kSPParticleEmitter.worldVelocity.z = ReadFloat();
            kSPParticleEmitter.localVelocity.x = ReadFloat();
            kSPParticleEmitter.localVelocity.y = ReadFloat();
            kSPParticleEmitter.localVelocity.z = ReadFloat();
            kSPParticleEmitter.rndVelocity.x = ReadFloat();
            kSPParticleEmitter.rndVelocity.y = ReadFloat();
            kSPParticleEmitter.rndVelocity.z = ReadFloat();
            kSPParticleEmitter.emitterVelocityScale = ReadFloat();
            kSPParticleEmitter.angularVelocity = ReadFloat();
            kSPParticleEmitter.rndAngularVelocity = ReadFloat();
            kSPParticleEmitter.rndRotation = ReadBool();
            kSPParticleEmitter.doesAnimateColor = ReadBool();
            kSPParticleEmitter.colorAnimation = new Color[5];
            for (int i = 0; i < 5; i++)
            {
                kSPParticleEmitter.colorAnimation[i] = ReadColor();
            }
            kSPParticleEmitter.worldRotationAxis.x = ReadFloat();
            kSPParticleEmitter.worldRotationAxis.y = ReadFloat();
            kSPParticleEmitter.worldRotationAxis.z = ReadFloat();
            kSPParticleEmitter.localRotationAxis.x = ReadFloat();
            kSPParticleEmitter.localRotationAxis.y = ReadFloat();
            kSPParticleEmitter.localRotationAxis.z = ReadFloat();
            kSPParticleEmitter.sizeGrow = ReadFloat();
            kSPParticleEmitter.rndForce.x = ReadFloat();
            kSPParticleEmitter.rndForce.y = ReadFloat();
            kSPParticleEmitter.rndForce.z = ReadFloat();
            kSPParticleEmitter.force.x = ReadFloat();
            kSPParticleEmitter.force.y = ReadFloat();
            kSPParticleEmitter.force.z = ReadFloat();
            kSPParticleEmitter.damping = ReadFloat();
            kSPParticleEmitter.castShadows = ReadBool();
            kSPParticleEmitter.recieveShadows = ReadBool();
            kSPParticleEmitter.lengthScale = ReadFloat();
            kSPParticleEmitter.velocityScale = ReadFloat();
            kSPParticleEmitter.maxParticleSize = ReadFloat();
            switch (ReadInt())
            {
                default:
                    kSPParticleEmitter.particleRenderMode = ParticleSystemRenderMode.Billboard;
                    break;
                case 3:
                    kSPParticleEmitter.particleRenderMode = ParticleSystemRenderMode.Stretch;
                    break;
                case 4:
                    kSPParticleEmitter.particleRenderMode = ParticleSystemRenderMode.HorizontalBillboard;
                    break;
                case 5:
                    kSPParticleEmitter.particleRenderMode = ParticleSystemRenderMode.VerticalBillboard;
                    break;
            }
            kSPParticleEmitter.uvAnimationXTile = ReadInt();
            kSPParticleEmitter.uvAnimationYTile = ReadInt();
            kSPParticleEmitter.uvAnimationCycles = ReadInt();
            int num = ReadInt();
            while (num >= matDummies.Count)
            {
                matDummies.Add(new PartReader.MaterialDummy());
            }
            matDummies[num].particleEmitters.Add(kSPParticleEmitter);
        }

        #endregion

        #region Mesh parsing

        private static Mesh ReadMesh()
        {
            Mesh mesh = new Mesh();
            EntryType entryType = (EntryType)ReadInt();
            if (entryType != EntryType.MeshStart)
            {
                Debug.LogError("Mesh Error");
                return null;
            }

            int size = ReadInt();
            SkipInt();

            int subMeshIndex = 0;
            while ((entryType = (EntryType)ReadInt()) != EntryType.MeshEnd)
            {
                switch (entryType)
                {
                    case EntryType.MeshVertexColors:
                        {
                            FillColor32Buffer(size);
                            mesh.SetColors(color32Buffer, 0, size);

                            break;
                        }
                    case EntryType.MeshVerts:
                        {
                            FillVector3Buffer(size);
                            mesh.SetVertices(vector3Buffer, 0, size);

                            break;
                        }
                    case EntryType.MeshUV:
                        {
                            FillVector2Buffer(size);
                            mesh.SetUVs(0, vector2Buffer, 0, size);
                            break;
                        }
                    case EntryType.MeshUV2:
                        {
                            FillVector2Buffer(size);
                            mesh.SetUVs(1, vector2Buffer, 0, size);
                            break;
                        }
                    case EntryType.MeshNormals:
                        {
                            FillVector3Buffer(size);
                            mesh.SetNormals(vector3Buffer, 0, size);
                            break;
                        }
                    case EntryType.MeshTangents:
                        {
                            FillVector4Buffer(size);
                            mesh.SetTangents(vector4Buffer, 0, size);
                            break;
                        }
                    case EntryType.MeshTriangles:
                        {
                            if (mesh.subMeshCount == subMeshIndex)
                                mesh.subMeshCount++;

                            int triangleCount = ReadInt();
                            FillIntBuffer(triangleCount);
                            mesh.SetTriangles(intBuffer, 0, triangleCount, subMeshIndex);
                            subMeshIndex++;
                            break;
                        }
                    case EntryType.MeshBoneWeights:
                        {
                            BoneWeight[] boneWeights = new BoneWeight[size];
                            for (int i = 0; i < size; i++)
                                boneWeights[i] = ReadBoneWeight();

                            mesh.boneWeights = boneWeights;
                            break;
                        }
                    case EntryType.MeshBindPoses:
                        {
                            int bindPosesCount = ReadInt();
                            Matrix4x4[] bindPoses = new Matrix4x4[bindPosesCount];
                            for (int i = 0; i < bindPosesCount; i++)
                                bindPoses[i] = ReadMatrix4x4();

                            mesh.bindposes = bindPoses;
                            break;
                        }
                }
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        // note : to fill mesh data, we take advantage of the fact that the various
        // structs (int, vector3, etc) are sequentially packed in the raw data array.
        // Ideally, we would pass the raw data array with a pointer offset to the various
        // mesh.Set*() methods, but that would require those methods accepting a Span<T>
        // that we would built from an arbitrary pointer offset.
        // So we fallback to a memcopy of the raw data to a buffer array that we then
        // pass to the unity methods.

        private static unsafe void FillIntBuffer(int intCount)
        {
            int byteCount = intCount * 4;
            int valIdx = Advance(byteCount);

            if (intBuffer == null || intBuffer.Length < intCount)
                intBuffer = new int[(int)(intCount * 1.5)];

            fixed (int* intBufferPtr = intBuffer)
                Buffer.MemoryCopy(dataPtr + valIdx, intBufferPtr, byteCount, byteCount);
        }

        private static unsafe void FillVector2Buffer(int vector2Count)
        {
            int byteCount = vector2Count * 8;
            int valIdx = Advance(byteCount);

            if (vector2Buffer == null || vector2Buffer.Length < vector2Count)
                vector2Buffer = new Vector2[(int)(vector2Count * 1.5)];

            fixed (Vector2* vector2BufferPtr = vector2Buffer)
                Buffer.MemoryCopy(dataPtr + valIdx, vector2BufferPtr, byteCount, byteCount);
        }

        private static unsafe void FillVector3Buffer(int vector3Count)
        {
            int byteCount = vector3Count * 12;
            int valIdx = Advance(byteCount);

            if (vector3Buffer == null || vector3Buffer.Length < vector3Count)
                vector3Buffer = new Vector3[(int)(vector3Count * 1.5)];

            fixed (Vector3* vector3BufferPtr = vector3Buffer)
                Buffer.MemoryCopy(dataPtr + valIdx, vector3BufferPtr, byteCount, byteCount);
        }

        private static unsafe void FillVector4Buffer(int vector4Count)
        {
            int byteCount = vector4Count * 16;
            int valIdx = Advance(byteCount);

            if (vector4Buffer == null || vector4Buffer.Length < vector4Count)
                vector4Buffer = new Vector4[(int)(vector4Count * 1.5)];

            fixed (Vector4* vector4BufferPtr = vector4Buffer)
                Buffer.MemoryCopy(dataPtr + valIdx, vector4BufferPtr, byteCount, byteCount);
        }

        private static unsafe void FillColor32Buffer(int color32Count)
        {
            int byteCount = color32Count * 4;
            int valIdx = Advance(byteCount);

            if (color32Buffer == null || color32Buffer.Length < color32Count)
                color32Buffer = new Color32[(int)(color32Count * 1.5)];

            fixed (Color32* color32BufferPtr = color32Buffer)
                Buffer.MemoryCopy(dataPtr + valIdx, color32BufferPtr, byteCount, byteCount);
        }


        #endregion

        #region Base binary reader infrastructure

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Advance(int bytes)
        {
            int currentIndex = index;
            int nextIndex = currentIndex + bytes;
            if (nextIndex > dataLength)
                ThrowEndOfDataException();

            index = nextIndex;
            return currentIndex;
        }

        private static void ThrowEndOfDataException()
        {
            throw new InvalidOperationException("Unable to read beyond the end of the data");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe byte ReadByte()
        {
            int valIdx = Advance(1);
            return *(dataPtr + valIdx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SkipInt()
        {
            Advance(4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int ReadInt()
        {
            int valIdx = Advance(4);
            return *(int*)(dataPtr + valIdx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float ReadFloat()
        {
            int valIdx = Advance(4);
            return *(float*)(dataPtr + valIdx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SkipBool()
        {
            Advance(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool ReadBool()
        {
            int valIdx = Advance(1);
            return *(dataPtr + valIdx) != 0;
        }

        private static void SkipString()
        {
            Advance(Read7BitEncodedInt());
        }

        private static string ReadString()
        {
            int strByteLength = Read7BitEncodedInt();

            if (strByteLength < 0)
                throw new Exception("Invalid string length");

            if (strByteLength == 0)
                return string.Empty;

            ExpandCharBuffer(strByteLength);

            int currentPos = index;
            Advance(strByteLength);

            int charCount = decoder.GetChars(data, currentPos, strByteLength, charBuffer, 0);
            return new string(charBuffer, 0, charCount);
        }

        private static void ExpandCharBuffer(int length)
        {
            if (charBuffer == null || charBuffer.Length < length)
                charBuffer = new char[(int)(length * 1.5)];
        }

        private static int Read7BitEncodedInt()
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
                b = ReadByte();
                num |= (b & 0x7F) << num2;
                num2 += 7;
            }
            while ((b & 0x80u) != 0);
            return num;
        }

        private static unsafe Vector2 ReadVector2()
        {
            int valIdx = Advance(8);
            return *(Vector2*)(dataPtr + valIdx);
        }

        private static unsafe Vector3 ReadVector3()
        {
            int valIdx = Advance(12);
            return *(Vector3*)(dataPtr + valIdx);
        }

        private static unsafe Vector4 ReadVector4()
        {
            int valIdx = Advance(16);
            return *(Vector4*)(dataPtr + valIdx);
        }

        private static unsafe Quaternion ReadQuaternion()
        {
            int valIdx = Advance(16);
            return *(Quaternion*)(dataPtr + valIdx);
        }

        private static unsafe Color ReadColor()
        {
            int valIdx = Advance(16);
            return *(Color*)(dataPtr + valIdx);
        }

        private static unsafe Color32 ReadColor32()
        {
            int valIdx = Advance(4);
            return *(Color32*)(dataPtr + valIdx);
        }

        private static unsafe BoneWeight ReadBoneWeight()
        {
            int valIdx = Advance(32);
            // data isn't packed with the same layout as the struct, so we fallback to setting every field
            return new BoneWeight()
            {
                boneIndex0 = *(int*)(dataPtr + valIdx),
                weight0 =  *(float*)(dataPtr + valIdx + 4),
                boneIndex1 = *(int*)(dataPtr + valIdx + 8),
                weight1 =  *(float*)(dataPtr + valIdx + 12),
                boneIndex2 = *(int*)(dataPtr + valIdx + 16),
                weight2 =  *(float*)(dataPtr + valIdx + 20),
                boneIndex3 = *(int*)(dataPtr + valIdx + 24),
                weight3 =  *(float*)(dataPtr + valIdx + 28)
            };
        }

        private static unsafe Matrix4x4 ReadMatrix4x4()
        {
            int valIdx = Advance(64);
            // data isn't packed with the same layout as the struct, so we fallback to setting every field
            return new Matrix4x4()
            {
                m00 = *(float*)(dataPtr + valIdx),
                m01 = *(float*)(dataPtr + valIdx + 4),
                m02 = *(float*)(dataPtr + valIdx + 8),
                m03 = *(float*)(dataPtr + valIdx + 12),
                m10 = *(float*)(dataPtr + valIdx + 16),
                m11 = *(float*)(dataPtr + valIdx + 20),
                m12 = *(float*)(dataPtr + valIdx + 24),
                m13 = *(float*)(dataPtr + valIdx + 28),
                m20 = *(float*)(dataPtr + valIdx + 32),
                m21 = *(float*)(dataPtr + valIdx + 36),
                m22 = *(float*)(dataPtr + valIdx + 40),
                m23 = *(float*)(dataPtr + valIdx + 44),
                m30 = *(float*)(dataPtr + valIdx + 48),
                m31 = *(float*)(dataPtr + valIdx + 52),
                m32 = *(float*)(dataPtr + valIdx + 56),
                m33 = *(float*)(dataPtr + valIdx + 60)
            };
        }

        private static unsafe Keyframe ReadKeyFrame()
        {
            // this is encoded as 4 floats (16 bytes), but there is 4 bytes of padding at the end
            int valIdx = Advance(20);
            return new Keyframe(
                *(float*)(dataPtr + valIdx),
                *(float*)(dataPtr + valIdx + 4), 
                *(float*)(dataPtr + valIdx + 8), 
                *(float*)(dataPtr + valIdx + 12));
        }

        #endregion
    }
}
