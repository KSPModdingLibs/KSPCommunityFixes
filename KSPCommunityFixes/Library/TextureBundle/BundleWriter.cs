using System;

namespace KSPCommunityFixes.Library.TextureBundle
{
    /// <summary>
    /// Writes the framing of a <c>UnityFS</c> bundle wrapping a single serialized file (the
    /// <c>CAB-&lt;hash&gt;</c> entry) and its streamed resource (<c>CAB-&lt;hash&gt;.resS</c>)
    /// directly into a shared <see cref="BundleBufferWriter"/>: the container header and the
    /// blocks-info directory. The serialized file is written straight after (by
    /// <see cref="SerializedFileWriter"/>) into the same buffer. For textures streamed from an
    /// existing DDS file on disk the resS is empty (its pixel bytes live in the DDS file, which
    /// Unity opens itself). Sizes that depend on the serialized file length are back-patched by
    /// <see cref="Finish"/>.
    /// </summary>
    /// <remarks>
    /// The layout matches what Unity 2019.4.18f1 itself produces, except that the bundle is left
    /// uncompressed: it is loaded once and discarded, so compressing it would only add work.
    ///
    /// <para>Borrowed from KSPTextureLoader
    /// (../KSPTextureLoader/src/KSPTextureLoader/Format/Bundle/BundleWriter.cs).</para>
    /// </remarks>
    internal static class BundleWriter
    {
        const string Signature = "UnityFS";
        const uint FormatVersion = 7;
        const string PlayerMinVersion = "5.x.x";
        const string EngineRevision = "2019.4.18f1";

        // Low 6 bits are the blocks-info compression type (0 == none); 0x40 is
        // kArchiveBlocksAndDirectoryInfoCombined, set by real 2019.4 bundles.
        const uint BundleFlags = 0x40;

        // Marks a directory node as a serialized file.
        const uint SerializedFileNodeFlag = 0x4;

        /// <summary>
        /// The reserved header/blocks-info slots that depend on the serialized file length,
        /// back-patched by <see cref="Finish"/>.
        /// </summary>
        public readonly struct PrefixScope
        {
            internal readonly int TotalSizePosition;
            internal readonly int BlockUncompressedPosition;
            internal readonly int BlockCompressedPosition;
            internal readonly int Node0SizePosition;
            internal readonly int Node1OffsetPosition;
            internal readonly long ResSLength;

            internal PrefixScope(
                int totalSizePosition,
                int blockUncompressedPosition,
                int blockCompressedPosition,
                int node0SizePosition,
                int node1OffsetPosition,
                long resSLength
            )
            {
                TotalSizePosition = totalSizePosition;
                BlockUncompressedPosition = blockUncompressedPosition;
                BlockCompressedPosition = blockCompressedPosition;
                Node0SizePosition = node0SizePosition;
                Node1OffsetPosition = node1OffsetPosition;
                ResSLength = resSLength;
            }
        }

        /// <summary>
        /// Write the container header and blocks-info directory into <paramref name="w"/> (which
        /// must be empty). <paramref name="cab"/> is the internal archive name; the resource node
        /// is named "<paramref name="cab"/>.resS" to match the <c>m_StreamData.path</c> written
        /// into the texture object. <paramref name="resSLength"/> is the length of the streamed
        /// pixel data appended after the prefix (0 when streaming from an external file).
        /// </summary>
        public static PrefixScope WriteHeaderAndBlocksInfo(
            BundleBufferWriter w,
            string cab,
            long resSLength
        )
        {
            if (string.IsNullOrEmpty(cab))
                throw new ArgumentException("cab name is required", nameof(cab));
            if (resSLength < 0)
                throw new ArgumentOutOfRangeException(nameof(resSLength));

            w.AlignBase = 0;
            w.BigEndian = true;

            w.WriteCString(Signature);
            w.WriteUInt32(FormatVersion);
            w.WriteCString(PlayerMinVersion);
            w.WriteCString(EngineRevision);

            int totalSizePosition = w.ReserveInt64(); // total bundle size, patched once known
            int blocksInfoCompPosition = w.ReserveUInt32(); // compressed blocks-info size
            int blocksInfoUncompPosition = w.ReserveUInt32(); // uncompressed blocks-info size
            w.WriteUInt32(BundleFlags);

            // Bundle version 7 pads the header to a 16-byte boundary.
            w.Align(16);

            int blocksInfoStart = w.Length;

            w.WriteZeros(16); // uncompressed data hash

            // A single uncompressed block spanning the whole virtual file system; the block sizes
            // span the serialized file plus the resS and are patched in Finish.
            w.WriteInt32(1);
            int blockUncompressedPosition = w.ReserveUInt32();
            int blockCompressedPosition = w.ReserveUInt32();
            w.WriteUInt16(0); // compression type 0 == none

            // Two directory nodes: the serialized file, then its resS right after.
            w.WriteInt32(2);

            w.WriteInt64(0); // node 0 offset within the block data
            int node0SizePosition = w.ReserveInt64(); // serialized file size
            w.WriteUInt32(SerializedFileNodeFlag);
            w.WriteCString(cab);

            int node1OffsetPosition = w.ReserveInt64(); // resS offset == serialized file size
            w.WriteInt64(resSLength);
            w.WriteUInt32(0);
            w.WriteCString(cab + ".resS");

            int blocksInfoLength = w.Length - blocksInfoStart;
            w.PatchUInt32(blocksInfoCompPosition, (uint)blocksInfoLength, bigEndian: true);
            w.PatchUInt32(blocksInfoUncompPosition, (uint)blocksInfoLength, bigEndian: true);

            return new PrefixScope(
                totalSizePosition,
                blockUncompressedPosition,
                blockCompressedPosition,
                node0SizePosition,
                node1OffsetPosition,
                resSLength
            );
        }

        /// <summary>
        /// Back-patch every size that depends on the serialized file length, plus the total bundle
        /// size. Call once the serialized file has been written into the same buffer.
        /// </summary>
        public static void Finish(BundleBufferWriter w, in PrefixScope scope, long serializedFileLength)
        {
            // The blocks-info block sizes are unsigned 32-bit, and everything lives in a single
            // uncompressed block.
            long blockDataSize = serializedFileLength + scope.ResSLength;
            if (blockDataSize > uint.MaxValue)
                throw new InvalidOperationException(
                    "texture data is too large to fit in a streamed bundle"
                );

            w.PatchUInt32(scope.BlockUncompressedPosition, (uint)blockDataSize, bigEndian: true);
            w.PatchUInt32(scope.BlockCompressedPosition, (uint)blockDataSize, bigEndian: true);
            w.PatchInt64(scope.Node0SizePosition, serializedFileLength, bigEndian: true);
            w.PatchInt64(scope.Node1OffsetPosition, serializedFileLength, bigEndian: true);
            w.PatchInt64(scope.TotalSizePosition, w.Length + scope.ResSLength, bigEndian: true);
        }
    }
}
