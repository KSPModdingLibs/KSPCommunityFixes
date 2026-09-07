using System;
using System.Collections.Generic;
using System.IO;
using PartToolsLib;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model;

/// <summary>
/// Compiles a <c>.mu</c> file into a <see cref="CompiledModel"/>.
/// </summary>
internal sealed unsafe class MuModelCompiler
{
    // ---- Per-Compile instance state ------------------------------

    private MuBinaryReader reader;

    private string fileUrl;
    private string directoryUrl;
    private int version;

    // Running slot counter. Slots are indices into the driver's locals[] array.
    private int nextSlot;

    // Global mesh index over the whole file, used to build a globally-unique canonical mesh name.
    private int meshIndex;

    private readonly List<IModelInstruction> instructions = [];
    private readonly List<MeshBlob> blobs = [];
    private readonly List<MeshBinding> bindings = [];

    // Holds the renderer/emitter slots that reference material index i.
    private readonly List<MatRef> matRefs = [];

    // Pending materials in material-index order (0..materialCount-1).
    private readonly List<PendingMaterial> pendingMaterials = [];

    // Texture-slot table: textureSlots[slot] = every PendingTexture that references that texture slot.
    private readonly List<List<PendingTexture>> textureSlots = [];

    // Skinned renderers awaiting a ResolveBones step.
    private readonly List<SkinnedEntry> skinnedRenderers = [];

    // Bone names for the SkinnedMeshRenderer currently being parsed.
    private string[] currentBoneNames;

    private List<DeferredLog> logs;

    private readonly Action<string> warnSink;

    public MuModelCompiler()
    {
        warnSink = msg => logs.Add(new DeferredLog(LogType.Warning, msg));
    }

    /// <summary>Compiles one <c>.mu</c> file into a <see cref="CompiledModel"/>.</summary>
    /// <param name="fileUrl">
    ///   The model's <c>UrlFile.url</c>; used for globally-unique mesh names and
    ///   <see cref="CompiledModel.SourceUrl"/>.
    /// </param>
    /// <param name="directoryUrl">The model's <c>file.parent.url</c>; used to build texture urls.</param>
    /// <param name="data">Raw model bytes. Must be pinnable (a plain managed array).</param>
    /// <param name="dataLength">
    ///   Valid byte count in <paramref name="data"/>; if &lt;= 0 the whole
    ///   array length is used.
    /// </param>
    public CompiledModel Compile(string fileUrl, string directoryUrl, byte[] data, int dataLength)
    {
        ResetState();
        this.fileUrl = fileUrl;
        this.directoryUrl = directoryUrl;

        int length = dataLength <= 0 ? data.Length : dataLength;

        // MuBinaryReader holds a raw byte*, so the buffer must stay pinned for the whole parse;
        // nothing reads the stream after this block closes.
        fixed (byte* p = data)
        {
            reader = new MuBinaryReader(p, length);

            if (reader.ReadInt() != 76543)
                throw new Exception("Invalid mu file");

            version = reader.ReadInt();
            reader.SkipString();

            // Root is the first thing allocated, so it lands in slot 0 (parent = -1 == none).
            ReadChild(-1);
        }

        // Two-pass finalize: materials + textures are fully known now, so emit them (see
        // FinalizeMaterials), then the deferred bone resolution.
        FinalizeMaterials();
        FinalizeBones();

        return new CompiledModel
        {
            SourceUrl = fileUrl,
            Instructions = [.. instructions],
            Blobs = [.. blobs],
            Bindings = [.. bindings],
            LocalCount = nextSlot,
            Logs = logs,
        };
    }

    private void ResetState()
    {
        reader = default;
        fileUrl = null;
        directoryUrl = null;
        version = 0;
        nextSlot = 0;
        meshIndex = 0;
        instructions.Clear();
        blobs.Clear();
        bindings.Clear();
        matRefs.Clear();
        pendingMaterials.Clear();
        textureSlots.Clear();
        skinnedRenderers.Clear();
        currentBoneNames = null;
        logs = [];
    }

    // ---- Core tree walk ------------------------------------------------------------------------

