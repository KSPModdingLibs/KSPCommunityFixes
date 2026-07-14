using System;
using System.Collections.Generic;
using System.IO;

namespace KSPCommunityFixes.Library.TextureBundle
{
    /// <summary>
    /// Builds a minimal UnityFS bundle wrapping a single streamed <c>Texture2D</c> and the
    /// <c>AssetBundle</c> that references it, where the texture's pixel data lives in an existing
    /// DDS file on disk. The generated bundle carries only ~1&#160;KB of metadata: the texture's
    /// <c>m_StreamData.path</c> is the absolute path of the DDS file and <c>m_StreamData.offset</c>
    /// is its data offset, so Unity opens the file and streams the compressed pixels itself. Pure
    /// CPU work; safe to call from a background thread.
    /// </summary>
    /// <remarks>
    /// The whole prefix is written into a single <see cref="BundleBufferWriter"/>: the UnityFS
    /// framing (<see cref="BundleWriter"/>), the serialized-file framing
    /// (<see cref="SerializedFileWriter"/>) and the two object bodies written here by hand. The
    /// bodies reproduce the exact field order, sizes and alignment padding of Unity 2019.4's own
    /// layout — the same layout the embedded type tree encodes.
    ///
    /// <para>Borrowed from KSPTextureLoader
    /// (../KSPTextureLoader/src/KSPTextureLoader/Format/Bundle/TextureBundleBuilder.cs), stripped
    /// to the classic Texture2D + external-file path.</para>
    /// </remarks>
    internal static class TextureBundleBuilder
    {
        // Unity BuildTarget.StandaloneWindows64.
        public const int StandaloneWindows64 = 19;

        // UnityEngine.Rendering.TextureDimension.Tex2D.
        const int TextureDimensionTex2D = 2;

        // Unity's usual default fallback, TextureFormat.ARGB32.
        const int ForcedFallbackFormat = 4;

        const long AssetBundlePathId = 1;
        const long TexturePathId = 2;

        // Texture objects in a multi-texture bundle get path ids 2, 3, 4, ... (the AssetBundle
        // object is path id 1).
        const long FirstTexturePathId = 2;

        // Unity canonicalizes the name passed to LoadAsset (lowercase, forward slashes) but
        // compares it against the stored container key verbatim, so a key with uppercase
        // characters or backslashes can never be looked up. A fixed already-canonical key
        // sidesteps that entirely; the caller renames the loaded texture afterwards anyway.
        const string ContainerKey = "texture";

        /// <summary>The texture to wrap in a bundle.</summary>
        public sealed class TextureRequest
        {
            /// <summary>Serialized <c>m_Name</c>. Cosmetic only: the asset is looked up by the fixed
            /// container key in <see cref="Built.AssetName"/>, and callers overwrite the texture's
            /// name after loading it.</summary>
            public string Name = "texture";

            public int Width;
            public int Height;
            public int MipCount = 1;

            /// <summary>The legacy <c>TextureFormat</c> written into <c>m_TextureFormat</c>.</summary>
            public int Format;

            /// <summary>0 == linear, 1 == sRGB.</summary>
            public int ColorSpace = 1;

            /// <summary>Whether Unity should keep a CPU-side copy of the pixels.</summary>
            public bool Readable;
        }

        /// <summary>The built bundle prefix plus the name to request from it.</summary>
        public readonly struct Built
        {
            public readonly byte[] Prefix;
            public readonly string AssetName;
            public readonly long PixelsLength;

            public Built(byte[] prefix, string assetName, long pixelsLength)
            {
                Prefix = prefix;
                AssetName = assetName;
                PixelsLength = pixelsLength;
            }
        }

