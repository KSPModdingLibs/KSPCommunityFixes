using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// One serialization-ready Unity mesh, produced on a background thread by the model compiler and
    /// written verbatim into a mesh bundle by <see cref="MeshBundleBuilder"/>. Everything here is
    /// already in the exact on-wire form Unity 2019.4 expects: the vertex data is interleaved into a
    /// single stream with a matching <see cref="Channels"/> descriptor, indices are packed to
    /// <see cref="IndexFormat"/>, and bounds/metrics are precomputed (there is no
    /// <c>RecalculateBounds</c> on the main thread anymore). Geometry is stored inline in the bundle,
    /// so there is no external stream data.
    /// </summary>
    internal sealed class MeshBlob
    {
        /// <summary>
        /// The mesh's serialized <c>m_Name</c> and its <c>m_Container</c> key. Must already be
        /// canonical (lowercased, forward slashes) so <c>AssetBundle.LoadAssetAsync(name)</c> can find
        /// it — Unity canonicalizes the lookup name but compares it verbatim against the stored key.
        /// </summary>
        public string Name;

        public int VertexCount;

        /// <summary>
        /// The full <c>m_Channels</c> descriptor array, one entry per Unity vertex attribute
        /// (absent attributes have <see cref="MeshChannel.Dimension"/> 0). Describes how
        /// <see cref="VertexData"/> is laid out.
        /// </summary>
        public MeshChannel[] Channels;

        /// <summary>The interleaved vertex stream (<c>m_VertexData.m_DataSize</c>), inline.</summary>
        public byte[] VertexData;

        /// <summary>0 == 16-bit indices, 1 == 32-bit indices (Unity's <c>IndexFormat</c>).</summary>
        public int IndexFormat;

        /// <summary>The concatenated per-submesh index buffer (<c>m_IndexBuffer</c>), inline.</summary>
        public byte[] IndexData;

        public MeshSubMesh[] SubMeshes;

        /// <summary>Whole-mesh local bounds (<c>m_LocalAABB</c>): center + extent (half-size).</summary>
        public Bounds LocalBounds;

        /// <summary>Skinning bind poses (<c>m_BindPose</c>); null/empty for a static mesh.</summary>
        public Matrix4x4[] BindPose;

        /// <summary>
        /// One CRC32 per bone (<c>m_BoneNameHashes</c>); null/empty for a static mesh. Count must
        /// equal <see cref="BindPose"/> and <see cref="BonesAABB"/> (Unity's per-bone invariant).
        /// </summary>
        public uint[] BoneNameHashes;

        /// <summary>The root bone's hash (<c>m_RootBoneNameHash</c>); <c>BoneNameHashes[0]</c>, 0 for a static mesh.</summary>
        public uint RootBoneNameHash;

        /// <summary>
        /// Per-bone local bounds (<c>m_BonesAABB</c>); null/empty for a static mesh. Count must equal
        /// <see cref="BindPose"/>. Filled conservatively with the whole-mesh bounds per bone (bounds
        /// affect culling only, not skinning).
        /// </summary>
        public MeshBoneAABB[] BonesAABB;

        /// <summary>
        /// UV distribution metrics (<c>m_MeshMetrics[0..1]</c>), one per UV channel, baked by
        /// <see cref="MeshMetrics.Compute"/> during compilation. A channel with no UVs keeps the neutral 1.0.
        /// </summary>
        public float MeshMetric0 = 1f;
        public float MeshMetric1 = 1f;
    }

    /// <summary>
    /// One <c>MinMaxAABB</c> entry (<c>m_BonesAABB</c> element): a min/max corner pair, 24 bytes
    /// (two <c>Vector3f</c>). Distinct from <see cref="Bounds"/> (center/extent), which the local
    /// AABB and submesh bounds use.
    /// </summary>
    internal readonly struct MeshBoneAABB
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;

        public MeshBoneAABB(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>One <c>ChannelInfo</c> entry: which stream/offset/format/dimension a vertex attribute uses.</summary>
    internal readonly struct MeshChannel
    {
        public readonly byte Stream;
        public readonly byte Offset;
        public readonly byte Format;
        public readonly byte Dimension;

        public MeshChannel(byte stream, byte offset, byte format, byte dimension)
        {
            Stream = stream;
            Offset = offset;
            Format = format;
            Dimension = dimension;
        }
    }

    /// <summary>One <c>SubMesh</c> record: an index range plus its bounds and topology.</summary>
    internal readonly struct MeshSubMesh
    {
        public readonly uint FirstByte;
        public readonly uint IndexCount;
        public readonly int Topology;
        public readonly uint BaseVertex;
        public readonly uint FirstVertex;
        public readonly uint VertexCount;
        public readonly Bounds LocalBounds;

        public MeshSubMesh(
            uint firstByte,
            uint indexCount,
            int topology,
            uint baseVertex,
            uint firstVertex,
            uint vertexCount,
            Bounds localBounds)
        {
            FirstByte = firstByte;
            IndexCount = indexCount;
            Topology = topology;
            BaseVertex = baseVertex;
            FirstVertex = firstVertex;
            VertexCount = vertexCount;
            LocalBounds = localBounds;
        }
    }
}