    /// <summary>
    /// Reads one transform node and returns the allocated slot.
    /// </summary>
    private int ReadChild(int parentSlot)
    {
        string name = reader.ReadString();
        Vector3 pos = reader.ReadVector3();
        Quaternion rot = reader.ReadQuaternion();
        Vector3 scale = reader.ReadVector3();

        // Allocate this GameObject's slot and create it before processing any of its components, so
        // colliders/renderers/etc. below configure the correct current slot and children can parent
        // to it. Slot policy: a slot is allocated for the ROOT + every child GameObject, every mesh,
        // every material, every MeshRenderer/SkinnedMeshRenderer and every material-bound emitter;
        // NOT for MeshFilter/colliders/Light/Camera/Animation/tag+layer (those configure the current
        // GameObject slot inline).
        int mySlot = nextSlot++;
        instructions.Add(new CreateGameObject
        {
            Dst = mySlot,
            Parent = parentSlot,
            Name = name,
            Pos = pos,
            Rot = rot,
            Scale = scale,
        });

        // No default case: an unknown opcode is ignored — it consumes nothing, and the loop reads the
        // next int as the following opcode.
        while (reader.Position < reader.Length)
        {
            switch ((EntryType)reader.ReadInt())
            {
                case EntryType.ChildTransformStart: // 0
                    ReadChild(mySlot);
                    break;
                case EntryType.ChildTransformEnd: // 1
                    return mySlot;
                case EntryType.Animation: // 2
                    ReadAnimation(mySlot);
                    break;
                case EntryType.MeshCollider: // 3
                    ReadMeshCollider(mySlot);
                    break;
                case EntryType.SphereCollider: // 4
                    ReadSphereCollider(mySlot);
                    break;
                case EntryType.CapsuleCollider: // 5
                    ReadCapsuleCollider(mySlot);
                    break;
                case EntryType.BoxCollider: // 6
                    ReadBoxCollider(mySlot);
                    break;
                case EntryType.MeshFilter: // 7
                    ReadMeshFilter(mySlot);
                    break;
                case EntryType.MeshRenderer: // 8
                    ReadMeshRenderer(mySlot);
                    break;
                case EntryType.SkinnedMeshRenderer: // 9
                    ReadSkinnedMeshRenderer(mySlot);
                    break;
                case EntryType.Materials: // 10
                    ReadMaterials();
                    break;
                case EntryType.Textures: // 12
                    ReadTextures();
                    break;
                case EntryType.Light: // 23
                    ReadLight(mySlot);
                    break;
                case EntryType.TagAndLayer: // 24
                    ReadTagAndLayer(mySlot);
                    break;
                case EntryType.MeshCollider2: // 25
                    ReadMeshColliderWithTrigger(mySlot);
                    break;
                case EntryType.SphereCollider2: // 26
                    ReadSphereColliderWithTrigger(mySlot);
                    break;
                case EntryType.CapsuleCollider2: // 27
                    ReadCapsuleColliderWithTrigger(mySlot);
                    break;
                case EntryType.BoxCollider2: // 28
                    ReadBoxColliderWithTrigger(mySlot);
                    break;
                case EntryType.WheelCollider: // 29
                    ReadWheelCollider(mySlot);
                    break;
                case EntryType.Camera: // 30
                    ReadCamera(mySlot);
                    break;
                case EntryType.ParticleEmitter: // 31
                    ReadParticles(mySlot);
                    break;
            }
        }

        return mySlot;
    }

    // ---- Component readers ---------------------------------------------------------------------

    private void ReadAnimation(int goSlot)
    {
        int clipCount = reader.ReadInt();
        var clips = new AnimationClipData[clipCount];
        for (int i = 0; i < clipCount; i++)
        {
            string clipName = reader.ReadString();
            Vector3 boundsCenter = reader.ReadVector3();
            Vector3 boundsSize = reader.ReadVector3();
            int wrapMode = reader.ReadInt();

            int curveCount = reader.ReadInt();
            var curves = new AnimationCurveData[curveCount];
            for (int j = 0; j < curveCount; j++)
            {
                string curvePath = reader.ReadString();
                string curveProperty = reader.ReadString();
                int typeCode = reader.ReadInt();
                int preWrap = reader.ReadInt();
                int postWrap = reader.ReadInt();

                int keyFrameCount = reader.ReadInt();
                var keys = new KeyframeData[keyFrameCount];
                for (int k = 0; k < keyFrameCount; k++)
                {
                    // The .mu keyframe record is 20 bytes (4 floats + 4 pad); only the four floats are kept.
                    Keyframe kf = reader.ReadKeyFrame();
                    keys[k] = new KeyframeData
                    {
                        Time = kf.time,
                        Value = kf.value,
                        InTangent = kf.inTangent,
                        OutTangent = kf.outTangent,
                    };
                }

                curves[j] = new AnimationCurveData
                {
                    Path = curvePath,
                    Property = curveProperty,
                    TypeCode = typeCode,
                    PreWrap = preWrap,
                    PostWrap = postWrap,
                    Keys = keys,
                };
            }

            clips[i] = new AnimationClipData
            {
                Name = clipName,
                BoundsCenter = boundsCenter,
                BoundsSize = boundsSize,
                WrapMode = wrapMode,
                Curves = curves,
            };
        }

        // ReadString returns string.Empty (never null) for an empty string, which is what AddAnimation's
        // "DefaultClip != string.Empty" guard relies on.
        string defaultClip = reader.ReadString();
        bool playAutomatically = reader.ReadBool();

        instructions.Add(new AddAnimation
        {
            Go = goSlot,
            Clips = clips,
            DefaultClip = defaultClip,
            PlayAutomatically = playAutomatically,
        });
    }