        /// <summary>
        /// One streamed texture to place in a combined bundle built by <see cref="BuildMany"/>.
        /// <see cref="Name"/> is both the serialized <c>m_Name</c> and the <c>m_Container</c> key,
        /// so the caller can look the loaded texture back up by its name.
        /// </summary>
        public readonly struct TextureEntry
        {
            public readonly string Name;
            public readonly int Width;
            public readonly int Height;
            public readonly int MipCount;

            /// <summary>The legacy <c>TextureFormat</c> written into <c>m_TextureFormat</c>.</summary>
            public readonly int Format;

            /// <summary>0 == linear, 1 == sRGB.</summary>
            public readonly int ColorSpace;

            public readonly bool Readable;

            /// <summary>Absolute path of the DDS file the pixels are streamed from.</summary>
            public readonly string ExternalPath;

            /// <summary>Byte offset of the pixel data within <see cref="ExternalPath"/>.</summary>
            public readonly long ExternalOffset;

            /// <summary>Total mip-chain byte size streamed from the file.</summary>
            public readonly long PixelsLength;

            public TextureEntry(
                string name,
                int width,
                int height,
                int mipCount,
                int format,
                int colorSpace,
                bool readable,
                string externalPath,
                long externalOffset,
                long pixelsLength)
            {
                Name = name;
                Width = width;
                Height = height;
                MipCount = mipCount;
                Format = format;
                ColorSpace = colorSpace;
                Readable = readable;
                ExternalPath = externalPath;
                ExternalOffset = externalOffset;
                PixelsLength = pixelsLength;
            }
        }

        /// <summary>
        /// Build a bundle whose streamed pixel data lives in the file at
        /// <paramref name="externalPath"/>, starting at <paramref name="externalOffset"/> and
        /// spanning <paramref name="pixelsLength"/> bytes.
        /// </summary>
        public static Built Build(
            TextureRequest req,
            long pixelsLength,
            string externalPath,
            long externalOffset,
            int targetPlatform = StandaloneWindows64
        )
        {
            if (req is null)
                throw new ArgumentNullException(nameof(req));
            if (externalPath is null)
                throw new ArgumentNullException(nameof(externalPath));
            if (pixelsLength < 0)
                throw new ArgumentOutOfRangeException(nameof(pixelsLength));
            if (externalOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(externalOffset));

            // Use a unique guid so that concurrent loads don't collide.
            string cab = "CAB-" + Guid.NewGuid().ToString("N");

            // UnityFS uses forward slashes and runs into issues with backslashes, so normalize.
            string streamPath = Path.GetFullPath(externalPath).Replace('\\', '/');
            long streamOffset = externalOffset;
            long streamSize = pixelsLength;
            if (streamOffset + streamSize > uint.MaxValue)
                throw new InvalidOperationException(
                    "texture data exceeds 4 GB; stream offsets are 32-bit in Unity bundles"
                );

            var w = new BundleBufferWriter(EstimateSize());

            // No pixel bytes are appended: they live in the external DDS file.
            var prefix = BundleWriter.WriteHeaderAndBlocksInfo(w, cab, resSLength: 0);

            Span<SerializedFileWriter.ObjectMeta> objects =
                stackalloc SerializedFileWriter.ObjectMeta[2];
            objects[0] = new SerializedFileWriter.ObjectMeta(
                AssetBundlePathId, SerializedTypeTrees.AssetBundleClassId);
            objects[1] = new SerializedFileWriter.ObjectMeta(
                TexturePathId, SerializedTypeTrees.Texture2DClassId);
            Span<SerializedFileWriter.ObjectSlot> slots =
                stackalloc SerializedFileWriter.ObjectSlot[2];

            var file = SerializedFileWriter.BeginFile(w, targetPlatform, objects, slots);

            file.BeginObject(w, ref slots[0]);
            WriteAssetBundleBody(w, cab, TexturePathId);
            file.EndObject(w, slots[0]);

            file.BeginObject(w, ref slots[1]);
            WriteClassicTextureBody(w, req, streamPath, streamOffset, streamSize);
            file.EndObject(w, slots[1]);

            long serializedFileLength = file.End(w);

            w.AlignBase = 0;
            BundleWriter.Finish(w, prefix, serializedFileLength);

            return new Built(w.ToArray(), ContainerKey, pixelsLength);
        }

