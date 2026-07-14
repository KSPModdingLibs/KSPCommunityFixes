using System;

namespace KSPCommunityFixes.Library.TextureBundle
{
    /// <summary>
    /// Writes the framing of a serialized Unity asset-bundle file (the <c>CAB-&lt;hash&gt;</c>
    /// entry) directly into a shared <see cref="BundleBufferWriter"/>: the header, the
    /// little-endian metadata (unity version, type list, object table) and the padding up to the
    /// object-data section. Callers write each object body between
    /// <see cref="FileScope.BeginObject"/> and <see cref="FileScope.EndObject"/>, and the byte
    /// offsets/sizes are back-patched into the reserved object-table slots.
    /// </summary>
    /// <remarks>
    /// The emitted file enables the type tree and copies each class's type entry verbatim from the
    /// embedded reference artifact (see <see cref="ReferenceTypeTrees"/>), so Unity deserializes
    /// objects from the embedded tree. It targets serialized file format 21 (Unity 2019.4), the
    /// version the reference type entries were captured for.
    ///
    /// <para>Borrowed from KSPTextureLoader
    /// (../KSPTextureLoader/src/KSPTextureLoader/Format/Bundle/SerializedFileWriter.cs).</para>
    /// </remarks>
    internal static class SerializedFileWriter
    {
        // 2019.4 writes serialized-file format 21. The reference type entries copied into the
        // metadata were captured from a format-21 file, so this must match.
        const uint FormatVersion = 21;

        const int ObjectAlignment = 16;

        // metadataSize + fileSize + version + dataOffset + endianness byte + 3 reserved.
        const int HeaderLength = 20;

        /// <summary>Identifies one object to place in the serialized file.</summary>
        public readonly struct ObjectMeta
        {
            public readonly long PathId;
            public readonly int ClassId;

            public ObjectMeta(long pathId, int classId)
            {
                PathId = pathId;
                ClassId = classId;
            }
        }

        /// <summary>The reserved object-table slots for one object, patched as its body is written.</summary>
        public struct ObjectSlot
        {
            internal int OffsetPosition;
            internal int SizePosition;
            internal int Start;
        }

        /// <summary>
        /// Bookkeeping returned by <see cref="BeginFile"/>: the reserved header slots and the
        /// object-data section origin, used to place object bodies and finalize the file.
        /// </summary>
        public readonly struct FileScope
        {
            internal readonly int FileStart;
            internal readonly int FileSizePosition;
            internal readonly int DataOffset;

            internal FileScope(int fileStart, int fileSizePosition, int dataOffset)
            {
                FileStart = fileStart;
                FileSizePosition = fileSizePosition;
                DataOffset = dataOffset;
            }

            /// <summary>Align to the next object slot and record where this object's body begins.</summary>
            public void BeginObject(BundleBufferWriter w, ref ObjectSlot slot)
            {
                w.Align(ObjectAlignment);
                slot.Start = w.Length;
                // The object-table offset is relative to the data section (dataOffset).
                w.PatchUInt32(
                    slot.OffsetPosition,
                    (uint)(w.Length - FileStart - DataOffset),
                    bigEndian: false
                );
            }

            /// <summary>Back-patch this object's byte size once its body has been written.</summary>
            public void EndObject(BundleBufferWriter w, in ObjectSlot slot) =>
                w.PatchUInt32(slot.SizePosition, (uint)(w.Length - slot.Start), bigEndian: false);

            /// <summary>Patch the total file size and return it.</summary>
            public long End(BundleBufferWriter w)
            {
                int fileLength = w.Length - FileStart;
                w.PatchUInt32(FileSizePosition, (uint)fileLength, bigEndian: true);
                return fileLength;
            }
        }