    private void ReadMeshCollider(int goSlot)
    {
        reader.SkipBool(); // "convex" bool, ignored (forced true in AddMeshCollider)
        int meshSlot = ReadMesh();
        instructions.Add(new AddMeshCollider
        {
            Go = goSlot,
            HasTrigger = false,
            IsTrigger = false,
            MeshSlot = meshSlot,
        });
    }

    private void ReadMeshColliderWithTrigger(int goSlot)
    {
        bool isTrigger = reader.ReadBool();
        reader.SkipBool(); // "convex" bool, ignored
        int meshSlot = ReadMesh();
        instructions.Add(new AddMeshCollider
        {
            Go = goSlot,
            HasTrigger = true,
            IsTrigger = isTrigger,
            MeshSlot = meshSlot,
        });
    }

    private void ReadSphereCollider(int goSlot)
    {
        float radius = reader.ReadFloat();
        Vector3 center = reader.ReadVector3();
        instructions.Add(new AddSphereCollider
        {
            Go = goSlot,
            HasTrigger = false,
            Radius = radius,
            Center = center,
        });
    }

    private void ReadSphereColliderWithTrigger(int goSlot)
    {
        bool isTrigger = reader.ReadBool();
        float radius = reader.ReadFloat();
        Vector3 center = reader.ReadVector3();
        instructions.Add(new AddSphereCollider
        {
            Go = goSlot,
            HasTrigger = true,
            IsTrigger = isTrigger,
            Radius = radius,
            Center = center,
        });
    }

    private void ReadCapsuleCollider(int goSlot)
    {
        float radius = reader.ReadFloat();
        int direction = reader.ReadInt();
        Vector3 center = reader.ReadVector3();
        instructions.Add(new AddCapsuleCollider
        {
            Go = goSlot,
            HasTrigger = false,
            Radius = radius,
            HasHeight = false,
            Direction = direction,
            Center = center,
        });
    }

    private void ReadCapsuleColliderWithTrigger(int goSlot)
    {
        bool isTrigger = reader.ReadBool();
        float radius = reader.ReadFloat();
        float height = reader.ReadFloat();
        int direction = reader.ReadInt();
        Vector3 center = reader.ReadVector3();
        instructions.Add(new AddCapsuleCollider
        {
            Go = goSlot,
            HasTrigger = true,
            IsTrigger = isTrigger,
            Radius = radius,
            HasHeight = true,
            Height = height,
            Direction = direction,
            Center = center,
        });
    }

    private void ReadBoxCollider(int goSlot)
    {
        Vector3 size = reader.ReadVector3();
        Vector3 center = reader.ReadVector3();
        instructions.Add(new AddBoxCollider
        {
            Go = goSlot,
            HasTrigger = false,
            Size = size,
            Center = center,
        });
    }

    private void ReadBoxColliderWithTrigger(int goSlot)
    {
        bool isTrigger = reader.ReadBool();
        Vector3 size = reader.ReadVector3();
        Vector3 center = reader.ReadVector3();
        instructions.Add(new AddBoxCollider
        {
            Go = goSlot,
            HasTrigger = true,
            IsTrigger = isTrigger,
            Size = size,
            Center = center,
        });
    }

    private void ReadWheelCollider(int goSlot)
    {
        float mass = reader.ReadFloat();
        float radius = reader.ReadFloat();
        float suspensionDistance = reader.ReadFloat();
        Vector3 center = reader.ReadVector3();
        float springSpring = reader.ReadFloat();
        float springDamper = reader.ReadFloat();
        float springTarget = reader.ReadFloat();

        // Friction curve fields in read order:
        // extremumSlip, extremumValue, asymptoteSlip, asymptoteValue, stiffness.
        float[] forward = new float[5];
        for (int i = 0; i < 5; i++)
            forward[i] = reader.ReadFloat();
        float[] sideways = new float[5];
        for (int i = 0; i < 5; i++)
            sideways[i] = reader.ReadFloat();

        instructions.Add(new AddWheelCollider
        {
            Go = goSlot,
            Mass = mass,
            Radius = radius,
            SuspensionDistance = suspensionDistance,
            Center = center,
            SpringSpring = springSpring,
            SpringDamper = springDamper,
            SpringTarget = springTarget,
            Forward = forward,
            Sideways = sideways,
        });
    }

    private void ReadMeshFilter(int goSlot)
    {
        int meshSlot = ReadMesh();
        instructions.Add(new AddMeshFilter { Go = goSlot, MeshSlot = meshSlot });
    }