        /// <summary>
        /// Build a single UnityFS bundle wrapping every texture in <paramref name="entries"/>, each
        /// streaming its pixels from its own DDS file on disk, plus the one <c>AssetBundle</c> object
        /// that references them all. Loading this bundle once (a single
        /// <c>AssetBundle.LoadFromMemoryAsync</c> + <c>LoadAllAssetsAsync</c>) realizes them all,
        /// which avoids the native-allocator exhaustion of loading tens of thousands of one-texture
        /// bundles. Returns <c>null</c> when <paramref name="entries"/> is empty. Pure CPU work; safe
        /// to call from a background thread.
        /// </summary>
        public static byte[] BuildMany(
            IReadOnlyList<TextureEntry> entries,
            int targetPlatform = StandaloneWindows64
        )
        {
            if (entries is null)
                throw new ArgumentNullException(nameof(entries));

            int n = entries.Count;
            if (n == 0)
                return null;

            // Use a unique guid so that concurrent loads don't collide.
            string cab = "CAB-" + Guid.NewGuid().ToString("N");

            var w = new BundleBufferWriter(EstimateSizeMany(n));

            // No pixel bytes are appended: they live in the external DDS files.
            var prefix = BundleWriter.WriteHeaderAndBlocksInfo(w, cab, resSLength: 0);

            // One AssetBundle object plus one Texture2D object per entry. N is in the tens of
            // thousands for a heavy install, so these live on the heap, not the stack.
            var objects = new SerializedFileWriter.ObjectMeta[n + 1];
            var slots = new SerializedFileWriter.ObjectSlot[n + 1];
            objects[0] = new SerializedFileWriter.ObjectMeta(
                AssetBundlePathId, SerializedTypeTrees.AssetBundleClassId);
            for (int i = 0; i < n; ++i)
                objects[i + 1] = new SerializedFileWriter.ObjectMeta(
                    FirstTexturePathId + i, SerializedTypeTrees.Texture2DClassId);

            var file = SerializedFileWriter.BeginFile(w, targetPlatform, objects, slots);

            file.BeginObject(w, ref slots[0]);
            WriteAssetBundleBodyMany(w, cab, entries);
            file.EndObject(w, slots[0]);

            for (int i = 0; i < n; ++i)
            {
                TextureEntry e = entries[i];

                // UnityFS uses forward slashes and runs into issues with backslashes, so normalize.
                string streamPath = Path.GetFullPath(e.ExternalPath).Replace('\\', '/');
                long streamOffset = e.ExternalOffset;
                long streamSize = e.PixelsLength;
                if (streamOffset + streamSize > uint.MaxValue)
                    throw new InvalidOperationException(
                        "texture data exceeds 4 GB; stream offsets are 32-bit in Unity bundles"
                    );

                var req = new TextureRequest
                {
                    Name = e.Name,
                    Width = e.Width,
                    Height = e.Height,
                    MipCount = e.MipCount,
                    Format = e.Format,
                    ColorSpace = e.ColorSpace,
                    Readable = e.Readable,
                };

                file.BeginObject(w, ref slots[i + 1]);
                WriteClassicTextureBody(w, req, streamPath, streamOffset, streamSize);
                file.EndObject(w, slots[i + 1]);
            }

            long serializedFileLength = file.End(w);

            w.AlignBase = 0;
            BundleWriter.Finish(w, prefix, serializedFileLength);

            return w.ToArray();
        }

        // Framing plus the two verbatim type entries (which dominate) plus room for N object bodies.
        // Each body is m_Name + the fixed Texture2D fields + the m_StreamData path (an absolute file
        // path); ~512 bytes/texture is a generous per-entry estimate so the buffer rarely regrows.
        static int EstimateSizeMany(int n) =>
            1024
            + ReferenceTypeTrees.TypeEntry(SerializedTypeTrees.AssetBundleClassId).Length
            + ReferenceTypeTrees.TypeEntry(SerializedTypeTrees.Texture2DClassId).Length
            + n * 512;

        // A generous starting capacity so the buffer rarely regrows: the framing plus the two
        // verbatim type entries (the type trees dominate) plus room for the bodies.
        static int EstimateSize() =>
            1024
            + ReferenceTypeTrees.TypeEntry(SerializedTypeTrees.AssetBundleClassId).Length
            + ReferenceTypeTrees.TypeEntry(SerializedTypeTrees.Texture2DClassId).Length;

