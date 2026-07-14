using System;
using System.Text;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// Interleaves mesh attribute arrays into the single-stream <see cref="MeshBlob"/> vertex layout
    /// Unity 2019.4 expects, and packs the index buffer. Pure CPU work; safe on a background thread
    /// (no <c>UnityEngine.Mesh</c> is created). This is the mesh half of the model compiler; it is
    /// also reused by the diff harness and the single-mesh validation.
    /// </summary>
    /// <remarks>
    /// Confirmed against a real Unity 2019.4.18f1 mesh: <c>m_Channels</c> is a fixed 14-entry array
    /// (absent attributes have dimension 0), indexed by vertex attribute
    /// (0=Position, 1=Normal, 2=Tangent, 3=Color, 4..11=TexCoord0..7, 12=BlendWeights, 13=BlendIndices);
    /// present channels are all in stream 0, packed at contiguous offsets in attribute-index order.
    /// v1 stores every present attribute as float32 (format 0), including colour (from Color32/255),
    /// which matches the observed all-float32 convention with no format-code risk.
    /// </remarks>
    internal static class MeshBlobBuilder
    {
        public const int ChannelCount = 14;

        // Vertex attribute channel indices (Unity 2019.4 order).
        public const int ChPosition = 0;
        public const int ChNormal = 1;
        public const int ChTangent = 2;
        public const int ChColor = 3;
        public const int ChTexCoord0 = 4;
        public const int ChTexCoord1 = 5;
        public const int ChBlendWeights = 12;
        public const int ChBlendIndices = 13;

        const byte FormatFloat32 = 0;
        // Unity 2019.4 VertexChannelFormat.kChannelFormatUInt32 (format enum 10). BlendIndices use
        // this: the bone indices are unsigned 32-bit ints, NOT floats, NOT UNorm8/16.
        const byte FormatUInt32 = 10;

        /// <summary>Attribute arrays for one mesh. Null/empty arrays mark absent attributes.</summary>
        public struct Arrays
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector4[] Tangents;
            public Color32[] Colors;
            public Vector2[] Uv0;
            public Vector2[] Uv1;

            /// <summary>Per-submesh triangle index lists (already the final winding).</summary>
            public int[][] SubMeshTriangles;

            // ---- Skinning (all three present together, or the mesh is treated as static) --------
            // Populated by the model compiler and the offline mesh harness, not within this assembly,
            // so CS0649 ("never assigned") is expected here — it is not a defect.
#pragma warning disable CS0649
            /// <summary>Per-vertex bone weights (<c>weight0..3</c>) and indices (<c>boneIndex0..3</c>).</summary>
            public BoneWeight[] BoneWeights;

            /// <summary>One bind pose per bone (<c>m_BindPose</c>); its length defines the bone count.</summary>
            public Matrix4x4[] BindPoses;

            /// <summary>
            /// One name per bone, index-aligned with <see cref="BindPoses"/>, used to compute
            /// <c>m_BoneNameHashes</c>. To reproduce Unity's exact stored hash the name must be the
            /// bone's full transform path from the model root (e.g. <c>globalMove01/joints01/bn_spA01</c>);
            /// see <see cref="BoneNameHash"/>.
            /// </summary>
            public string[] BoneNames;
#pragma warning restore CS0649
        }

        /// <summary>Build a <see cref="MeshBlob"/> from a live <c>UnityEngine.Mesh</c> (main thread only).</summary>
        public static MeshBlob FromMesh(Mesh mesh, string name, Action<string> warn = null)
        {
            var arrays = new Arrays
            {
                Vertices = mesh.vertices,
                Normals = mesh.normals,
                Tangents = mesh.tangents,
                Colors = mesh.colors32,
                Uv0 = mesh.uv,
                Uv1 = mesh.uv2,
            };
            int subCount = mesh.subMeshCount;
            arrays.SubMeshTriangles = new int[subCount][];
            for (int i = 0; i < subCount; ++i)
                arrays.SubMeshTriangles[i] = mesh.GetTriangles(i);
            return FromArrays(name, in arrays, warn);
        }

        // <paramref name="warn"/> is an optional attribute-mismatch sink. When null (main-thread/offline
        // callers) warnings fall back to Debug.LogWarning; a worker-thread caller passes a sink that
        // buffers the message instead, since Debug.LogWarning is not safe to call off the main thread.
        public static unsafe MeshBlob FromArrays(string name, in Arrays a, Action<string> warn = null)
        {
            Vector3[] verts = a.Vertices ?? Array.Empty<Vector3>();
            int vertexCount = verts.Length;

            // A non-null attribute array whose length doesn't match the vertex count is dropped
            // (treated as absent) below; warn so a mismatched .mu doesn't silently lose an attribute.
            WarnIfWrongLength(name, "normals", Count(a.Normals), vertexCount, warn);
            WarnIfWrongLength(name, "tangents", Count(a.Tangents), vertexCount, warn);
            WarnIfWrongLength(name, "colors", Count(a.Colors), vertexCount, warn);
            WarnIfWrongLength(name, "uv0", Count(a.Uv0), vertexCount, warn);
            WarnIfWrongLength(name, "uv1", Count(a.Uv1), vertexCount, warn);

            bool hasNormals = Count(a.Normals) == vertexCount && vertexCount > 0;
            bool hasTangents = Count(a.Tangents) == vertexCount && vertexCount > 0;
            bool hasColors = Count(a.Colors) == vertexCount && vertexCount > 0;
            bool hasUv0 = Count(a.Uv0) == vertexCount && vertexCount > 0;
            bool hasUv1 = Count(a.Uv1) == vertexCount && vertexCount > 0;

            // A mesh is skinned when it carries per-vertex bone weights AND bind poses. Both are
            // required together: BlendWeights/BlendIndices channels are meaningless without bind poses,
            // and bind poses without weights can't bind vertices. A wrong-length BoneWeights array is
            // dropped (treated as static) with a warning, like the other attributes above.
            WarnIfWrongLength(name, "boneWeights", Count(a.BoneWeights), vertexCount, warn);
            int boneCount = Count(a.BindPoses);
            bool hasSkin =
                Count(a.BoneWeights) == vertexCount && vertexCount > 0 && boneCount > 0;

            // The per-bone invariant Unity enforces (bindpose.count == boneNameHashes.count ==
            // bonesAABB.count) is the hard structural requirement, so BoneNames must supply exactly one
            // name per bind pose. A missing/short array is a caller (compiler) bug — fail loud rather
            // than emit a mesh Unity may reject.
            if (hasSkin && Count(a.BoneNames) != boneCount)
                throw new InvalidOperationException(
                    $"mesh '{name}': skinned mesh has {boneCount} bind pose(s) but " +
                    $"{Count(a.BoneNames)} bone name(s); BoneNames must be one-per-bone");

            // Assign channel offsets in attribute-index order for present attributes. v1 stores every
            // present attribute (including colour, from Color32/255 below) as float32 (format 0): this
            // is a deliberate v1 choice that is self-consistent with the emitted type tree and round-
            // trips colors32 exactly. Unity-native would pack colour as UNorm8 (format 2), but we do
            // not require byte-parity with Unity-generated bundles.
            var channels = new MeshChannel[ChannelCount];
            int stride = 0;
            AddChannel(channels, ChPosition, true, 3, ref stride);
            AddChannel(channels, ChNormal, hasNormals, 3, ref stride);
            AddChannel(channels, ChTangent, hasTangents, 4, ref stride);
            AddChannel(channels, ChColor, hasColors, 4, ref stride);
            AddChannel(channels, ChTexCoord0, hasUv0, 2, ref stride);
            AddChannel(channels, ChTexCoord1, hasUv1, 2, ref stride);
            // Skin channels come last (attribute indices 12/13), packed contiguously after the present
            // attributes in the single interleaved stream 0. ch12 BlendWeights: 4x Float32 (16 bytes).
            // ch13 BlendIndices: 4x UInt32 (16 bytes) — format byte 10, the one non-Float32 channel.
            // Unity natively uses variable dimension (1/2/4) and a separate stream; dim 4 in stream 0 is
            // structurally valid (the loader honours each channel's (stream, offset, format, dimension)).
            AddChannel(channels, ChBlendWeights, hasSkin, 4, ref stride);
            AddChannel(channels, ChBlendIndices, hasSkin, 4, ref stride, FormatUInt32);

            // Compute the buffer size in long: stride * vertexCount overflows int for an absurdly
            // large mesh (unreachable for a real .mu) and would otherwise reach new byte[] as a
            // negative size. Fail loud instead.
            long vertexBytes = (long)stride * vertexCount;
            if (vertexBytes > int.MaxValue)
                throw new InvalidOperationException(
                    $"mesh '{name}': vertex buffer {vertexBytes} bytes (stride {stride} x {vertexCount} " +
                    "verts) exceeds int.MaxValue");

            byte[] vertexData = new byte[(int)vertexBytes];
            fixed (byte* basePtr = vertexData)
            {
                for (int v = 0; v < vertexCount; ++v)
                {
                    float* p = (float*)(basePtr + v * stride);
                    Vector3 pos = verts[v];
                    *p++ = pos.x; *p++ = pos.y; *p++ = pos.z;
                    if (hasNormals) { Vector3 n = a.Normals[v]; *p++ = n.x; *p++ = n.y; *p++ = n.z; }
                    if (hasTangents) { Vector4 t = a.Tangents[v]; *p++ = t.x; *p++ = t.y; *p++ = t.z; *p++ = t.w; }
                    if (hasColors) { Color32 c = a.Colors[v]; *p++ = c.r / 255f; *p++ = c.g / 255f; *p++ = c.b / 255f; *p++ = c.a / 255f; }
                    if (hasUv0) { Vector2 uv = a.Uv0[v]; *p++ = uv.x; *p++ = uv.y; }
                    if (hasUv1) { Vector2 uv = a.Uv1[v]; *p++ = uv.x; *p++ = uv.y; }
                    if (hasSkin)
                    {
                        BoneWeight bw = a.BoneWeights[v];
                        // ch12 BlendWeights: 4 float32 weights.
                        *p++ = bw.weight0; *p++ = bw.weight1; *p++ = bw.weight2; *p++ = bw.weight3;
                        // ch13 BlendIndices: 4 UInt32 bone indices (raw 32-bit, not float bits).
                        uint* ip = (uint*)p;
                        *ip++ = (uint)bw.boneIndex0; *ip++ = (uint)bw.boneIndex1;
                        *ip++ = (uint)bw.boneIndex2; *ip++ = (uint)bw.boneIndex3;
                        p = (float*)ip;
                    }
                }
            }

            // Index buffer: concatenated per-submesh indices, 16-bit when the mesh fits.
            int[][] tris = a.SubMeshTriangles ?? Array.Empty<int[]>();
            bool use32 = vertexCount > ushort.MaxValue;
            // Accumulate in long: a single int[] can't exceed int.MaxValue, but the SUM across
            // submeshes can, and an int accumulator would wrap (to a small/negative value) BEFORE
            // the guard below runs, letting an undersized indexData through to the unsafe write.
            long totalIndices = 0;
            for (int i = 0; i < tris.Length; ++i)
                totalIndices += tris[i]?.Length ?? 0;

            // Same overflow guard for the index buffer (see the vertex buffer above).
            long indexBytes = totalIndices * (use32 ? 4 : 2);
            if (indexBytes > int.MaxValue)
                throw new InvalidOperationException(
                    $"mesh '{name}': index buffer {indexBytes} bytes ({totalIndices} indices) " +
                    "exceeds int.MaxValue");

            byte[] indexData = new byte[(int)indexBytes];
            var subMeshes = new MeshSubMesh[tris.Length];
            Bounds meshBounds = ComputeBounds(verts, 0, vertexCount);

            fixed (byte* idxBase = indexData)
            {
                int byteOffset = 0;
                for (int s = 0; s < tris.Length; ++s)
                {
                    int[] t = tris[s] ?? Array.Empty<int>();
                    uint firstByte = (uint)byteOffset;
                    if (use32)
                    {
                        uint* ip = (uint*)(idxBase + byteOffset);
                        for (int k = 0; k < t.Length; ++k) ip[k] = (uint)t[k];
                        byteOffset += t.Length * 4;
                    }
                    else
                    {
                        ushort* ip = (ushort*)(idxBase + byteOffset);
                        for (int k = 0; k < t.Length; ++k) ip[k] = (ushort)t[k];
                        byteOffset += t.Length * 2;
                    }

                    // topology 0 == triangle list: .mu submeshes are always triangle lists, so a fixed
                    // triangle topology is a safe assumption. baseVertex 0 / firstVertex 0 /
                    // vertexCount = whole-mesh count is Unity's own convention for script-built meshes
                    // (indices are absolute into the shared vertex buffer, and the submesh's vertex
                    // range spans the entire mesh) — intentional, not a bug.
                    subMeshes[s] = new MeshSubMesh(
                        firstByte,
                        (uint)t.Length,
                        topology: 0, // triangles
                        baseVertex: 0,
                        firstVertex: 0,
                        vertexCount: (uint)vertexCount,
                        localBounds: ComputeSubMeshBounds(verts, t, meshBounds));
                }
            }

            // Bone metadata. A static mesh keeps everything empty (byte-identical to before). A skinned
            // mesh maintains Unity's invariant bindpose.count == boneNameHashes.count == bonesAABB.count.
            Matrix4x4[] bindPose = Array.Empty<Matrix4x4>();
            uint[] boneNameHashes = Array.Empty<uint>();
            uint rootBoneNameHash = 0;
            MeshBoneAABB[] bonesAABB = Array.Empty<MeshBoneAABB>();
            if (hasSkin)
            {
                bindPose = a.BindPoses;
                boneNameHashes = new uint[boneCount];
                for (int i = 0; i < boneCount; ++i)
                    boneNameHashes[i] = BoneNameHash(a.BoneNames[i]);
                rootBoneNameHash = boneNameHashes[0];

                // Conservative per-bone bounds: the whole-mesh AABB repeated. Per-bone tight bounds only
                // affect culling, never skinning, so this is safe and keeps the invariant.
                var boneMin = meshBounds.min;
                var boneMax = meshBounds.max;
                bonesAABB = new MeshBoneAABB[boneCount];
                for (int i = 0; i < boneCount; ++i)
                    bonesAABB[i] = new MeshBoneAABB(boneMin, boneMax);
            }

            return new MeshBlob
            {
                Name = name,
                VertexCount = vertexCount,
                Channels = channels,
                VertexData = vertexData,
                IndexFormat = use32 ? 1 : 0,
                IndexData = indexData,
                SubMeshes = subMeshes,
                LocalBounds = meshBounds,
                BindPose = bindPose,
                BoneNameHashes = boneNameHashes,
                RootBoneNameHash = rootBoneNameHash,
                BonesAABB = bonesAABB,
            };
        }

        static void AddChannel(
            MeshChannel[] channels, int index, bool present, int dimension, ref int stride,
            byte format = FormatFloat32)
        {
            if (!present)
                return;
            channels[index] = new MeshChannel((byte)0, (byte)stride, format, (byte)dimension);
            // Every format v1 emits (Float32=0 for all attributes, UInt32=10 for BlendIndices) is a
            // 4-byte element, so the stride advance is dimension * 4 regardless of format.
            stride += dimension * 4;
        }

        static int Count<T>(T[] array) => array?.Length ?? 0;

        // Standard CRC-32 (ISO-HDLC / zlib: reflected, polynomial 0xEDB88320, init and final-xor
        // 0xFFFFFFFF) of the UTF-8 bytes of <paramref name="name"/>. This is the exact algorithm Unity
        // 2019.4 uses for m_BoneNameHashes / m_RootBoneNameHash — verified byte-for-byte against 542
        // real skinned meshes in KSP's sharedassets0.assets (e.g. all 35 bones of body01 and the EVA
        // jetpack bone reproduced exactly). NOTE: Unity hashes the bone's FULL transform path from the
        // model root (e.g. "globalMove01/joints01/bn_spA01"), not the bare leaf name, so the caller must
        // pass that path in Arrays.BoneNames to match the native stored value. The stored value does not
        // affect skinning (the compiler binds SkinnedMeshRenderer.bones by name at replay and the
        // bindpose[i] <-> BlendIndices[i] <-> bones[i] correspondence is by index), so a leaf-only name
        // still skins correctly; only the byte-parity of the hash field would differ.
        static readonly uint[] Crc32Table = BuildCrc32Table();

        static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; ++i)
            {
                uint c = i;
                for (int k = 0; k < 8; ++k)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        public static uint BoneNameHash(string name)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < bytes.Length; ++i)
                crc = (crc >> 8) ^ Crc32Table[(crc ^ bytes[i]) & 0xFF];
            return crc ^ 0xFFFFFFFFu;
        }

        // Warns only when an attribute array is actually present (non-null, non-empty) but its length
        // disagrees with the vertex count, i.e. it is about to be silently dropped. Cheap: no
        // allocation unless the (rare) warning path fires. When a <paramref name="warn"/> sink is
        // supplied (worker-thread callers) the message is routed there; otherwise it falls back to
        // Debug.LogWarning for main-thread/offline callers.
        static void WarnIfWrongLength(string name, string attr, int length, int vertexCount, Action<string> warn)
        {
            if (length != 0 && length != vertexCount)
            {
                string message =
                    $"[MeshBlobBuilder] mesh '{name}': {attr} array length {length} != vertexCount " +
                    $"{vertexCount}; dropping {attr}";
                if (warn != null)
                    warn(message);
                else
                    Debug.LogWarning(message);
            }
        }

        static Bounds ComputeBounds(Vector3[] verts, int start, int count)
        {
            if (count <= 0)
                return new Bounds(Vector3.zero, Vector3.zero);
            Vector3 min = verts[start], max = verts[start];
            for (int i = start + 1; i < start + count; ++i)
            {
                Vector3 p = verts[i];
                if (p.x < min.x) min.x = p.x; else if (p.x > max.x) max.x = p.x;
                if (p.y < min.y) min.y = p.y; else if (p.y > max.y) max.y = p.y;
                if (p.z < min.z) min.z = p.z; else if (p.z > max.z) max.z = p.z;
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        static Bounds ComputeSubMeshBounds(Vector3[] verts, int[] indices, Bounds fallback)
        {
            if (indices == null || indices.Length == 0 || verts.Length == 0)
                return fallback;
            Vector3 min = verts[indices[0]], max = min;
            for (int i = 1; i < indices.Length; ++i)
            {
                Vector3 p = verts[indices[i]];
                if (p.x < min.x) min.x = p.x; else if (p.x > max.x) max.x = p.x;
                if (p.y < min.y) min.y = p.y; else if (p.y > max.y) max.y = p.y;
                if (p.z < min.z) min.z = p.z; else if (p.z > max.z) max.z = p.z;
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }
    }
}