    private void ReadMeshRenderer(int goSlot)
    {
        int rendererSlot = nextSlot++;

        bool hasShadowFlags = version >= 1;
        bool castShadows = false;
        bool receiveShadows = false;
        if (hasShadowFlags)
        {
            castShadows = reader.ReadBool();
            receiveShadows = reader.ReadBool();
        }

        instructions.Add(new AddMeshRenderer
        {
            Go = goSlot,
            Dst = rendererSlot,
            HasShadowFlags = hasShadowFlags,
            CastShadows = castShadows,
            ReceiveShadows = receiveShadows,
        });

        int rendererCount = reader.ReadInt();
        for (int i = 0; i < rendererCount; i++)
        {
            int materialIndex = reader.ReadInt();
            EnsureMatRef(materialIndex);
            matRefs[materialIndex].Renderers.Add(rendererSlot);
        }
    }

    private void ReadSkinnedMeshRenderer(int goSlot)
    {
        int smrSlot = nextSlot++;

        int rendererCount = reader.ReadInt();
        for (int i = 0; i < rendererCount; i++)
        {
            int materialIndex = reader.ReadInt();
            EnsureMatRef(materialIndex);
            // SkinnedMeshRenderer is a Renderer, so it shares the renderer material fan-out.
            matRefs[materialIndex].Renderers.Add(smrSlot);
        }

        Vector3 boundsCenter = reader.ReadVector3();
        Vector3 boundsSize = reader.ReadVector3();
        int quality = reader.ReadInt();
        bool updateWhenOffscreen = reader.ReadBool();

        int boneCount = reader.ReadInt();
        var boneNames = new string[boneCount];
        for (int j = 0; j < boneCount; j++)
            boneNames[j] = reader.ReadString();

        // Hand the SMR's bone names to ReadMesh so the skinned mesh blob attaches them index-aligned
        // with the bind poses it reads (BoneNames[i] <-> BindPoses[i]). Cleared right after so a later
        // non-skinned ReadMesh can't pick up stale names.
        currentBoneNames = boneNames;
        int meshSlot = ReadMesh();
        currentBoneNames = null;

        instructions.Add(new AddSkinnedMeshRenderer
        {
            Go = goSlot,
            Dst = smrSlot,
            LocalBounds = new Bounds(boundsCenter, boundsSize),
            Quality = quality,
            UpdateWhenOffscreen = updateWhenOffscreen,
            MeshSlot = meshSlot,
        });

        skinnedRenderers.Add(new SkinnedEntry { SmrSlot = smrSlot, BoneNames = boneNames });
    }

    private void ReadMaterials()
    {
        // Assumes exactly ONE Materials block per .mu, as all real files have: a second Materials block
        // would append into pendingMaterials and shift the second block's material indices. This is a
        // documented latent structural assumption, not a bug to fix.
        int materialCount = reader.ReadInt();
        for (int i = 0; i < materialCount; i++)
        {
            PendingMaterial pending = version < 4 ? ReadMaterial() : ReadMaterialV4();
            pending.Slot = nextSlot++;
            pendingMaterials.Add(pending); // index i == material index i
        }
    }