        static void WriteClassicTextureBody(
            BundleBufferWriter w,
            TextureRequest req,
            string streamPath,
            long streamOffset,
            long streamSize
        )
        {
            // Unlike m_StreamData.size, m_CompleteImageSize is a signed int in the 2019.4 type
            // tree, so the classic types cap at 2 GB per image.
            if (streamSize > int.MaxValue)
                throw new InvalidOperationException(
                    "texture data exceeds 2 GB; m_CompleteImageSize is a signed 32-bit int"
                );

            w.WriteAlignedString(req.Name); // m_Name
            w.WriteInt32(ForcedFallbackFormat); // m_ForcedFallbackFormat
            w.WriteBool(false); // m_DownscaleFallback
            w.Align(4);
            w.WriteInt32(req.Width); // m_Width
            w.WriteInt32(req.Height); // m_Height
            // The size of one image (a single face's full mip chain). Unity reads
            // m_ImageCount * m_CompleteImageSize from the stream; for a 2D texture m_ImageCount is 1.
            w.WriteInt32((int)streamSize); // m_CompleteImageSize
            w.WriteInt32(req.Format); // m_TextureFormat
            w.WriteInt32(req.MipCount); // m_MipCount
            w.WriteBool(req.Readable); // m_IsReadable
            w.WriteBool(false); // m_IgnoreMasterTextureLimit
            w.WriteBool(false); // m_IsPreProcessed
            w.WriteBool(false); // m_StreamingMipmaps
            w.Align(4);
            w.WriteInt32(0); // m_StreamingMipmapsPriority
            w.Align(4);
            w.WriteInt32(1); // m_ImageCount
            w.WriteInt32(TextureDimensionTex2D); // m_TextureDimension
            WriteTextureSettings(w); // m_TextureSettings
            w.WriteInt32(0); // m_LightmapFormat
            w.WriteInt32(req.ColorSpace); // m_ColorSpace
            WriteEmptyImageData(w); // image data
            WriteStreamData(w, streamOffset, streamSize, streamPath); // m_StreamData
        }

        static void WriteTextureSettings(BundleBufferWriter w)
        {
            w.WriteInt32(1); // m_FilterMode (bilinear)
            w.WriteInt32(1); // m_Aniso
            w.WriteSingle(0f); // m_MipBias
            w.WriteInt32(0); // m_WrapU (repeat)
            w.WriteInt32(0); // m_WrapV
            w.WriteInt32(0); // m_WrapW
        }

        // The inline pixel array is always empty (pixels are streamed), but the node still carries
        // a count prefix and the align flag.
        static void WriteEmptyImageData(BundleBufferWriter w) => w.BeginArray().End(align: true);

        // offset and size are unsigned ints; the 4 GB bound is checked in Build.
        static void WriteStreamData(
            BundleBufferWriter w,
            long streamOffset,
            long streamSize,
            string streamPath
        )
        {
            w.WriteUInt32((uint)streamOffset); // offset
            w.WriteUInt32((uint)streamSize); // size
            w.WriteAlignedString(streamPath); // path
        }

        static void WriteAssetBundleBody(BundleBufferWriter w, string identity, long texturePathId)
        {
            w.WriteAlignedString(identity); // m_Name

            // m_PreloadTable is what LoadAssetAsync actually loads during its asynchronous phase:
            // the preload thread reads the objects listed for the requested asset, including their
            // streamed data. Without an entry the request completes having loaded nothing, and the
            // first access to its `asset` property then performs the entire load (pixel read +
            // upload) synchronously on the main thread.
            var preload = w.BeginArray();
            preload.Add();
            WritePPtr(w, fileId: 0, pathId: texturePathId);
            preload.End(align: true);

            // m_Container: map<string, AssetInfo>. Its Array is not aligned.
            var container = w.BeginArray();
            container.Add();
            w.WriteAlignedString(ContainerKey); // pair.first
            WriteAssetInfo(w, preloadIndex: 0, preloadSize: 1, fileId: 0, pathId: texturePathId);
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

        // The multi-texture variant of WriteAssetBundleBody: the preload table and container each
        // carry one entry per texture, and each container entry references its own single-entry
        // preload slice so LoadAllAssetsAsync streams every texture.
        static void WriteAssetBundleBodyMany(
            BundleBufferWriter w,
            string identity,
            IReadOnlyList<TextureEntry> entries
        )
        {
            int n = entries.Count;

            w.WriteAlignedString(identity); // m_Name

            // m_PreloadTable: one PPtr per texture.
            var preload = w.BeginArray();
            for (int i = 0; i < n; ++i)
            {
                preload.Add();
                WritePPtr(w, fileId: 0, pathId: FirstTexturePathId + i);
            }
            preload.End(align: true);

            // m_Container: map<string, AssetInfo>. Its Array is not aligned. Each texture is keyed by
            // its entry name and points at its own single-entry preload slice.
            var container = w.BeginArray();
            for (int i = 0; i < n; ++i)
            {
                container.Add();
                w.WriteAlignedString(entries[i].Name); // pair.first
                WriteAssetInfo(
                    w, preloadIndex: i, preloadSize: 1, fileId: 0, pathId: FirstTexturePathId + i);
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
