using System;
using System.Collections.Generic;
using KSPCommunityFixes.Library.TextureBundle;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// Builds an in-memory UnityFS bundle wrapping N <c>Mesh</c> objects plus the one
    /// <c>AssetBundle</c> that references them, the mesh analogue of
    /// <see cref="TextureBundleBuilder"/>. Unlike the texture path, mesh geometry can't be streamed
    /// from the source file (the <c>.mu</c> layout differs from Unity's vertex stream, and Unity 2019.4
    /// never keeps a CPU-readable copy of streamed mesh vertices), so the interleaved vertex/index
    /// bytes are written <b>inline</b> and <c>m_StreamData</c> is empty. Pure CPU work; safe to call
    /// from a background thread. Reuses the <see cref="TextureBundle"/> writer stack unchanged.
    /// </summary>
    /// <remarks>
    /// <see cref="WriteMeshBody"/> reproduces Unity 2019.4.18f1's serialized <c>Mesh</c> (class 43)
    /// field order, sizes and alignment exactly as the embedded type tree encodes them; only the
    /// align-after flag (type-tree meta bit 0x4000) affects the byte stream.
    /// </remarks>
    internal static class MeshBundleBuilder
    {
        // Unity BuildTarget.StandaloneWindows64.
        public const int StandaloneWindows64 = 19;

        const long AssetBundlePathId = 1;

        // Mesh objects get path ids 2, 3, 4, ... (the AssetBundle object is path id 1).
        const long FirstMeshPathId = 2;

        /// <summary>
        /// Canonicalize an asset name into the exact form Unity uses for the <c>m_Container</c> lookup
        /// key: lowercased (invariant culture) with backslashes replaced by forward slashes. Unity
        /// canonicalizes the query passed to <c>AssetBundle.LoadAsset(name)</c> this same way but then
        /// compares it <b>verbatim</b> against the stored key, so a stored key that isn't already
        /// canonical can never be found. Pure; the model compiler reuses this to derive the lookup key.
        /// </summary>
        public static string Canonicalize(string name) =>
            (name ?? string.Empty).ToLowerInvariant().Replace('\\', '/');

        /// <summary>
        /// Build one UnityFS bundle wrapping every mesh in <paramref name="blobs"/> (each keyed in the
        /// bundle's container by its canonical <see cref="MeshBlob.Name"/>) plus the one
        /// <c>AssetBundle</c> object. Returns <c>null</c> when <paramref name="blobs"/> is empty.
        /// Throws if two blobs canonicalize to the same container key (an ambiguous name lookup).
        /// </summary>
        public static byte[] BuildMany(
            IReadOnlyList<MeshBlob> blobs,
            int targetPlatform = StandaloneWindows64
        )
        {
            if (blobs is null)
                throw new ArgumentNullException(nameof(blobs));

            int n = blobs.Count;
            if (n == 0)
                return null;

            // Canonicalize every container key once up front and reject duplicates: a duplicate
            // canonical key makes AssetBundle.LoadAsset(name) ambiguous (the runtime container is a
            // multimap, so a colliding key would silently resolve to an arbitrary mesh). Fail loud.
            var canonicalKeys = new string[n];
            var seen = new Dictionary<string, int>(n, StringComparer.Ordinal);
            for (int i = 0; i < n; ++i)
            {
                string key = Canonicalize(blobs[i].Name);
                if (seen.TryGetValue(key, out int prev))
                    throw new InvalidOperationException(
                        $"duplicate mesh container key '{key}' from meshes '{blobs[prev].Name}' and " +
                        $"'{blobs[i].Name}'; canonical container keys must be unique for name lookup");
                seen[key] = i;
                canonicalKeys[i] = key;
            }

            // Unique guid so concurrent loads don't collide.
            string cab = "CAB-" + Guid.NewGuid().ToString("N");

            var w = new BundleBufferWriter(EstimateSize(blobs));

            // Geometry is inline in the object bodies, so there is no streamed resS.
            var prefix = BundleWriter.WriteHeaderAndBlocksInfo(w, cab, resSLength: 0);

            // One AssetBundle object plus one Mesh object per blob.
            var objects = new SerializedFileWriter.ObjectMeta[n + 1];
            var slots = new SerializedFileWriter.ObjectSlot[n + 1];
            objects[0] = new SerializedFileWriter.ObjectMeta(
                AssetBundlePathId, SerializedTypeTrees.AssetBundleClassId);
            for (int i = 0; i < n; ++i)
                objects[i + 1] = new SerializedFileWriter.ObjectMeta(
                    FirstMeshPathId + i, SerializedTypeTrees.MeshClassId);

            var file = SerializedFileWriter.BeginFile(w, targetPlatform, objects, slots);

            file.BeginObject(w, ref slots[0]);
            WriteAssetBundleBody(w, cab, canonicalKeys);
            file.EndObject(w, slots[0]);

            for (int i = 0; i < n; ++i)
            {
                file.BeginObject(w, ref slots[i + 1]);
                WriteMeshBody(w, blobs[i]);
                file.EndObject(w, slots[i + 1]);
            }

            long serializedFileLength = file.End(w);

            w.AlignBase = 0;
            BundleWriter.Finish(w, prefix, serializedFileLength);

            return w.ToArray();
        }

        static int EstimateSize(IReadOnlyList<MeshBlob> blobs)
        {
            long total =
                1024
                + ReferenceTypeTrees.TypeEntry(SerializedTypeTrees.AssetBundleClassId).Length
                + ReferenceTypeTrees.TypeEntry(SerializedTypeTrees.MeshClassId).Length;
            for (int i = 0; i < blobs.Count; ++i)
            {
                MeshBlob b = blobs[i];
                total += 512
                    + (b.VertexData?.Length ?? 0)
                    + (b.IndexData?.Length ?? 0)
                    + (b.SubMeshes?.Length ?? 0) * 48
                    + (b.BindPose?.Length ?? 0) * 64;
            }
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        // ---- Mesh object body (Unity 2019.4.18f1, class 43) ---------------------------------------

        static void WriteMeshBody(BundleBufferWriter w, MeshBlob mesh)
        {
            w.WriteAlignedString(mesh.Name); // m_Name

            // m_SubMeshes : vector<SubMesh>, Array aligns
            var subMeshes = w.BeginArray();
            MeshSubMesh[] subs = mesh.SubMeshes ?? Array.Empty<MeshSubMesh>();
            for (int i = 0; i < subs.Length; ++i)
            {
                subMeshes.Add();
                WriteSubMesh(w, subs[i]);
            }
            subMeshes.End(align: true);

            // m_Shapes : BlendShapeData — four empty aligned vectors (no blendshapes in .mu)
            w.BeginArray().End(align: true); // vertices
            w.BeginArray().End(align: true); // shapes
            w.BeginArray().End(align: true); // channels
            w.BeginArray().End(align: true); // fullWeights

            // m_BindPose : vector<Matrix4x4f>, Array aligns
            var bindPose = w.BeginArray();
            Matrix4x4[] poses = mesh.BindPose ?? Array.Empty<Matrix4x4>();
            for (int i = 0; i < poses.Length; ++i)
            {
                bindPose.Add();
                WriteMatrix4x4(w, poses[i]);
            }
            bindPose.End(align: true);

            w.BeginArray().End(align: true); // m_BoneNameHashes : vector<uint>
            w.WriteUInt32(0); // m_RootBoneNameHash
            w.BeginArray().End(align: true); // m_BonesAABB : vector<MinMaxAABB>
            w.BeginArray().End(align: true); // m_VariableBoneCountWeights.m_Data : vector<uint>

            w.WriteByte(0); // m_MeshCompression (0 == uncompressed)
            w.WriteBool(true); // m_IsReadable — keep the CPU copy (stock KSP behaviour)
            w.WriteBool(false); // m_KeepVertices
            w.WriteBool(false); // m_KeepIndices
            w.Align(4); // m_KeepIndices aligns

            w.WriteInt32(mesh.IndexFormat); // m_IndexFormat (0 == UInt16, 1 == UInt32)

            // m_IndexBuffer : vector<UInt8>, Array aligns
            byte[] indexData = mesh.IndexData ?? Array.Empty<byte>();
            w.WriteInt32(indexData.Length);
            w.WriteBytes(indexData);
            w.Align(4);

            // m_VertexData : VertexData (node aligns after)
            w.WriteUInt32((uint)mesh.VertexCount); // m_VertexCount
            var channels = w.BeginArray(); // m_Channels : vector<ChannelInfo>, Array aligns
            MeshChannel[] chans = mesh.Channels ?? Array.Empty<MeshChannel>();
            for (int i = 0; i < chans.Length; ++i)
            {
                channels.Add();
                w.WriteByte(chans[i].Stream);
                w.WriteByte(chans[i].Offset);
                w.WriteByte(chans[i].Format);
                w.WriteByte(chans[i].Dimension);
            }
            channels.End(align: true);
            byte[] vertexData = mesh.VertexData ?? Array.Empty<byte>();
            w.WriteInt32(vertexData.Length); // m_DataSize : TypelessData, aligns
            w.WriteBytes(vertexData);
            w.Align(4);
            w.Align(4); // VertexData node aligns (no-op after m_DataSize's align)

            WriteEmptyCompressedMesh(w); // m_CompressedMesh (all packed vectors empty)

            WriteAABB(w, mesh.LocalBounds); // m_LocalAABB
            w.WriteInt32(0); // m_MeshUsageFlags
            w.BeginArray().End(align: true); // m_BakedConvexCollisionMesh : vector<UInt8>
            w.BeginArray().End(align: true); // m_BakedTriangleCollisionMesh : vector<UInt8>
            w.WriteSingle(mesh.MeshMetric0); // m_MeshMetrics[0]
            w.WriteSingle(mesh.MeshMetric1); // m_MeshMetrics[1]
            w.Align(4); // m_MeshMetrics[1] aligns

            // m_StreamData : StreamingInfo — empty (geometry is inline above)
            w.WriteUInt32(0); // offset
            w.WriteUInt32(0); // size
            w.WriteAlignedString(string.Empty); // path
        }

        static void WriteSubMesh(BundleBufferWriter w, in MeshSubMesh s)
        {
            w.WriteUInt32(s.FirstByte);
            w.WriteUInt32(s.IndexCount);
            w.WriteInt32(s.Topology);
            w.WriteUInt32(s.BaseVertex);
            w.WriteUInt32(s.FirstVertex);
            w.WriteUInt32(s.VertexCount);
            WriteAABB(w, s.LocalBounds);
        }

        static void WriteAABB(BundleBufferWriter w, in Bounds bounds)
        {
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            w.WriteSingle(c.x);
            w.WriteSingle(c.y);
            w.WriteSingle(c.z);
            w.WriteSingle(e.x);
            w.WriteSingle(e.y);
            w.WriteSingle(e.z);
        }

        static void WriteMatrix4x4(BundleBufferWriter w, in Matrix4x4 m)
        {
            // Unity serializes e00,e01,...,e33 (row-major field names). Matrix4x4 indexer m[row,col]
            // matches eRowCol.
            w.WriteSingle(m.m00); w.WriteSingle(m.m01); w.WriteSingle(m.m02); w.WriteSingle(m.m03);
            w.WriteSingle(m.m10); w.WriteSingle(m.m11); w.WriteSingle(m.m12); w.WriteSingle(m.m13);
            w.WriteSingle(m.m20); w.WriteSingle(m.m21); w.WriteSingle(m.m22); w.WriteSingle(m.m23);
            w.WriteSingle(m.m30); w.WriteSingle(m.m31); w.WriteSingle(m.m32); w.WriteSingle(m.m33);
        }

        // All ten PackedBitVectors are empty; five carry m_Range/m_Start floats (see the type tree).
        static void WriteEmptyCompressedMesh(BundleBufferWriter w)
        {
            WritePackedBitVector(w, hasRange: true);  // m_Vertices
            WritePackedBitVector(w, hasRange: true);  // m_UV
            WritePackedBitVector(w, hasRange: true);  // m_Normals
            WritePackedBitVector(w, hasRange: true);  // m_Tangents
            WritePackedBitVector(w, hasRange: false); // m_Weights
            WritePackedBitVector(w, hasRange: false); // m_NormalSigns
            WritePackedBitVector(w, hasRange: false); // m_TangentSigns
            WritePackedBitVector(w, hasRange: true);  // m_FloatColors
            WritePackedBitVector(w, hasRange: false); // m_BoneIndices
            WritePackedBitVector(w, hasRange: false); // m_Triangles
            w.WriteUInt32(0); // m_UVInfo
        }

        static void WritePackedBitVector(BundleBufferWriter w, bool hasRange)
        {
            w.WriteUInt32(0); // m_NumItems
            if (hasRange)
            {
                w.WriteSingle(0f); // m_Range
                w.WriteSingle(0f); // m_Start
            }
            w.BeginArray().End(align: true); // m_Data : vector<UInt8>, Array aligns
            w.WriteByte(0); // m_BitSize
            w.Align(4); // m_BitSize aligns
        }

        // ---- AssetBundle object body --------------------------------------------------------------

        static void WriteAssetBundleBody(
            BundleBufferWriter w,
            string identity,
            string[] canonicalKeys
        )
        {
            int n = canonicalKeys.Length;

            w.WriteAlignedString(identity); // m_Name

            // m_PreloadTable : one PPtr per mesh, in blob order (path ids 2..N+1). Each container
            // entry references its own single-entry slice of this table by blob index.
            var preload = w.BeginArray();
            for (int i = 0; i < n; ++i)
            {
                preload.Add();
                WritePPtr(w, fileId: 0, pathId: FirstMeshPathId + i);
            }
            preload.End(align: true);

            // m_Container : map<string, AssetInfo>. Array not aligned. Written sorted ascending by
            // canonical key. Ghidra (UnityPlayer 2019.4) shows Unity deserializes this map into a
            // std::multimap<string, AssetInfo, std::less<string>> by inserting each pair into a
            // red-black tree, so the runtime lookup is a tree search that re-sorts on load and is
            // therefore independent of the serialized order. We still sort here to match the layout of
            // real Unity-built bundles (correct whether the lookup were linear or binary). Only the
            // container entry order changes: each entry still keys its own mesh (path id) and its own
            // preload slice (blob index), so meshes keep path ids 2..N+1 in blob order.
            var order = new int[n];
            for (int i = 0; i < n; ++i)
                order[i] = i;
            Array.Sort(order, (a, b) => string.CompareOrdinal(canonicalKeys[a], canonicalKeys[b]));

            var container = w.BeginArray();
            for (int j = 0; j < n; ++j)
            {
                int i = order[j];
                container.Add();
                w.WriteAlignedString(canonicalKeys[i]); // pair.first (canonical lookup key)
                WriteAssetInfo(
                    w, preloadIndex: i, preloadSize: 1, fileId: 0, pathId: FirstMeshPathId + i);
            }
            container.End();

            WriteAssetInfo(w, preloadIndex: 0, preloadSize: 0, fileId: 0, pathId: 0); // m_MainAsset
            w.WriteUInt32(1); // m_RuntimeCompatibility
            w.WriteAlignedString(identity); // m_AssetBundleName
            w.BeginArray().End(align: true); // m_Dependencies (empty)
            w.WriteBool(false); // m_IsStreamedSceneAssetBundle
            w.Align(4);
            w.WriteInt32(0); // m_ExplicitDataLayout
            w.WriteInt32(0); // m_PathFlags
            w.BeginArray().End(); // m_SceneHashes (empty map, Array not aligned)
        }

        static void WritePPtr(BundleBufferWriter w, int fileId, long pathId)
        {
            w.WriteInt32(fileId); // m_FileID
            w.WriteInt64(pathId); // m_PathID
        }

        static void WriteAssetInfo(
            BundleBufferWriter w,
            int preloadIndex,
            int preloadSize,
            int fileId,
            long pathId
        )
        {
            w.WriteInt32(preloadIndex);
            w.WriteInt32(preloadSize);
            WritePPtr(w, fileId, pathId);
        }
    }
}