    private PendingMaterial ReadMaterial()
    {
        string name = reader.ReadString();
        ShaderType shaderType = (ShaderType)reader.ReadInt();

        var pm = new PendingMaterial
        {
            Name = name,
            Shader = new ShaderRef { ByName = false, Type = shaderType },
        };

        switch (shaderType)
        {
            default: // Custom (0), Diffuse (1) and any unknown type read just _MainTex
                ReadMaterialTexture(pm, "_MainTex");
                break;
            case ShaderType.Specular:
                ReadMaterialTexture(pm, "_MainTex");
                AddColor(pm, "_SpecColor", reader.ReadColor());
                AddFloat(pm, "_Shininess", reader.ReadFloat());
                break;
            case ShaderType.Bumped:
                ReadMaterialTexture(pm, "_MainTex");
                ReadMaterialTexture(pm, "_BumpMap");
                break;
            case ShaderType.BumpedSpecular:
                ReadMaterialTexture(pm, "_MainTex");
                ReadMaterialTexture(pm, "_BumpMap");
                AddColor(pm, "_SpecColor", reader.ReadColor());
                AddFloat(pm, "_Shininess", reader.ReadFloat());
                break;
            case ShaderType.Emissive:
                ReadMaterialTexture(pm, "_MainTex");
                ReadMaterialTexture(pm, "_Emissive");
                AddColor(pm, "_EmissiveColor", reader.ReadColor());
                break;
            case ShaderType.EmissiveSpecular:
                ReadMaterialTexture(pm, "_MainTex");
                AddColor(pm, "_SpecColor", reader.ReadColor());
                AddFloat(pm, "_Shininess", reader.ReadFloat());
                ReadMaterialTexture(pm, "_Emissive");
                AddColor(pm, "_EmissiveColor", reader.ReadColor());
                break;
            case ShaderType.EmissiveBumpedSpecular:
                ReadMaterialTexture(pm, "_MainTex");
                ReadMaterialTexture(pm, "_BumpMap");
                AddColor(pm, "_SpecColor", reader.ReadColor());
                AddFloat(pm, "_Shininess", reader.ReadFloat());
                ReadMaterialTexture(pm, "_Emissive");
                AddColor(pm, "_EmissiveColor", reader.ReadColor());
                break;
            case ShaderType.AlphaCutout:
                ReadMaterialTexture(pm, "_MainTex");
                AddFloat(pm, "_Cutoff", reader.ReadFloat());
                break;
            case ShaderType.AlphaCutoutBumped:
                ReadMaterialTexture(pm, "_MainTex");
                ReadMaterialTexture(pm, "_BumpMap");
                AddFloat(pm, "_Cutoff", reader.ReadFloat());
                break;
            case ShaderType.Alpha:
                ReadMaterialTexture(pm, "_MainTex");
                break;
            case ShaderType.AlphaSpecular:
                ReadMaterialTexture(pm, "_MainTex");
                AddFloat(pm, "_Gloss", reader.ReadFloat());
                AddColor(pm, "_SpecColor", reader.ReadColor());
                AddFloat(pm, "_Shininess", reader.ReadFloat());
                break;
            case ShaderType.AlphaUnlit:
                ReadMaterialTexture(pm, "_MainTex");
                AddColor(pm, "_Color", reader.ReadColor());
                break;
            case ShaderType.Unlit:
                ReadMaterialTexture(pm, "_MainTex");
                AddColor(pm, "_Color", reader.ReadColor());
                break;
            case ShaderType.ParticleAlpha:
                ReadMaterialTexture(pm, "_MainTex");
                AddColor(pm, "_Color", reader.ReadColor());
                AddFloat(pm, "_InvFade", reader.ReadFloat());
                break;
            case ShaderType.ParticleAdditive:
                ReadMaterialTexture(pm, "_MainTex");
                AddColor(pm, "_Color", reader.ReadColor());
                AddFloat(pm, "_InvFade", reader.ReadFloat());
                break;
            case ShaderType.BumpedSpecularMap:
                ReadMaterialTexture(pm, "_MainTex");
                ReadMaterialTexture(pm, "_BumpMap");
                ReadMaterialTexture(pm, "_SpecMap");
                AddFloat(pm, "_SpecTint", reader.ReadFloat());
                AddFloat(pm, "_Shininess", reader.ReadFloat());
                break;
        }

        return pm;
    }

    private PendingMaterial ReadMaterialV4()
    {
        string matName = reader.ReadString();
        string shaderName = reader.ReadString();
        int propertyCount = reader.ReadInt();

        var pm = new PendingMaterial
        {
            Name = matName,
            Shader = new ShaderRef { ByName = true, Name = shaderName },
        };

        for (int i = 0; i < propertyCount; i++)
        {
            string propName = reader.ReadString();
            switch (reader.ReadInt())
            {
                case 0:
                    AddColor(pm, propName, reader.ReadColor());
                    break;
                case 1:
                    AddVector(pm, propName, reader.ReadVector4());
                    break;
                case 2:
                    AddFloat(pm, propName, reader.ReadFloat());
                    break;
                case 3:
                    AddFloat(pm, propName, reader.ReadFloat());
                    break;
                case 4:
                    ReadMaterialTexture(pm, propName);
                    break;
                    // No default: an unknown type code consumes nothing beyond the name.
            }
        }

        return pm;
    }

    private void ReadMaterialTexture(PendingMaterial pm, string propertyName)
    {
        int slotIndex = reader.ReadInt();
        Vector2 scale = reader.ReadVector2();
        Vector2 offset = reader.ReadVector2();

        var pt = new PendingTexture
        {
            Name = propertyName,
            Scale = scale,
            Offset = offset,
            Url = null,
            IsNormalMap = false,
        };
        pm.Textures.Add(pt);

        if (slotIndex != -1)
        {
            while (slotIndex >= textureSlots.Count)
                textureSlots.Add(new List<PendingTexture>());
            // One texture slot can be referenced by many (material, property) pairs; the resolved url
            // is fanned out to every registered PendingTexture in ReadTextures.
            textureSlots[slotIndex].Add(pt);
        }
    }

