using System;
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

        const byte FormatFloat32 = 0;

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
        }

        /// <summary>Build a <see cref="MeshBlob"/> from a live <c>UnityEngine.Mesh</c> (main thread only).</summary>
        public static MeshBlob FromMesh(Mesh mesh, string name)
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
            return FromArrays(name, in arrays);
        }

        public static unsafe MeshBlob FromArrays(string name, in Arrays a)
        {
            Vector3[] verts = a.Vertices ?? Array.Empty<Vector3>();
            int vertexCount = verts.Length;

            // A non-null attribute array whose length doesn't match the vertex count is dropped
            // (treated as absent) below; warn so a mismatched .mu doesn't silently lose an attribute.
            WarnIfWrongLength(name, "normals", Count(a.Normals), vertexCount);
            WarnIfWrongLength(name, "tangents", Count(a.Tangents), vertexCount);
            WarnIfWrongLength(name, "colors", Count(a.Colors), vertexCount);
            WarnIfWrongLength(name, "uv0", Count(a.Uv0), vertexCount);
            WarnIfWrongLength(name, "uv1", Count(a.Uv1), vertexCount);

            bool hasNormals = Count(a.Normals) == vertexCount && vertexCount > 0;
            bool hasTangents = Count(a.Tangents) == vertexCount && vertexCount > 0;
            bool hasColors = Count(a.Colors) == vertexCount && vertexCount > 0;
            bool hasUv0 = Count(a.Uv0) == vertexCount && vertexCount > 0;
            bool hasUv1 = Count(a.Uv1) == vertexCount && vertexCount > 0;

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
                BindPose = Array.Empty<Matrix4x4>(),
            };
        }

        static void AddChannel(MeshChannel[] channels, int index, bool present, int dimension, ref int stride)
        {
            if (!present)
                return;
            channels[index] = new MeshChannel((byte)0, (byte)stride, FormatFloat32, (byte)dimension);
            stride += dimension * 4;
        }

        static int Count<T>(T[] array) => array?.Length ?? 0;

        // Warns only when an attribute array is actually present (non-null, non-empty) but its length
        // disagrees with the vertex count, i.e. it is about to be silently dropped. Cheap: no
        // allocation unless the (rare) warning path fires.
        static void WarnIfWrongLength(string name, string attr, int length, int vertexCount)
        {
            if (length != 0 && length != vertexCount)
                Debug.LogWarning(
                    $"[MeshBlobBuilder] mesh '{name}': {attr} array length {length} != vertexCount " +
                    $"{vertexCount}; dropping {attr}");
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
