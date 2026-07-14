using System;
using System.Collections.Generic;
using System.IO;
using PartToolsLib;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// Thread-safe, instance-state port of <see cref="KSPCommunityFixes.Library.MuParser"/> that walks a
    /// <c>.mu</c> file's opcode tree and, instead of building <c>UnityEngine.Object</c>s on the main
    /// thread, EMITS a flat <see cref="IModelInstruction"/> list plus serialization-ready
    /// <see cref="MeshBlob"/>s into a <see cref="CompiledModel"/>. Everything here is off-main-thread work
    /// (no <c>UnityEngine.Object</c> is ever created), so many worker threads can compile different files
    /// in parallel — hence every accumulator is an instance field, never a static.
    /// <para>
    /// The opcode dispatch, read order and version-gating are a faithful reproduction of MuParser's
    /// <c>ReadChild</c> switch and its readers; the non-obvious parity points (two-pass material/texture
    /// deferral, slot allocation policy, the skinned-mesh seam, the <c>texCount</c> guard and the
    /// last-wins material ordering) are commented inline where they occur.
    /// </para>
    /// <para>
    /// A single instance may be reused across many files on one worker thread: <see cref="Compile"/>
    /// resets all instance state at its top, so no explicit disposal or re-instantiation is required.
    /// </para>
    /// </summary>
    internal sealed unsafe class MuModelCompiler
    {
        // ---- Per-Compile instance state (reset at the top of Compile) ------------------------------

        // The single mutable reader. Per MuBinaryReader's contract it is held in exactly ONE
        // non-readonly field and mutated in place; every read helper below is an instance method that
        // advances this.reader, so the cursor never desyncs through a defensive struct copy.
        private MuBinaryReader reader;

        private string fileUrl;
        private string directoryUrl;
        private int version;

        // Running slot counter. Slots are indices into the driver's locals[] array; the high-water mark
        // (== this value at the end) becomes CompiledModel.LocalCount.
        private int nextSlot;

        // Global mesh index over the whole file, used to build a globally-unique canonical mesh name.
        private int meshIndex;

        private readonly List<IModelInstruction> instructions = new List<IModelInstruction>();
        private readonly List<MeshBlob> blobs = new List<MeshBlob>();
        private readonly List<MeshBinding> bindings = new List<MeshBinding>();

        // matRefs[i] holds the renderer/emitter slots that reference material index i, mirroring
        // MuParser's matDummies[i].renderers / .particleEmitters. Populated while walking (when an
        // AddMeshRenderer/AddSkinnedMeshRenderer/AddParticleEmitter is emitted); consumed at finalize.
        private readonly List<MatRef> matRefs = new List<MatRef>();

        // Pending materials in material-index order (0..materialCount-1), parsed by ReadMaterials but
        // NOT emitted until finalize (their texture urls aren't known until the Textures block).
        private readonly List<PendingMaterial> pendingMaterials = new List<PendingMaterial>();

        // Texture-slot table: textureSlots[slot] = every PendingTexture that references that texture
        // slot. Mirrors MuParser's PartReader.TextureDummyList — grown so that Count == highest
        // referenced slot + 1, which is what the texCount guard compares against.
        private readonly List<List<PendingTexture>> textureSlots = new List<List<PendingTexture>>();

        // Skinned renderers awaiting a ResolveBones step (mirrors MuParser's boneDummies).
        private readonly List<SkinnedEntry> skinnedRenderers = new List<SkinnedEntry>();

        // Bone names for the SkinnedMeshRenderer currently being parsed. ReadSkinnedMeshRenderer sets
        // this to the SMR's bone-name list immediately BEFORE it calls ReadMesh (which reads the matching
        // bind poses) and clears it (null) right AFTER, so ReadMesh can attach the index-aligned bone-name
        // list to the skinned mesh blob it builds. Null whenever a mesh is read outside a skinned renderer
        // (MeshFilter / collider meshes), which never carry bind poses.
        private string[] currentBoneNames;

        // Diagnostics buffered during compilation. Compile runs on background worker threads (via a
        // parallel PLINQ query over all models), and KSP's ILogHandler plus the mod handlers chained onto
        // Application.logMessageReceived are NOT thread-safe, so nothing here may log off-thread. Instead
        // every diagnostic is appended to this list and handed to the returned CompiledModel, whose
        // FlushLogs() emits them on the MAIN thread during replay.
        private List<DeferredLog> logs;

        // Single cached sink passed to MeshBlobBuilder.FromArrays so its attribute-length warnings are
        // buffered rather than logged off-thread. It closes over the logs FIELD (not a captured list), so
        // after ResetState swaps in a fresh list it always targets the current one — no per-mesh alloc.
        private readonly Action<string> warnSink;

        public MuModelCompiler()
        {
            warnSink = msg => logs.Add(new DeferredLog(LogType.Warning, msg));
        }

        /// <summary>
        /// Compile one <c>.mu</c> file into a main-thread-ready <see cref="CompiledModel"/>. Never throws:
        /// on any parse error a <see cref="CompiledModel"/> with <see cref="CompiledModel.Failed"/> set is
        /// returned so a PLINQ <c>Select</c> over many files can't fault.
        /// </summary>
        /// <param name="fileUrl">The model's <c>UrlFile.url</c>. Used for globally-unique mesh names and
        /// <see cref="CompiledModel.SourceUrl"/> (the equivalent of MuParser's root object name).</param>
        /// <param name="directoryUrl">The model's <c>file.parent.url</c>. Used to build texture urls,
        /// exactly MuParser's <c>modelDirectoryUrl</c> parameter.</param>
        /// <param name="data">Raw model bytes. Must be pinnable (a plain managed array).</param>
        /// <param name="dataLength">Valid byte count in <paramref name="data"/>; if &lt;= 0 the whole
        /// array length is used (matching <c>MuParser.Parse</c>).</param>
        public CompiledModel Compile(string fileUrl, string directoryUrl, byte[] data, int dataLength)
        {
            ResetState();
            this.fileUrl = fileUrl;
            this.directoryUrl = directoryUrl;

            try
            {
                int length = dataLength <= 0 ? data.Length : dataLength;

                // Pin for the whole walk: MuBinaryReader holds a raw byte*, so the buffer must stay
                // pinned for the entire parse. A single fixed block around the walk matches MuParser's
                // GCHandle-pinned scope; nothing reads the stream after this block closes.
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

                // Two-pass finalize (see FinalizeMaterials): materials + textures are known now, so emit
                // CreateMaterial/AssignMaterial in ascending material-index order, then the deferred bone
                // resolution (MuParser runs AffectSkinnedMeshRenderersBones last).
                FinalizeMaterials();
                FinalizeBones();

                return new CompiledModel
                {
                    SourceUrl = fileUrl,
                    Instructions = instructions.ToArray(),
                    Blobs = blobs.ToArray(),
                    Bindings = bindings.ToArray(),
                    LocalCount = nextSlot,
                    // Skinned meshes are now fully serialized (blend channels + bind pose + bone
                    // metadata) and flow through the background bundle path like any static model, so
                    // this is never set — the FastLoader skinned/MuParser fallback is no longer taken.
                    ContainsSkinnedMesh = false,
                    Failed = false,
                    Logs = logs, // flushed on the main thread by the replay pipeline (see FlushLogs)
                };
            }
            catch (Exception e)
            {
                // SourceUrl is set first so the pipeline can log which file failed. Compile never throws.
                // A Failed model still carries any diagnostics buffered before the fault so they aren't lost.
                return new CompiledModel
                {
                    SourceUrl = fileUrl,
                    Failed = true,
                    FailureMessage = e.GetType().Name + ": " + e.Message,
                    Logs = logs,
                };
            }
        }

        private void ResetState()
        {
            // Fresh reader is assigned inside the fixed block; just clear the accumulators here so a
            // worker thread can reuse one instance across many files.
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
            // Fresh list per Compile so a returned CompiledModel keeps its own buffered diagnostics even
            // after this instance is reused for the next file. warnSink closes over this field, so it
            // automatically targets the new list.
            logs = new List<DeferredLog>();
        }

        // ---- Core tree walk ------------------------------------------------------------------------

        /// <summary>
        /// Mirror of <c>MuParser.ReadChild</c>: reads the transform header, allocates a GameObject slot,
        /// emits <see cref="CreateGameObject"/>, then dispatches the child opcode stream until
        /// <c>ChildTransformEnd</c> (or end of data). Returns the allocated slot.
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

            // No default case: MuParser ignores unknown opcodes (they consume nothing and the loop reads
            // the next int). The switched EntryType values are numerically identical to MuParser's raw
            // int cases.
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
                        ReadMeshCollider2(mySlot);
                        break;
                    case EntryType.SphereCollider2: // 26
                        ReadSphereCollider2(mySlot);
                        break;
                    case EntryType.CapsuleCollider2: // 27
                        ReadCapsuleCollider2(mySlot);
                        break;
                    case EntryType.BoxCollider2: // 28
                        ReadBoxCollider2(mySlot);
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

        /// <summary>Mirror of <c>MuParser.ReadAnimation</c>: bakes clip/curve/keyframe data verbatim
        /// (the <c>isInvalid</c> / null-skip logic and curve-type mapping run at replay in
        /// <see cref="AddAnimation"/>).</summary>
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
                        // reader.ReadKeyFrame consumes MuParser's 20-byte record (4 floats + 4 pad) and
                        // returns a plain Keyframe struct (safe off-thread); we bake its four floats.
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

            // ReadString returns string.Empty (never null) for an empty string; baked verbatim so the
            // AddAnimation "DefaultClip != string.Empty" guard behaves exactly like MuParser.
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

        /// <summary>Mirror of <c>MuParser.ReadMeshCollider</c> (opcode 3): the file's "convex" bool is
        /// read and discarded (always forced convex at replay), then the mesh.</summary>
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

        /// <summary>Mirror of <c>MuParser.ReadMeshCollider2</c> (opcode 25): isTrigger, then the discarded
        /// "convex" bool, then the mesh.</summary>
        private void ReadMeshCollider2(int goSlot)
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

        /// <summary>Mirror of <c>MuParser.ReadSphereCollider</c> (opcode 4).</summary>
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

        /// <summary>Mirror of <c>MuParser.ReadSphereCollider2</c> (opcode 26).</summary>
        private void ReadSphereCollider2(int goSlot)
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

        /// <summary>Mirror of <c>MuParser.ReadCapsuleCollider</c> (opcode 5): radius, direction, center.</summary>
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

        /// <summary>Mirror of <c>MuParser.ReadCapsuleCollider2</c> (opcode 27): isTrigger, radius, height,
        /// direction, center.</summary>
        private void ReadCapsuleCollider2(int goSlot)
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

        /// <summary>Mirror of <c>MuParser.ReadBoxCollider</c> (opcode 6).</summary>
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

        /// <summary>Mirror of <c>MuParser.ReadBoxCollider2</c> (opcode 28).</summary>
        private void ReadBoxCollider2(int goSlot)
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

        /// <summary>Mirror of <c>MuParser.ReadWheelCollider</c> (opcode 29): mass/radius/suspension, the
        /// JointSpring triplet, then the two 5-float friction curves (in MuParser's read order).</summary>
        private void ReadWheelCollider(int goSlot)
        {
            float mass = reader.ReadFloat();
            float radius = reader.ReadFloat();
            float suspensionDistance = reader.ReadFloat();
            Vector3 center = reader.ReadVector3();
            float springSpring = reader.ReadFloat();
            float springDamper = reader.ReadFloat();
            float springTarget = reader.ReadFloat();

            // Friction curve fields in MuParser's exact read order:
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

        /// <summary>Mirror of <c>MuParser.ReadMeshFilter</c> (opcode 7).</summary>
        private void ReadMeshFilter(int goSlot)
        {
            int meshSlot = ReadMesh();
            instructions.Add(new AddMeshFilter { Go = goSlot, MeshSlot = meshSlot });
        }

        /// <summary>Mirror of <c>MuParser.ReadMeshRenderer</c> (opcode 8): shadow flags exist only for
        /// version &gt;= 1; a renderer registers itself under every material index it lists (last index
        /// wins at replay, preserved by ascending-index AssignMaterial emission).</summary>
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

        /// <summary>Mirror of <c>MuParser.ReadSkinnedMeshRenderer</c> (opcode 9): reads the material
        /// fan-out, local bounds / quality / updateWhenOffscreen, the bone NAMES and the mesh; emits an
        /// <see cref="AddSkinnedMeshRenderer"/> and records a deferred <see cref="ResolveBones"/> step. The
        /// bone names are threaded into <see cref="ReadMesh"/> (via <see cref="currentBoneNames"/>) so the
        /// skinned mesh blob carries them index-aligned with its bind poses; the SAME list also drives the
        /// bone binding in <see cref="ResolveBones"/>, so hashes and binding stay consistent.</summary>
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

            // Hand the SMR's bone names to ReadMesh so the skinned mesh blob it builds attaches them
            // index-aligned with the bind poses it reads (BoneNames[i] <-> BindPoses[i]). MuParser reads
            // this SMR's mesh immediately after its bone names, so the pairing matches the oracle. Cleared
            // right after so a later non-skinned ReadMesh can't pick up stale names.
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

        /// <summary>Mirror of <c>MuParser.ReadMaterials</c> (opcode 10): reads each material into a pending
        /// descriptor (deferred emission, since texture urls aren't known yet) and allocates its slot in
        /// ascending index order.</summary>
        private void ReadMaterials()
        {
            // Assumes exactly ONE Materials block per .mu (as all real/stock files have, matching MuParser and
            // stock PartReader): a hypothetical second Materials block would append into pendingMaterials and
            // shift the second block's material indices, diverging from the oracle's per-block re-indexing. This
            // is a documented latent structural assumption, not a bug to fix.
            int materialCount = reader.ReadInt();
            for (int i = 0; i < materialCount; i++)
            {
                PendingMaterial pending = version < 4 ? ReadMaterial() : ReadMaterial4();
                pending.Slot = nextSlot++;
                pendingMaterials.Add(pending); // index i == material index i
            }
        }

        /// <summary>Mirror of <c>MuParser.ReadMaterial</c> (version &lt; 4): shader is resolved by
        /// <see cref="ShaderType"/> at replay; the per-ShaderType read order and property NAMES exactly
        /// match MuParser's int-property-id setters (each id is <c>Shader.PropertyToID(name)</c>).</summary>
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
                default: // Custom (0), Diffuse (1) and any unknown type: MuParser's default reads _MainTex
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

        /// <summary>Mirror of <c>MuParser.ReadMaterial4</c> (version &gt;= 4): shader is resolved by name at
        /// replay; property type codes 0=Color, 1=Vector, 2 &amp; 3=Float, 4=texture. An unknown type code
        /// reads nothing after the property name (matching MuParser's switch with no default).</summary>
        private PendingMaterial ReadMaterial4()
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
                    // No default: an unknown type code consumes nothing beyond the name, like MuParser.
                }
            }

            return pm;
        }

        /// <summary>Mirror of <c>MuParser.ReadMaterialTexture</c>: reads the texture-slot index, scale and
        /// offset. Scale/offset are always baked; the url is resolved later (Textures block). A slot of
        /// -1 registers no dummy (MuParser's <c>AddTextureDummy</c> skips -1); otherwise the slot table is
        /// grown so its Count matches MuParser's <c>textureDummies.Count</c> for the texCount guard.</summary>
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
                // is fanned out to every registered PendingTexture in ReadTextures. (We keep a direct
                // reference to the PendingTexture rather than MuParser's dedup-by-material list; the dedup
                // only removed idempotent duplicate SetTexture calls, so the outcome is identical.)
                textureSlots[slotIndex].Add(pt);
            }
        }

        /// <summary>Mirror of <c>MuParser.ReadTextures</c> (opcode 12): resolves texture urls and fans them
        /// out to the pending textures. Preserves the <c>texCount != textureDummies.Count</c> guard — on
        /// mismatch it logs and returns, leaving every url null (scale/offset still applied).</summary>
        private void ReadTextures()
        {
            int texCount = reader.ReadInt();

            // texCount guard: on mismatch MuParser logs and returns immediately WITHOUT reading the
            // texture entries, so no url is ever assigned (every PendingTexture keeps Url == null). We
            // reproduce that exactly, including leaving the cursor right after texCount.
            if (texCount != textureSlots.Count)
            {
                logs.Add(new DeferredLog(LogType.Error, "TextureError: " + texCount + " " + textureSlots.Count));
                return;
            }

            for (int i = 0; i < texCount; i++)
            {
                string name = reader.ReadString();
                TextureType textureType = (TextureType)reader.ReadInt();

                // Url built exactly as MuParser: directoryUrl + "/" + filename-without-extension.
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

        /// <summary>Mirror of <c>MuParser.ReadLight</c> (opcode 23): spot angle exists only for version &gt; 1.</summary>
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

        /// <summary>Mirror of <c>MuParser.ReadTagAndLayer</c> (opcode 24). Configures the current
        /// GameObject slot inline (no new slot).</summary>
        private void ReadTagAndLayer(int goSlot)
        {
            string tag = reader.ReadString();
            int layer = reader.ReadInt();
            instructions.Add(new SetTagAndLayer { Go = goSlot, Tag = tag, Layer = layer });
        }

        /// <summary>Mirror of <c>MuParser.ReadCamera</c> (opcode 30).</summary>
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

        /// <summary>Mirror of <c>MuParser.ReadParticles</c> (opcode 31): reads every KSPParticleEmitter
        /// field in order (including the 5-element color animation and the raw render-mode int), allocates
        /// the emitter slot and registers it under its material index for the deferred fan-out.</summary>
        private void ReadParticles(int goSlot)
        {
            int emitterSlot = nextSlot++;

            var d = new ParticleEmitterData();
            d.Emit = reader.ReadBool();
            d.Shape = reader.ReadInt();
            // Component-wise reads: arguments evaluate left-to-right, matching MuParser's x, y, z order.
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
            var colorAnimation = new Color[5]; // MuParser always allocates exactly 5.
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

        /// <summary>Mirror of <c>MuParser.ReadMesh</c> (opcode MeshStart): reads the sub-blocks in file
        /// order into a <see cref="MeshBlobBuilder.Arrays"/>, builds a <see cref="MeshBlob"/>, records a
        /// <see cref="MeshBinding"/> and returns the allocated mesh slot. Bone weights / bind poses (when
        /// present) are stored into the arrays; together with the SMR bone names threaded in via
        /// <see cref="currentBoneNames"/> they let <see cref="MeshBlobBuilder.FromArrays"/> emit a fully
        /// skinned mesh (blend channels, bind pose and bone metadata).</summary>
        private int ReadMesh()
        {
            EntryType entryType = (EntryType)reader.ReadInt();
            if (entryType != EntryType.MeshStart)
            {
                // Corrupt stream: MuParser logs "Mesh Error" and returns a null mesh (the component then
                // gets a null sharedMesh) while the walk continues. We allocate a slot with NO binding so
                // locals[slot] stays null == null mesh, matching that behaviour without a replay crash.
                logs.Add(new DeferredLog(LogType.Error, "Mesh Error"));
                return nextSlot++;
            }

            int size = reader.ReadInt(); // vertex count for every per-vertex attribute
            reader.SkipInt();            // unknown field, skipped exactly as MuParser

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
                        // Skinned seam: one BoneWeight per vertex (four weights + four bone indices).
                        // Stored now (was discarded before skin support) so MeshBlobBuilder emits the
                        // BlendWeights (ch12) and BlendIndices (ch13) vertex channels. Same read order and
                        // count as the oracle, so cursor parity is preserved.
                        var boneWeights = new BoneWeight[size];
                        for (int i = 0; i < size; i++)
                            boneWeights[i] = reader.ReadBoneWeight();
                        arrays.BoneWeights = boneWeights;
                        break;
                    }
                    case EntryType.MeshBindPoses:
                    {
                        // One bind pose per bone; its length defines the bone count. Stored now (was
                        // discarded before skin support) so the blob carries m_BindPose and its bone
                        // metadata. Same read order and count as the oracle, so cursor parity is preserved.
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
        /// Emits the deferred material work. MuParser reads the Materials block BEFORE the Textures block,
        /// so texture urls aren't known when a material is first parsed; we accumulate pending materials +
        /// texture-slot references during the walk and resolve them here. For each material index in
        /// ASCENDING order we emit <see cref="CreateMaterial"/> then its <see cref="AssignMaterial"/> — that
        /// ordering preserves MuParser's LAST-WINS singular-sharedMaterial semantics (a renderer listed
        /// under several material indices ends up with the highest one).
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
                // gets empty fan-out lists — harmless, and it avoids reproducing a potential MuParser
                // IndexOutOfRange when the Materials count exceeds the referenced count.
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

        /// <summary>Mirror of <c>MuParser.AffectSkinnedMeshRenderersBones</c>, which <c>Parse</c> runs last:
        /// emits one <see cref="ResolveBones"/> per skinned renderer, resolving bone names from the model
        /// root (slot 0).</summary>
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
        /// Returns a bone-name array whose length equals the mesh's bind-pose count — the count
        /// <see cref="MeshBlobBuilder.FromArrays"/> requires to satisfy Unity's per-bone invariant
        /// (<c>m_BindPose</c> / <c>m_BoneNameHashes</c> / <c>m_BonesAABB</c> all equal). In the normal
        /// case the SkinnedMeshRenderer supplies exactly one bone name per bind pose and the same array is
        /// returned unchanged (no allocation). A few stock/mod <c>.mu</c> files export a
        /// <c>SkinnedMeshRenderer</c> whose bone-name list is shorter than (often empty relative to) the
        /// mesh's bind poses; MuParser tolerates this (it simply sets a shorter — possibly empty —
        /// <c>SkinnedMeshRenderer.bones</c>, so the mesh is not actually deformed), so rather than fail the
        /// whole model we pad the mesh's (cosmetic, leaf-name) hash source with empty names to keep the
        /// count invariant. Binding is unaffected: <see cref="FinalizeBones"/> still emits
        /// <see cref="ResolveBones"/> from the ORIGINAL SMR name list, so the runtime
        /// <c>SkinnedMeshRenderer.bones</c> stays byte-for-byte MuParser-equivalent — only the mesh's
        /// ignored hash values gain padding entries.
        /// </summary>
        private string[] ReconcileBoneNames(string[] smrBoneNames, int boneCount, string meshName)
        {
            int have = smrBoneNames?.Length ?? 0;
            if (have == boneCount)
                return smrBoneNames ?? Array.Empty<string>();

            var names = new string[boneCount];
            int copy = Math.Min(have, boneCount);
            for (int i = 0; i < copy; i++)
                names[i] = smrBoneNames[i];
            for (int i = copy; i < boneCount; i++)
                names[i] = string.Empty;

            logs.Add(new DeferredLog(LogType.Log,
                $"[MuModelCompiler] Model '{fileUrl}' mesh '{meshName}': SkinnedMeshRenderer declares " +
                $"{have} bone name(s) but the mesh has {boneCount} bind pose(s). This is an unusual but " +
                "valid .mu; reconciling by padding the mesh's cosmetic bone-name/hash list to preserve " +
                "Unity's per-bone count invariant. This is benign - bone binding is unaffected."));
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
            public readonly List<ValueProp> Values = new List<ValueProp>();
            public readonly List<PendingTexture> Textures = new List<PendingTexture>();
        }

        /// <summary>A material texture property whose url is resolved later. A mutable CLASS (not the
        /// <see cref="TextureProp"/> struct) so the texture-slot table can hold references to it and fill
        /// in the url when the Textures block is read.</summary>
        private sealed class PendingTexture
        {
            public string Name;
            public Vector2 Scale;
            public Vector2 Offset;
            public string Url;
            public bool IsNormalMap;
        }

        /// <summary>Renderer + emitter slots referencing one material index (mirrors MuParser's
        /// <c>MaterialDummy</c>).</summary>
        private sealed class MatRef
        {
            public readonly List<int> Renderers = new List<int>();
            public readonly List<int> Emitters = new List<int>();
        }

        /// <summary>A skinned renderer awaiting bone resolution (mirrors MuParser's <c>BonesDummy</c>).</summary>
        private struct SkinnedEntry
        {
            public int SmrSlot;
            public string[] BoneNames;
        }
    }
}