    private void ReadTextures()
    {
        int texCount = reader.ReadInt();

        // texCount guard: on mismatch, log and return immediately without reading the texture entries,
        // so no url is assigned (every PendingTexture keeps Url == null) and the cursor stops right
        // after texCount.
        if (texCount != textureSlots.Count)
        {
            logs.Add(new DeferredLog(LogType.Error, "TextureError: " + texCount + " " + textureSlots.Count));
            return;
        }

        for (int i = 0; i < texCount; i++)
        {
            string name = reader.ReadString();
            TextureType textureType = (TextureType)reader.ReadInt();

            string url = directoryUrl + "/" + Path.GetFileNameWithoutExtension(name);
            // IsNormalMap is derived from the TEXTURE entry's type, not from any shader slot name.
            bool isNormalMap = textureType == TextureType.NormalMap;

            List<PendingTexture> refs = textureSlots[i];
            for (int j = 0; j < refs.Count; j++)
            {
                refs[j].Url = url;
                refs[j].IsNormalMap = isNormalMap;
            }
        }
    }

    private void ReadLight(int goSlot)
    {
        int type = reader.ReadInt();
        float intensity = reader.ReadFloat();
        float range = reader.ReadFloat();
        Color color = reader.ReadColor();
        int cullingMask = reader.ReadInt();

        bool hasSpotAngle = version > 1;
        float spotAngle = hasSpotAngle ? reader.ReadFloat() : 0f;

        instructions.Add(new AddLight
        {
            Go = goSlot,
            Type = type,
            Intensity = intensity,
            Range = range,
            Color = color,
            CullingMask = cullingMask,
            HasSpotAngle = hasSpotAngle,
            SpotAngle = spotAngle,
        });
    }

    private void ReadTagAndLayer(int goSlot)
    {
        string tag = reader.ReadString();
        int layer = reader.ReadInt();
        instructions.Add(new SetTagAndLayer { Go = goSlot, Tag = tag, Layer = layer });
    }

    private void ReadCamera(int goSlot)
    {
        int clearFlags = reader.ReadInt();
        Color backgroundColor = reader.ReadColor();
        int cullingMask = reader.ReadInt();
        bool orthographic = reader.ReadBool();
        float fieldOfView = reader.ReadFloat();
        float nearClip = reader.ReadFloat();
        float farClip = reader.ReadFloat();
        float depth = reader.ReadFloat();

        instructions.Add(new AddCamera
        {
            Go = goSlot,
            ClearFlags = clearFlags,
            BackgroundColor = backgroundColor,
            CullingMask = cullingMask,
            Orthographic = orthographic,
            FieldOfView = fieldOfView,
            NearClip = nearClip,
            FarClip = farClip,
            Depth = depth,
        });
    }

    private void ReadParticles(int goSlot)
    {
        int emitterSlot = nextSlot++;

        var d = new ParticleEmitterData();
        d.Emit = reader.ReadBool();
        d.Shape = reader.ReadInt();
        // Arguments evaluate left-to-right, so the reads fill x, y, z in order.
        d.Shape3D = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        d.Shape2D = new Vector2(reader.ReadFloat(), reader.ReadFloat());
        d.Shape1D = reader.ReadFloat();
        d.Color = reader.ReadColor();
        d.UseWorldSpace = reader.ReadBool();
        d.MinSize = reader.ReadFloat();
        d.MaxSize = reader.ReadFloat();
        d.MinEnergy = reader.ReadFloat();
        d.MaxEnergy = reader.ReadFloat();
        d.MinEmission = reader.ReadInt();
        d.MaxEmission = reader.ReadInt();
        d.WorldVelocity = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        d.LocalVelocity = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        d.RndVelocity = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        d.EmitterVelocityScale = reader.ReadFloat();
        d.AngularVelocity = reader.ReadFloat();
        d.RndAngularVelocity = reader.ReadFloat();
        d.RndRotation = reader.ReadBool();
        d.DoesAnimateColor = reader.ReadBool();
        var colorAnimation = new Color[5]; // the color animation is always exactly 5 entries
        for (int i = 0; i < 5; i++)
            colorAnimation[i] = reader.ReadColor();
        d.ColorAnimation = colorAnimation;
        d.WorldRotationAxis = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        d.LocalRotationAxis = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        d.SizeGrow = reader.ReadFloat();
        d.RndForce = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        d.Force = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        d.Damping = reader.ReadFloat();
        d.CastShadows = reader.ReadBool();
        d.RecieveShadows = reader.ReadBool();
        d.LengthScale = reader.ReadFloat();
        d.VelocityScale = reader.ReadFloat();
        d.MaxParticleSize = reader.ReadFloat();
        d.RenderModeCode = reader.ReadInt(); // raw code; AddParticleEmitter maps it to the render mode
        d.UvAnimationXTile = reader.ReadInt();
        d.UvAnimationYTile = reader.ReadInt();
        d.UvAnimationCycles = reader.ReadInt();

        instructions.Add(new AddParticleEmitter { Go = goSlot, Dst = emitterSlot, Data = d });

        int materialIndex = reader.ReadInt();
        EnsureMatRef(materialIndex);
        matRefs[materialIndex].Emitters.Add(emitterSlot);
    }

