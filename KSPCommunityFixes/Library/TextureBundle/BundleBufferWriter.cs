using System;
using System.Text;

namespace KSPCommunityFixes.Library.TextureBundle
{
    /// <summary>
    /// A single growable byte buffer that a bundle prefix is written into, with primitive
    /// writers, deferred-length helpers and back-patching. It is the allocation-light write
    /// path: one buffer grown in place, values encoded straight into it (strings included,
    /// with no intermediate arrays), and a single right-sized copy at the end via
    /// <see cref="ToArray"/>.
    /// </summary>
    /// <remarks>
    /// Both little-endian and big-endian values are supported through <see cref="BigEndian"/>:
    /// the UnityFS container header and the serialized-file header are big-endian, while
    /// serialized metadata and object data are little-endian.
    ///
    /// <para><see cref="AlignBase"/> makes <see cref="Align"/> pad relative to a chosen origin
    /// rather than the buffer start. The serialized file is written into this same buffer at a
    /// non-16-aligned offset, but Unity aligns object data relative to the serialized file's own
    /// start; pointing <see cref="AlignBase"/> at that start keeps the padding identical to what
    /// a file written from offset zero would have.</para>
    ///
    /// <para>Borrowed from KSPTextureLoader
    /// (../KSPTextureLoader/src/KSPTextureLoader/Format/Bundle/BundleBufferWriter.cs).</para>
    /// </remarks>
    internal sealed class BundleBufferWriter
    {
        byte[] buffer;
        int length;

        /// <summary>When <c>true</c> multi-byte integers are written most-significant-byte first.</summary>
        public bool BigEndian;

        /// <summary>The origin <see cref="Align"/> pads relative to (see the type remarks).</summary>
        public int AlignBase;

        public BundleBufferWriter(int initialCapacity = 256)
        {
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            buffer = new byte[Math.Max(initialCapacity, 64)];
        }

        /// <summary>The number of bytes written so far (the current write position).</summary>
        public int Length => length;

        void EnsureCapacity(int additional)
        {
            long required = (long)length + additional;
            if (required <= buffer.Length)
                return;

            long capacity = buffer.Length;
            while (capacity < required)
                capacity *= 2;
            if (capacity > int.MaxValue)
                capacity = int.MaxValue;

            Array.Resize(ref buffer, (int)capacity);
        }

        void WriteUInt(ulong value, int size)
        {
            EnsureCapacity(size);
            if (BigEndian)
            {
                for (int i = 0; i < size; ++i)
                    buffer[length + i] = (byte)(value >> (8 * (size - 1 - i)));
            }
            else
            {
                for (int i = 0; i < size; ++i)
                    buffer[length + i] = (byte)(value >> (8 * i));
            }
            length += size;
        }

        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            buffer[length++] = value;
        }

        public void WriteBool(bool value) => WriteByte((byte)(value ? 1 : 0));

        public void WriteUInt16(ushort value) => WriteUInt(value, 2);

        public void WriteInt32(int value) => WriteUInt((uint)value, 4);

        public void WriteUInt32(uint value) => WriteUInt(value, 4);

        public void WriteInt64(long value) => WriteUInt((ulong)value, 8);

        public unsafe void WriteSingle(float value) => WriteUInt32(*(uint*)&value);

        public void WriteBytes(byte[] value)
        {
            if (value is null || value.Length == 0)
                return;
            EnsureCapacity(value.Length);
            Buffer.BlockCopy(value, 0, buffer, length, value.Length);
            length += value.Length;
        }

        /// <summary>Write <paramref name="count"/> zero bytes.</summary>
        public void WriteZeros(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            EnsureCapacity(count);
            Array.Clear(buffer, length, count);
            length += count;
        }

        /// <summary>
        /// Pad with zero bytes until the write position is a multiple of
        /// <paramref name="alignment"/> relative to <see cref="AlignBase"/>.
        /// </summary>
        public void Align(int alignment = 4)
        {
            int rem = (length - AlignBase) % alignment;
            if (rem != 0)
                WriteZeros(alignment - rem);
        }

        /// <summary>
        /// Write an Int32 length-prefixed UTF-8 string and align to 4 bytes afterwards, the way
        /// strings are stored inside serialized object data. The bytes are encoded straight into
        /// the buffer with no intermediate array.
        /// </summary>
        public void WriteAlignedString(string value)
        {
            int count = Utf8ByteCount(value);
            WriteInt32(count);
            WriteUtf8(value, count);
            Align(4);
        }

        /// <summary>Write a null-terminated UTF-8 string (no intermediate array).</summary>
        public void WriteCString(string value)
        {
            WriteUtf8(value, Utf8ByteCount(value));
            WriteByte(0);
        }

        static int Utf8ByteCount(string value) =>
            string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);

        void WriteUtf8(string value, int count)
        {
            if (count > 0)
            {
                EnsureCapacity(count);
                Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, length);
                length += count;
            }
        }

        /// <summary>Reserve four bytes to be back-patched later, returning their position.</summary>
        public int ReserveUInt32()
        {
            int pos = length;
            WriteZeros(4);
            return pos;
        }

        /// <summary>Reserve eight bytes to be back-patched later, returning their position.</summary>
        public int ReserveInt64()
        {
            int pos = length;
            WriteZeros(8);
            return pos;
        }

        /// <summary>Back-patch four bytes at an earlier position, with the given endianness.</summary>
        public void PatchUInt32(int position, uint value, bool bigEndian)
        {
            if (bigEndian)
                for (int i = 0; i < 4; ++i)
                    buffer[position + 3 - i] = (byte)(value >> (8 * i));
            else
                for (int i = 0; i < 4; ++i)
                    buffer[position + i] = (byte)(value >> (8 * i));
        }

        /// <summary>Back-patch eight bytes at an earlier position, with the given endianness.</summary>
        public void PatchInt64(int position, long value, bool bigEndian)
        {
            ulong v = (ulong)value;
            if (bigEndian)
                for (int i = 0; i < 8; ++i)
                    buffer[position + 7 - i] = (byte)(v >> (8 * i));
            else
                for (int i = 0; i < 8; ++i)
                    buffer[position + i] = (byte)(v >> (8 * i));
        }

        /// <summary>Begin a length-prefixed array whose element count is filled in on <see cref="ArrayScope.End"/>.</summary>
        public ArrayScope BeginArray() => new ArrayScope(this);

        /// <summary>
        /// A length-prefixed array wrapper: reserves the Int32 count up front, tallies elements
        /// as they are written and back-patches the count when the scope ends.
        /// </summary>
        public struct ArrayScope
        {
            readonly BundleBufferWriter writer;
            readonly int countPosition;
            int count;

            internal ArrayScope(BundleBufferWriter writer)
            {
                this.writer = writer;
                countPosition = writer.ReserveUInt32();
                count = 0;
            }

            /// <summary>Record that one element is about to be written.</summary>
            public void Add() => count++;

            /// <summary>Patch the element count, optionally aligning the writer to 4 bytes after.</summary>
            public void End(bool align = false)
            {
                writer.PatchUInt32(countPosition, (uint)count, bigEndian: false);
                if (align)
                    writer.Align(4);
            }
        }

        /// <summary>Copy the written bytes into a new, exactly-sized array.</summary>
        public byte[] ToArray()
        {
            var result = new byte[length];
            Buffer.BlockCopy(buffer, 0, result, 0, length);
            return result;
        }
    }
}