        /// <summary>
        /// Write the header, metadata and object-data padding into <paramref name="w"/> at its
        /// current position, reserving one <see cref="ObjectSlot"/> per object in
        /// <paramref name="slots"/>. <paramref name="targetPlatform"/> is the Unity
        /// <c>BuildTarget</c> the bundle is tagged for; it must match the running player or Unity
        /// rejects the bundle at load.
        /// </summary>
        public static FileScope BeginFile(
            BundleBufferWriter w,
            int targetPlatform,
            ReadOnlySpan<ObjectMeta> objects,
            Span<ObjectSlot> slots
        )
        {
            if (objects.Length == 0)
                throw new ArgumentException("at least one object is required", nameof(objects));
            if (slots.Length < objects.Length)
                throw new ArgumentException("slots must have one entry per object", nameof(slots));

            int fileStart = w.Length;
            // Object-data alignment is relative to the serialized file's own start.
            w.AlignBase = fileStart;

            // Header (big-endian), sizes back-patched once known.
            w.BigEndian = true;
            int metaSizePosition = w.ReserveUInt32();
            int fileSizePosition = w.ReserveUInt32();
            w.WriteUInt32(FormatVersion);
            int dataOffsetPosition = w.ReserveUInt32();
            w.WriteByte(0); // m_Endianess: 0 == little-endian data
            w.WriteZeros(3); // reserved

            int metaStart = w.Length; // == fileStart + HeaderLength

            // Metadata (little-endian).
            w.BigEndian = false;
            w.WriteCString(ReferenceTypeTrees.UnityVersion);
            w.WriteInt32(targetPlatform);
            w.WriteBool(true); // m_EnableTypeTree

            // The distinct class ids, in first-seen order, form the type list. Each entry (class
            // id, strip/script fields, hashes, type-tree blob and dependencies) is copied verbatim
            // from the reference artifact so objects carry their full tree. This is sized to
            // objects.Length (which can be tens of thousands for a combined texture bundle), so it
            // lives on the heap rather than the stack.
            Span<int> typeOrder = new int[objects.Length];
            int typeCount = 0;
            for (int i = 0; i < objects.Length; ++i)
                if (IndexOf(typeOrder, typeCount, objects[i].ClassId) < 0)
                    typeOrder[typeCount++] = objects[i].ClassId;

            w.WriteInt32(typeCount);
            for (int i = 0; i < typeCount; ++i)
                w.WriteBytes(ReferenceTypeTrees.TypeEntry(typeOrder[i]));

            // Object table: reserve the byte offset/size of each object, patched as bodies are
            // written.
            w.WriteInt32(objects.Length);
            for (int i = 0; i < objects.Length; ++i)
            {
                w.Align(4);
                w.WriteInt64(objects[i].PathId);
                slots[i].OffsetPosition = w.ReserveUInt32();
                slots[i].SizePosition = w.ReserveUInt32();
                w.WriteInt32(IndexOf(typeOrder, typeCount, objects[i].ClassId));
            }

            w.WriteInt32(0); // m_ScriptTypes count
            w.WriteInt32(0); // m_Externals count
            w.WriteInt32(0); // m_RefTypes count
            w.WriteCString(""); // userInformation

            int metaLength = w.Length - metaStart;
            int dataOffset = Align(HeaderLength + metaLength, ObjectAlignment);

            // Pad up to the object-data section.
            int padding = dataOffset - (w.Length - fileStart);
            if (padding > 0)
                w.WriteZeros(padding);

            w.PatchUInt32(metaSizePosition, (uint)metaLength, bigEndian: true);
            w.PatchUInt32(dataOffsetPosition, (uint)dataOffset, bigEndian: true);

            return new FileScope(fileStart, fileSizePosition, dataOffset);
        }

        static int IndexOf(Span<int> values, int count, int value)
        {
            for (int i = 0; i < count; ++i)
                if (values[i] == value)
                    return i;
            return -1;
        }

        static int Align(int value, int alignment)
        {
            int rem = value % alignment;
            return rem == 0 ? value : value + (alignment - rem);
        }
    }
}