    // ---- Mesh parsing --------------------------------------------------------------------------

    /// <summary>Reads a mesh and returns the allocated mesh slot.</summary>
    private int ReadMesh()
    {
        EntryType entryType = (EntryType)reader.ReadInt();
        if (entryType != EntryType.MeshStart)
        {
            // Corrupt stream: log "Mesh Error" and allocate a slot with NO binding, so locals[slot]
            // stays null (a null sharedMesh) and the walk continues without a replay crash.
            logs.Add(new DeferredLog(LogType.Error, "Mesh Error"));
            return nextSlot++;
        }

        int size = reader.ReadInt(); // vertex count for every per-vertex attribute
        reader.SkipInt();            // unknown field, skipped

        int index = meshIndex++;
        string canonicalName = MeshBundleBuilder.Canonicalize($"{fileUrl}#{index}");

        var arrays = new MeshBlobBuilder.Arrays();
        var triangles = new List<int[]>();

        EntryType subType;
        while ((subType = (EntryType)reader.ReadInt()) != EntryType.MeshEnd)
        {
            switch (subType)
            {
                case EntryType.MeshVertexColors:
                    {
                        var colors = new Color32[size];
                        reader.FillColor32Buffer(colors, size);
                        arrays.Colors = colors;
                        break;
                    }
                case EntryType.MeshVerts:
                    {
                        var verts = new Vector3[size];
                        reader.FillVector3Buffer(verts, size);
                        arrays.Vertices = verts;
                        break;
                    }
                case EntryType.MeshUV:
                    {
                        var uv0 = new Vector2[size];
                        reader.FillVector2Buffer(uv0, size);
                        arrays.Uv0 = uv0;
                        break;
                    }
                case EntryType.MeshUV2:
                    {
                        var uv1 = new Vector2[size];
                        reader.FillVector2Buffer(uv1, size);
                        arrays.Uv1 = uv1;
                        break;
                    }
                case EntryType.MeshNormals:
                    {
                        var normals = new Vector3[size];
                        reader.FillVector3Buffer(normals, size);
                        arrays.Normals = normals;
                        break;
                    }
                case EntryType.MeshTangents:
                    {
                        var tangents = new Vector4[size];
                        reader.FillVector4Buffer(tangents, size);
                        arrays.Tangents = tangents;
                        break;
                    }
                case EntryType.MeshTriangles:
                    {
                        int triangleCount = reader.ReadInt();
                        var tris = new int[triangleCount];
                        reader.FillIntBuffer(tris, triangleCount);
                        triangles.Add(tris); // one submesh per MeshTriangles block, in encounter order
                        break;
                    }
                case EntryType.MeshBoneWeights:
                    {
                        // One BoneWeight per vertex (four weights + four bone indices), stored so
                        // MeshBlobBuilder emits the BlendWeights (ch12) and BlendIndices (ch13) vertex
                        // channels.
                        var boneWeights = new BoneWeight[size];
                        for (int i = 0; i < size; i++)
                            boneWeights[i] = reader.ReadBoneWeight();
                        arrays.BoneWeights = boneWeights;
                        break;
                    }
                case EntryType.MeshBindPoses:
                    {
                        // One bind pose per bone; its length defines the bone count. Stored so the blob
                        // carries m_BindPose and its bone metadata.
                        int bindPosesCount = reader.ReadInt();
                        var bindPoses = new Matrix4x4[bindPosesCount];
                        for (int i = 0; i < bindPosesCount; i++)
                            bindPoses[i] = reader.ReadMatrix4x4();
                        arrays.BindPoses = bindPoses;
                        break;
                    }
            }
        }

        arrays.SubMeshTriangles = triangles.ToArray();

        // Skinned mesh: attach the SMR's bone names (set by ReadSkinnedMeshRenderer just before this
        // call) index-aligned with the bind poses just read, so BoneNames.Length == BindPoses.Length.
        // FromArrays hashes them into m_BoneNameHashes. These are the .mu's LEAF bone names — exactly
        // what ResolveBones/FindChildByName binds SkinnedMeshRenderer.bones by at replay. Unity
        // natively hashes each bone's FULL transform path, so the emitted hash won't byte-match Unity's
        // stored value; that is COSMETIC (binding is by name, and only the per-bone array COUNT is
        // structurally required). Full-path reconstruction is a possible future refinement, only if an
        // in-KSP issue ever implicates the hash value.
        if (arrays.BindPoses != null)
            arrays.BoneNames = ReconcileBoneNames(currentBoneNames, arrays.BindPoses.Length, canonicalName);

        MeshBlob blob = MeshBlobBuilder.FromArrays(canonicalName, in arrays, warnSink);
        blobs.Add(blob);

        int meshSlot = nextSlot++;
        bindings.Add(new MeshBinding(meshSlot, canonicalName));
        return meshSlot;
    }

