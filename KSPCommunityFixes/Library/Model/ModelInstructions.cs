using System;
using KSPCommunityFixes.Library;
using PartToolsLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// One atomic, main-thread GameObject-assembly step of a <see cref="CompiledModel"/>. The background
    /// compiler bakes all the data (positions, enum codes, property values, resolved texture urls, ...)
    /// into the instruction's fields; <see cref="Execute"/> only does the <c>UnityEngine</c> calls, and
    /// reproduces the matching <see cref="KSPCommunityFixes.Library.MuParser"/> reader's object-building
    /// tail exactly. Slots are indices into the driver-owned <c>locals</c> array; <c>-1</c> means "none".
    /// No file reading happens here.
    /// </summary>
    internal interface IModelInstruction
    {
        void Execute(UnityEngine.Object[] locals);
    }

    // ---- Hierarchy ------------------------------------------------------------------------------

    /// <summary>Reproduces <c>MuParser.ReadChild</c>'s object-building head: create the GameObject,
    /// parent it, then set localPosition, localRotation, localScale IN THAT ORDER.</summary>
    internal sealed class CreateGameObject : IModelInstruction
    {
        public int Dst;
        public int Parent;
        public string Name;
        public Vector3 Pos;
        public Quaternion Rot;
        public Vector3 Scale;

        public void Execute(UnityEngine.Object[] locals)
        {
            GameObject go = new GameObject(Name);
            // Parity: MuParser assigns parent, then localPosition, localRotation, localScale in this order.
            go.transform.parent = Parent < 0 ? null : ((GameObject)locals[Parent]).transform;
            go.transform.localPosition = Pos;
            go.transform.localRotation = Rot;
            go.transform.localScale = Scale;
            locals[Dst] = go;
        }
    }

    /// <summary>Reproduces <c>MuParser.ReadTagAndLayer</c>.</summary>
    internal sealed class SetTagAndLayer : IModelInstruction
    {
        public int Go;
        public string Tag;
        public int Layer;

        public void Execute(UnityEngine.Object[] locals)
        {
            GameObject go = (GameObject)locals[Go];
            // Parity: MuParser sets the tag unguarded. Assigning an unregistered tag throws
            // UnityException; we reproduce that behaviour rather than swallowing it.
            go.tag = Tag;
            go.layer = Layer;
        }
    }

    // ---- Mesh / renderers -----------------------------------------------------------------------

    /// <summary>Reproduces <c>MuParser.ReadMeshFilter</c>.</summary>
    internal sealed class AddMeshFilter : IModelInstruction
    {
        public int Go;
        public int MeshSlot;

        public void Execute(UnityEngine.Object[] locals) =>
            ((GameObject)locals[Go]).AddComponent<MeshFilter>().sharedMesh = (Mesh)locals[MeshSlot];
    }

    /// <summary>Reproduces <c>MuParser.ReadMeshRenderer</c>. The shadow flags are only present for
    /// <c>version &gt;= 1</c> (baked as <see cref="HasShadowFlags"/>).</summary>
    internal sealed class AddMeshRenderer : IModelInstruction
    {
        public int Go;
        public int Dst;
        public bool HasShadowFlags;
        public bool CastShadows;
        public bool ReceiveShadows;

        public void Execute(UnityEngine.Object[] locals)
        {
            MeshRenderer mr = ((GameObject)locals[Go]).AddComponent<MeshRenderer>();
            if (HasShadowFlags)
            {
                // Parity: MuParser maps the single "cast shadows" bool to ShadowCastingMode.On/Off only
                // (never TwoSided/ShadowsOnly).
                mr.shadowCastingMode = CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.receiveShadows = ReceiveShadows;
            }
            locals[Dst] = mr;
        }
    }

    /// <summary>Reproduces <c>MuParser.ReadSkinnedMeshRenderer</c>'s object-building (bone resolution is
    /// a separate <see cref="ResolveBones"/> step). Deferred/not emitted in v1, but faithful.</summary>
    internal sealed class AddSkinnedMeshRenderer : IModelInstruction
    {
        public int Go;
        public int Dst;
        public Bounds LocalBounds;
        public int Quality;
        public bool UpdateWhenOffscreen;
        public int MeshSlot;

        public void Execute(UnityEngine.Object[] locals)
        {
            SkinnedMeshRenderer smr = ((GameObject)locals[Go]).AddComponent<SkinnedMeshRenderer>();
            smr.localBounds = LocalBounds;
            smr.quality = (SkinQuality)Quality;
            smr.updateWhenOffscreen = UpdateWhenOffscreen;
            smr.sharedMesh = (Mesh)locals[MeshSlot];
            locals[Dst] = smr;
        }
    }

    // ---- Materials / textures -------------------------------------------------------------------

    /// <summary>Reproduces the object-building of <c>MuParser.ReadMaterial</c> (v&lt;4) /
    /// <c>ReadMaterial4</c> (v&gt;=4) plus the deferred texture assignment from <c>ReadTextures</c>,
    /// folded into a single baked step. Value/texture property names are baked as strings by the
    /// compiler (the v&lt;4 path's int property IDs are just <c>Shader.PropertyToID(name)</c>, so the
    /// string setters are equivalent).</summary>
    internal sealed class CreateMaterial : IModelInstruction
    {
        public int Dst;
        public ShaderRef Shader;
        public ValueProp[] ValueProps;
        public TextureProp[] TextureProps;
        public string Name;

        public void Execute(UnityEngine.Object[] locals)
        {
            // Parity/RISK: Shader may resolve to null (unknown v>=4 shader name). MuParser does NOT
            // substitute a default there — new Material((Shader)null) is the intended behaviour — so we
            // must not coalesce to a fallback shader here. (The v<4 by-type route always resolves to a
            // concrete KSP shader.) See ShaderRef.Resolve.
            Material mat = new Material(Shader.Resolve());
            mat.name = Name;

            ValueProp[] values = ValueProps;
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    ValueProp v = values[i];
                    switch (v.Kind)
                    {
                        case ValueProp.KindColor:
                            mat.SetColor(v.Name, v.ColorVal);
                            break;
                        case ValueProp.KindVector:
                            mat.SetVector(v.Name, v.VecVal);
                            break;
                        default: // KindFloat (covers MuParser's material property type codes 2 and 3)
                            mat.SetFloat(v.Name, v.FloatVal);
                            break;
                    }
                }
            }

            TextureProp[] textures = TextureProps;
            if (textures != null)
            {
                for (int i = 0; i < textures.Length; i++)
                {
                    TextureProp t = textures[i];
                    // Parity: MuParser always sets scale/offset (in ReadMaterialTexture); the texture
                    // itself is resolved later in ReadTextures via GameDatabase.GetTexture(url, isNormal).
                    mat.SetTextureScale(t.Name, t.Scale);
                    mat.SetTextureOffset(t.Name, t.Offset);
                    if (t.Url != null)
                    {
                        // Parity: reproduce MuParser.ReadTextures' skip-and-log on a missing texture — if the
                        // resolved texture IsNullOrDestroyed, log the error and do NOT call SetTexture.
                        Texture2D tex = GameDatabase.Instance.GetTexture(t.Url, t.IsNormalMap);
                        if (tex.IsNullOrDestroyed())
                            Debug.LogError($"Texture '{t.Url}' not found!");
                        else
                            mat.SetTexture(t.Name, tex);
                    }
                }
            }

            locals[Dst] = mat;
        }
    }

    /// <summary>Reproduces the sharedMaterial/emitter-material fan-out of <c>MuParser.ReadMaterials</c>
    /// (and the emitter registration in <c>ReadParticles</c>) for a single material index.</summary>
    internal sealed class AssignMaterial : IModelInstruction
    {
        public int MaterialSlot;
        public int[] RendererSlots;
        public int[] EmitterSlots;

        public void Execute(UnityEngine.Object[] locals)
        {
            Material mat = (Material)locals[MaterialSlot];
            // Parity: MuParser uses the SINGULAR Renderer.sharedMaterial (not sharedMaterials[]), setting
            // it once per material index. For a multi-material renderer the LAST material index that
            // lists it wins; the compiler emits these instructions in material-index order to preserve
            // that "last wins" outcome.
            int[] renderers = RendererSlots;
            if (renderers != null)
                for (int i = 0; i < renderers.Length; i++)
                    ((Renderer)locals[renderers[i]]).sharedMaterial = mat;

            int[] emitters = EmitterSlots;
            if (emitters != null)
                for (int i = 0; i < emitters.Length; i++)
                    ((KSPParticleEmitter)locals[emitters[i]]).material = mat;
        }
    }

    // ---- Colliders ------------------------------------------------------------------------------

    /// <summary>Reproduces <c>MuParser.ReadMeshCollider</c> (case 3) / <c>ReadMeshCollider2</c> (case 25).
    /// The isTrigger flag exists only in the "2" variant (baked as <see cref="HasTrigger"/>).</summary>
    internal sealed class AddMeshCollider : IModelInstruction
    {
        public int Go;
        public bool HasTrigger;
        public bool IsTrigger;
        public int MeshSlot;

        public void Execute(UnityEngine.Object[] locals)
        {
            MeshCollider mc = ((GameObject)locals[Go]).AddComponent<MeshCollider>();
            mc.convex = true; // Parity: MuParser always forces convex (the file's "convex" bool is ignored).
            if (HasTrigger)
                mc.isTrigger = IsTrigger;
            mc.sharedMesh = (Mesh)locals[MeshSlot];
        }
    }

    /// <summary>Reproduces <c>MuParser.ReadSphereCollider</c> (case 4) / <c>ReadSphereCollider2</c> (case 26).</summary>
    internal sealed class AddSphereCollider : IModelInstruction
    {
        public int Go;
        public bool HasTrigger;
        public bool IsTrigger;
        public float Radius;
        public Vector3 Center;

        public void Execute(UnityEngine.Object[] locals)
        {
            SphereCollider sc = ((GameObject)locals[Go]).AddComponent<SphereCollider>();
            if (HasTrigger)
                sc.isTrigger = IsTrigger;
            sc.radius = Radius;
            sc.center = Center;
        }
    }

    /// <summary>Reproduces <c>MuParser.ReadCapsuleCollider</c> (case 5) / <c>ReadCapsuleCollider2</c>
    /// (case 27). Height exists only in the "2" variant (baked as <see cref="HasHeight"/>).</summary>
    internal sealed class AddCapsuleCollider : IModelInstruction
    {
        public int Go;
        public bool HasTrigger;
        public bool IsTrigger;
        public float Radius;
        public bool HasHeight;
        public float Height;
        public int Direction;
        public Vector3 Center;

        public void Execute(UnityEngine.Object[] locals)
        {
            CapsuleCollider cc = ((GameObject)locals[Go]).AddComponent<CapsuleCollider>();
            if (HasTrigger)
                cc.isTrigger = IsTrigger;
            cc.radius = Radius;
            if (HasHeight)
                cc.height = Height;
            // Parity: Direction is the raw axis index Unity uses (0 = X, 1 = Y, 2 = Z), copied verbatim.
            cc.direction = Direction;
            cc.center = Center;
        }
    }

    /// <summary>Reproduces <c>MuParser.ReadBoxCollider</c> (case 6) / <c>ReadBoxCollider2</c> (case 28).</summary>
    internal sealed class AddBoxCollider : IModelInstruction
    {
        public int Go;
        public bool HasTrigger;
        public bool IsTrigger;
        public Vector3 Size;
        public Vector3 Center;

        public void Execute(UnityEngine.Object[] locals)
        {
            BoxCollider bc = ((GameObject)locals[Go]).AddComponent<BoxCollider>();
            if (HasTrigger)
                bc.isTrigger = IsTrigger;
            bc.size = Size;
            bc.center = Center;
        }
    }

    /// <summary>Reproduces <c>MuParser.ReadWheelCollider</c> (case 29), including the JointSpring and the
    /// two WheelFrictionCurves, and the final <c>enabled = false</c>.</summary>
    internal sealed class AddWheelCollider : IModelInstruction
    {
        public int Go;
        public float Mass;
        public float Radius;
        public float SuspensionDistance;
        public Vector3 Center;
        public float SpringSpring;
        public float SpringDamper;
        public float SpringTarget;
        // Parity: friction curve fields in MuParser's read order:
        // [0]=extremumSlip [1]=extremumValue [2]=asymptoteSlip [3]=asymptoteValue [4]=stiffness.
        public float[] Forward;
        public float[] Sideways;

        public void Execute(UnityEngine.Object[] locals)
        {
            WheelCollider wc = ((GameObject)locals[Go]).AddComponent<WheelCollider>();
            wc.mass = Mass;
            wc.radius = Radius;
            wc.suspensionDistance = SuspensionDistance;
            wc.center = Center;
            wc.suspensionSpring = new JointSpring
            {
                spring = SpringSpring,
                damper = SpringDamper,
                targetPosition = SpringTarget
            };
            wc.forwardFriction = new WheelFrictionCurve
            {
                extremumSlip = Forward[0],
                extremumValue = Forward[1],
                asymptoteSlip = Forward[2],
                asymptoteValue = Forward[3],
                stiffness = Forward[4]
            };
            wc.sidewaysFriction = new WheelFrictionCurve
            {
                extremumSlip = Sideways[0],
                extremumValue = Sideways[1],
                asymptoteSlip = Sideways[2],
                asymptoteValue = Sideways[3],
                stiffness = Sideways[4]
            };
            wc.enabled = false; // Parity: MuParser leaves wheel colliders disabled.
        }
    }

    // ---- Light / camera -------------------------------------------------------------------------

    /// <summary>Reproduces <c>MuParser.ReadLight</c> (case 23). The spot angle exists only for
    /// <c>version &gt; 1</c> (baked as <see cref="HasSpotAngle"/>).</summary>
    internal sealed class AddLight : IModelInstruction
    {
        public int Go;
        public int Type;
        public float Intensity;
        public float Range;
        public Color Color;
        public int CullingMask;
        public bool HasSpotAngle;
        public float SpotAngle;

        public void Execute(UnityEngine.Object[] locals)
        {
            Light light = ((GameObject)locals[Go]).AddComponent<Light>();
            light.type = (LightType)Type;
            light.intensity = Intensity;
            light.range = Range;
            light.color = Color;
            light.cullingMask = CullingMask;
            if (HasSpotAngle)
                light.spotAngle = SpotAngle;
        }
    }

    /// <summary>Reproduces <c>MuParser.ReadCamera</c> (case 30), including the final
    /// <c>allowHDR = false; enabled = false</c>.</summary>
    internal sealed class AddCamera : IModelInstruction
    {
        public int Go;
        public int ClearFlags;
        public Color BackgroundColor;
        public int CullingMask;
        public bool Orthographic;
        public float FieldOfView;
        public float NearClip;
        public float FarClip;
        public float Depth;

        public void Execute(UnityEngine.Object[] locals)
        {
            Camera camera = ((GameObject)locals[Go]).AddComponent<Camera>();
            camera.clearFlags = (CameraClearFlags)ClearFlags;
            camera.backgroundColor = BackgroundColor;
            camera.cullingMask = CullingMask;
            camera.orthographic = Orthographic;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = NearClip;
            camera.farClipPlane = FarClip;
            camera.depth = Depth;
            camera.allowHDR = false; // Parity: MuParser force-disables HDR and the camera itself.
            camera.enabled = false;
        }
    }

    // ---- Particles ------------------------------------------------------------------------------

    /// <summary>Reproduces <c>MuParser.ReadParticles</c> (case 31): copies every KSPParticleEmitter field
    /// and maps the raw render-mode code. Material assignment is a separate <see cref="AssignMaterial"/>
    /// step (the emitter is exposed via <see cref="Dst"/>).</summary>
    internal sealed class AddParticleEmitter : IModelInstruction
    {
        public int Go;
        public int Dst;
        public ParticleEmitterData Data;

        public void Execute(UnityEngine.Object[] locals)
        {
            KSPParticleEmitter e = ((GameObject)locals[Go]).AddComponent<KSPParticleEmitter>();
            ParticleEmitterData d = Data;
            e.emit = d.Emit;
            e.shape = (KSPParticleEmitter.EmissionShape)d.Shape;
            // MuParser sets shape3D/shape2D component-wise; assigning the whole vector is equivalent.
            e.shape3D = d.Shape3D;
            e.shape2D = d.Shape2D;
            e.shape1D = d.Shape1D;
            e.color = d.Color;
            e.useWorldSpace = d.UseWorldSpace;
            e.minSize = d.MinSize;
            e.maxSize = d.MaxSize;
            e.minEnergy = d.MinEnergy;
            e.maxEnergy = d.MaxEnergy;
            e.minEmission = d.MinEmission;
            e.maxEmission = d.MaxEmission;
            e.worldVelocity = d.WorldVelocity;
            e.localVelocity = d.LocalVelocity;
            e.rndVelocity = d.RndVelocity;
            e.emitterVelocityScale = d.EmitterVelocityScale;
            e.angularVelocity = d.AngularVelocity;
            e.rndAngularVelocity = d.RndAngularVelocity;
            e.rndRotation = d.RndRotation;
            e.doesAnimateColor = d.DoesAnimateColor;
            // Parity: MuParser assigns a fresh Color[5]; copy so the emitter never aliases baked data.
            Color[] colorAnimation = new Color[5];
            Color[] src = d.ColorAnimation;
            for (int i = 0; i < 5; i++)
                colorAnimation[i] = src[i];
            e.colorAnimation = colorAnimation;
            e.worldRotationAxis = d.WorldRotationAxis;
            e.localRotationAxis = d.LocalRotationAxis;
            e.sizeGrow = d.SizeGrow;
            e.rndForce = d.RndForce;
            e.force = d.Force;
            e.damping = d.Damping;
            e.castShadows = d.CastShadows;
            e.recieveShadows = d.RecieveShadows; // [sic] KSPParticleEmitter's own misspelled field name.
            e.lengthScale = d.LengthScale;
            e.velocityScale = d.VelocityScale;
            e.maxParticleSize = d.MaxParticleSize;
            // Parity: MuParser's render-mode switch — default => Billboard; 3/4/5 => the explicit modes.
            switch (d.RenderModeCode)
            {
                default:
                    e.particleRenderMode = ParticleSystemRenderMode.Billboard;
                    break;
                case 3:
                    e.particleRenderMode = ParticleSystemRenderMode.Stretch;
                    break;
                case 4:
                    e.particleRenderMode = ParticleSystemRenderMode.HorizontalBillboard;
                    break;
                case 5:
                    e.particleRenderMode = ParticleSystemRenderMode.VerticalBillboard;
                    break;
            }
            e.uvAnimationXTile = d.UvAnimationXTile;
            e.uvAnimationYTile = d.UvAnimationYTile;
            e.uvAnimationCycles = d.UvAnimationCycles;
            locals[Dst] = e;
        }
    }

    // ---- Animation ------------------------------------------------------------------------------

    /// <summary>Reproduces <c>MuParser.ReadAnimation</c> (case 2): rebuilds legacy <c>Animation</c> /
    /// <c>AnimationClip</c> / <c>AnimationCurve</c> / <c>Keyframe</c> objects and replays the exact
    /// <c>isInvalid</c> / null-skip logic and curve-type mapping.</summary>
    internal sealed class AddAnimation : IModelInstruction
    {
        public int Go;
        public AnimationClipData[] Clips;
        public string DefaultClip;
        public bool PlayAutomatically;

        public void Execute(UnityEngine.Object[] locals)
        {
            Animation animation = ((GameObject)locals[Go]).AddComponent<Animation>();

            // Parity: isInvalid is declared ONCE outside the clip loop, so a single invalid curve poisons
            // every later clip (AddClip is skipped) and the default clip. This is faithful to MuParser,
            // not a bug to "fix".
            bool isInvalid = false;

            AnimationClipData[] clips = Clips ?? Array.Empty<AnimationClipData>();
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClipData clip = clips[i];
                AnimationClip animationClip = new AnimationClip();
                animationClip.legacy = true;
                animationClip.localBounds = new Bounds(clip.BoundsCenter, clip.BoundsSize);
                animationClip.wrapMode = (WrapMode)clip.WrapMode;

                AnimationCurveData[] curves = clip.Curves ?? Array.Empty<AnimationCurveData>();
                for (int j = 0; j < curves.Length; j++)
                {
                    AnimationCurveData curve = curves[j];
                    // Parity: curve type code 0-3 => Transform/Material/Light/AudioSource; anything else
                    // leaves curveType null, which trips the isInvalid guard below.
                    Type curveType = null;
                    switch (curve.TypeCode)
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

                    KeyframeData[] keys = curve.Keys ?? Array.Empty<KeyframeData>();
                    Keyframe[] keyFrames = new Keyframe[keys.Length];
                    for (int k = 0; k < keys.Length; k++)
                        keyFrames[k] = new Keyframe(keys[k].Time, keys[k].Value, keys[k].InTangent, keys[k].OutTangent);

                    AnimationCurve animationCurve = new AnimationCurve(keyFrames);
                    animationCurve.preWrapMode = (WrapMode)curve.PreWrap;
                    animationCurve.postWrapMode = (WrapMode)curve.PostWrap;

                    if (clip.Name == null || curve.Path == null || curveType == null || curve.Property == null)
                    {
                        isInvalid = true;
                        Debug.LogWarning($"{clip.Name ?? "Null clipName"} : {curve.Path ?? "Null curvePath"}, {(curveType == null ? "Null curveType" : curveType.ToString())}, {curve.Property ?? "Null curveProperty"}");
                        continue;
                    }

                    animationClip.SetCurve(curve.Path, curveType, curve.Property, animationCurve);
                }

                if (!isInvalid)
                    animation.AddClip(animationClip, clip.Name);
            }

            // Parity contract: the compiler must bake DefaultClip as string.Empty (never null) when absent,
            // because MuParser derives it from ReadString(), which returns string.Empty for a zero-length
            // string — a baked null would wrongly pass this guard.
            if (DefaultClip != string.Empty && !isInvalid)
                animation.clip = animation.GetClip(DefaultClip);

            animation.playAutomatically = PlayAutomatically;
        }
    }

    // ---- Bones ----------------------------------------------------------------------------------

    /// <summary>Reproduces <c>MuParser.AffectSkinnedMeshRenderersBones</c> for one skinned mesh renderer:
    /// resolves each bone by name from the model root and assigns the bone array. Deferred/not emitted in
    /// v1, but faithful.</summary>
    internal sealed class ResolveBones : IModelInstruction
    {
        public int SmrSlot;
        public int RootSlot;
        public string[] BoneNames;

        public void Execute(UnityEngine.Object[] locals)
        {
            Transform root = ((GameObject)locals[RootSlot]).transform;
            Transform[] bones = new Transform[BoneNames.Length];
            for (int i = 0; i < BoneNames.Length; i++)
                bones[i] = FindChildByName(root, BoneNames[i]);
            ((SkinnedMeshRenderer)locals[SmrSlot]).bones = bones;
        }

        // Ported verbatim from MuParser.FindChildByName: depth-first search returning the first transform
        // whose name matches (including the root itself).
        private static Transform FindChildByName(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            foreach (Transform item in parent)
            {
                Transform transform = FindChildByName(item, name);
                if (transform != null)
                    return transform;
            }
            return null;
        }
    }

    // ---- Payload structs ------------------------------------------------------------------------

    /// <summary>How a KSP shader is resolved at replay. The v&lt;4 path resolves a
    /// <see cref="PartToolsLib.ShaderType"/> by type; the v&gt;=4 path resolves a raw shader name.</summary>
    /// <remarks>
    /// <see cref="ShaderHelpers.GetShader(string)"/> is byte-for-byte equivalent to MuParser's
    /// <c>Shader.Find(name)</c> — it caches the same instances and, critically, returns <c>null</c> for
    /// an unknown name (it never substitutes a default). So resolving the name route through
    /// <see cref="ShaderHelpers"/> preserves MuParser's v&gt;=4 null-shader fallback
    /// (<c>new Material((Shader)null)</c>). The type route uses
    /// <see cref="ShaderHelpers.GetShader(ShaderType)"/>, whose <c>default</c> maps to KSP/Diffuse, which
    /// is exactly what MuParser's v&lt;4 path does.
    /// </remarks>
    internal struct ShaderRef
    {
        public bool ByName;
        public ShaderType Type;
        public string Name;

        public Shader Resolve() => ByName ? ShaderHelpers.GetShader(Name) : ShaderHelpers.GetShader(Type);
    }

    /// <summary>One scalar/color/vector material property. <see cref="Kind"/>: 0 Color, 1 Vector, 2 Float
    /// (MuParser material property type codes 2 and 3 both map to Float).</summary>
    internal struct ValueProp
    {
        public const int KindColor = 0;
        public const int KindVector = 1;
        public const int KindFloat = 2;

        public string Name;
        public int Kind;
        public Color ColorVal;
        public Vector4 VecVal;
        public float FloatVal;
    }

    /// <summary>One material texture property. Scale/offset are always applied; <see cref="Url"/> non-null
    /// additionally binds a texture via <c>GameDatabase.GetTexture(Url, IsNormalMap)</c>.</summary>
    internal struct TextureProp
    {
        public string Name;
        public string Url;
        public bool IsNormalMap;
        public Vector2 Scale;
        public Vector2 Offset;
    }

    /// <summary>Every field <c>MuParser.ReadParticles</c> reads for a <c>KSPParticleEmitter</c>.
    /// <see cref="RenderModeCode"/> is the raw int mapped by <see cref="AddParticleEmitter"/>;
    /// <see cref="ColorAnimation"/> is a 5-element array.</summary>
    internal struct ParticleEmitterData
    {
        public bool Emit;
        public int Shape;
        public Vector3 Shape3D;
        public Vector2 Shape2D;
        public float Shape1D;
        public Color Color;
        public bool UseWorldSpace;
        public float MinSize;
        public float MaxSize;
        public float MinEnergy;
        public float MaxEnergy;
        public int MinEmission;
        public int MaxEmission;
        public Vector3 WorldVelocity;
        public Vector3 LocalVelocity;
        public Vector3 RndVelocity;
        public float EmitterVelocityScale;
        public float AngularVelocity;
        public float RndAngularVelocity;
        public bool RndRotation;
        public bool DoesAnimateColor;
        public Color[] ColorAnimation;
        public Vector3 WorldRotationAxis;
        public Vector3 LocalRotationAxis;
        public float SizeGrow;
        public Vector3 RndForce;
        public Vector3 Force;
        public float Damping;
        public bool CastShadows;
        public bool RecieveShadows;
        public float LengthScale;
        public float VelocityScale;
        public float MaxParticleSize;
        public int RenderModeCode;
        public int UvAnimationXTile;
        public int UvAnimationYTile;
        public int UvAnimationCycles;
    }

    /// <summary>One legacy <c>AnimationClip</c>: <c>localBounds = new Bounds(BoundsCenter, BoundsSize)</c>,
    /// <c>wrapMode = (WrapMode)WrapMode</c>, plus its curves.</summary>
    internal struct AnimationClipData
    {
        public string Name;
        public Vector3 BoundsCenter;
        public Vector3 BoundsSize;
        public int WrapMode;
        public AnimationCurveData[] Curves;
    }

    /// <summary>One animation curve. <see cref="TypeCode"/>: 0 Transform, 1 Material, 2 Light,
    /// 3 AudioSource (anything else marks the animation invalid, matching MuParser).</summary>
    internal struct AnimationCurveData
    {
        public string Path;
        public string Property;
        public int TypeCode;
        public int PreWrap;
        public int PostWrap;
        public KeyframeData[] Keys;
    }

    /// <summary>One keyframe (four floats, matching MuParser's <c>ReadKeyFrame</c>; weights left default).</summary>
    internal struct KeyframeData
    {
        public float Time;
        public float Value;
        public float InTangent;
        public float OutTangent;
    }
}