    // ---- Finalize (two-pass materials/textures, then bones) ------------------------------------

    /// <summary>
    /// Emits the deferred material work.
    /// </summary>
    private void FinalizeMaterials()
    {
        for (int i = 0; i < pendingMaterials.Count; i++)
        {
            PendingMaterial pm = pendingMaterials[i];

            var textureProps = new TextureProp[pm.Textures.Count];
            for (int j = 0; j < pm.Textures.Count; j++)
            {
                PendingTexture pt = pm.Textures[j];
                textureProps[j] = new TextureProp
                {
                    Name = pt.Name,
                    Url = pt.Url,             // null when unresolved (slot -1, guard failed, or no Textures block)
                    IsNormalMap = pt.IsNormalMap,
                    Scale = pt.Scale,
                    Offset = pt.Offset,
                };
            }

            instructions.Add(new CreateMaterial
            {
                Dst = pm.Slot,
                Shader = pm.Shader,
                ValueProps = pm.Values.ToArray(),
                TextureProps = textureProps,
                Name = pm.Name,
            });

            // Renderers/emitters that referenced material index i. A material never referenced by any
            // renderer (matRefs shorter than pendingMaterials, or a defined-but-unused material) simply
            // gets empty fan-out lists — harmless.
            int[] rendererSlots = Array.Empty<int>();
            int[] emitterSlots = Array.Empty<int>();
            if (i < matRefs.Count)
            {
                MatRef mr = matRefs[i];
                rendererSlots = mr.Renderers.ToArray();
                emitterSlots = mr.Emitters.ToArray();
            }

            instructions.Add(new AssignMaterial
            {
                MaterialSlot = pm.Slot,
                RendererSlots = rendererSlots,
                EmitterSlots = emitterSlots,
            });
        }
    }

    /// <summary>Emits one <see cref="ResolveBones"/> per skinned renderer.</summary>
    private void FinalizeBones()
    {
        for (int i = 0; i < skinnedRenderers.Count; i++)
        {
            SkinnedEntry se = skinnedRenderers[i];
            instructions.Add(new ResolveBones
            {
                SmrSlot = se.SmrSlot,
                RootSlot = 0, // the root GameObject is always slot 0
                BoneNames = se.BoneNames,
            });
        }
    }

    /// <summary>
    /// Returns a bone-name array whose length equals the mesh's bind-pose count.
    /// If there aren't enough bones in the mu then it is padded with empty ones
    /// until it matches.
    /// </summary>
    private string[] ReconcileBoneNames(string[] smrBoneNames, int boneCount, string meshName)
    {
        int have = smrBoneNames?.Length ?? 0;
        if (have == boneCount)
            return smrBoneNames ?? [];

        var names = new string[boneCount];
        int copy = Math.Min(have, boneCount);
        for (int i = 0; i < copy; i++)
            names[i] = smrBoneNames[i];
        for (int i = copy; i < boneCount; i++)
            names[i] = string.Empty;

        return names;
    }

    // ---- Small helpers -------------------------------------------------------------------------

    private void EnsureMatRef(int index)
    {
        while (index >= matRefs.Count)
            matRefs.Add(new MatRef());
    }

    private static void AddColor(PendingMaterial pm, string name, Color value) =>
        pm.Values.Add(new ValueProp { Name = name, Kind = ValueProp.KindColor, ColorVal = value });

    private static void AddFloat(PendingMaterial pm, string name, float value) =>
        pm.Values.Add(new ValueProp { Name = name, Kind = ValueProp.KindFloat, FloatVal = value });

    private static void AddVector(PendingMaterial pm, string name, Vector4 value) =>
        pm.Values.Add(new ValueProp { Name = name, Kind = ValueProp.KindVector, VecVal = value });

    // ---- Private accumulator types -------------------------------------------------------------

    /// <summary>A material parsed but not yet emitted (its texture urls resolve after the walk).</summary>
    private sealed class PendingMaterial
    {
        public int Slot;
        public string Name;
        public ShaderRef Shader;
        public readonly List<ValueProp> Values = [];
        public readonly List<PendingTexture> Textures = [];
    }

    /// <summary>A material texture property whose url is resolved later.</summary>
    private sealed class PendingTexture
    {
        public string Name;
        public Vector2 Scale;
        public Vector2 Offset;
        public string Url;
        public bool IsNormalMap;
    }

    /// <summary>Renderer + emitter slots referencing one material index.</summary>
    private sealed class MatRef
    {
        public readonly List<int> Renderers = new List<int>();
        public readonly List<int> Emitters = new List<int>();
    }

    /// <summary>A skinned renderer awaiting bone resolution.</summary>
    private struct SkinnedEntry
    {
        public int SmrSlot;
        public string[] BoneNames;
    }
}
