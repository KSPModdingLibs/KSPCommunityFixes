using System;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// Thread-safe, instance-state extraction of <c>MuParser</c>'s low-level binary reading primitives.
    /// <para>
    /// The stock <c>MuParser</c> keeps its cursor and scratch buffers in static mutable fields, which
    /// makes it usable from only one thread at a time. This struct owns all of that state per-instance
    /// (a base pointer, a length, a read cursor and a lazily grown decode buffer) so many worker
    /// threads can parse different <c>.mu</c> files in parallel. The byte layout, cursor advancement,
    /// bounds checks and string decoding are a faithful, byte-for-byte reproduction of the originals.
    /// </para>
    /// <para>
    /// The caller is responsible for pinning the input <c>byte[]</c> (via a <see cref="System.Runtime.InteropServices.GCHandle"/>
    /// or a <c>fixed</c> statement) and passing the resulting pointer plus valid length. The struct
    /// stores the pointer and never pins on its own, so the buffer must stay pinned for the reader's
    /// whole lifetime. Only plain value types (<see cref="Vector3"/>, <see cref="Quaternion"/>,
    /// <see cref="BoneWeight"/>, <see cref="Matrix4x4"/>, <see cref="Keyframe"/>, ...) are produced;
    /// no <c>UnityEngine.Object</c> is ever created, so every method here is safe off the main thread.
    /// </para>
    /// <para>
    /// IMPORTANT — this is a MUTABLE struct. It carries a live read cursor (<see cref="Position"/>) and
    /// a lazily-allocated decode buffer, so it must be treated like <see cref="System.Text.Json.Utf8JsonReader"/>:
    /// hold it in exactly one local variable or one non-<c>readonly</c> field and mutate that single
    /// instance. Do NOT pass it by value to a helper, store it in a <c>readonly</c> field (which forces a
    /// defensive copy on every access), capture it in a lambda, box it, or iterate it in a <c>foreach</c> —
    /// each of those silently makes a copy whose cursor advances independently, desyncing the read
    /// position with no compiler error. Any helper that must advance the reader has to take it by
    /// <c>ref MuBinaryReader</c>. It stays a struct (rather than a class) deliberately, for the same
    /// allocation/performance reasons as <c>Utf8JsonReader</c>; do not "fix" the mutable-struct footgun
    /// by turning it into a class.
    /// </para>
    /// </summary>
    internal unsafe struct MuBinaryReader
    {
        // MuParser named this field "decoder", but it is a UTF8Encoding (an Encoding), not a
        // System.Text.Decoder. That distinction matters for threading: a System.Text.Decoder carries
        // per-instance state across calls and is NOT thread-safe, whereas Encoding.GetChars is
        // stateless and documented as safe to call from multiple threads on a shared instance. So we
        // keep a single shared static readonly UTF8Encoding and decode straight through it, exactly as
        // the original does, with no per-thread hazard.
        private static readonly UTF8Encoding utf8 = new UTF8Encoding();

        /// <summary>Base pointer to the caller-pinned data buffer.</summary>
        public readonly byte* Ptr;

        /// <summary>Valid length of the data buffer, in bytes (the original <c>dataLength</c>).</summary>
        public readonly int Length;

        /// <summary>Current read cursor, in bytes (replaces the original static <c>index</c>).</summary>
        public int Position;

        // Instance-owned UTF8 decode scratch buffer, lazily allocated and grown by ReadString
        // (replaces the original static charBuffer). Being per-instance is what makes ReadString
        // thread-safe.
        private char[] charBuffer;

        /// <summary>
        /// Create a reader over an already-pinned buffer. Pinning is the caller's responsibility; this
        /// struct only stores the pointer and never pins.
        /// </summary>
        /// <param name="ptr">Base pointer to the pinned data buffer.</param>
        /// <param name="length">Valid length of the buffer in bytes.</param>
        public MuBinaryReader(byte* ptr, int length)
        {
            Ptr = ptr;
            Length = length;
            Position = 0;
            charBuffer = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Advance(int bytes)
        {
            int currentIndex = Position;
            int nextIndex = currentIndex + bytes;
            // The unsigned compare plus the explicit bytes < 0 guard rejects overflow and negative reads
            // from corrupt/malicious data (e.g. a huge or negative 7-bit-encoded length that would wrap
            // nextIndex negative and slip past a plain "> Length" check), while remaining identical to the
            // original bounds behavior for every valid file where 0 <= nextIndex <= Length.
            if (bytes < 0 || (uint)nextIndex > (uint)Length)
                ThrowEndOfDataException();

            Position = nextIndex;
            return currentIndex;
        }

        private static void ThrowEndOfDataException()
        {
            throw new InvalidOperationException("Unable to read beyond the end of the data");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            int valIdx = Advance(1);
            return *(Ptr + valIdx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipInt()
        {
            Advance(4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt()
        {
            int valIdx = Advance(4);
            return *(int*)(Ptr + valIdx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFloat()
        {
            int valIdx = Advance(4);
            return *(float*)(Ptr + valIdx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipBool()
        {
            Advance(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool()
        {
            int valIdx = Advance(1);
            return *(Ptr + valIdx) != 0;
        }

        public void SkipString()
        {
            Advance(Read7BitEncodedInt());
        }

        public string ReadString()
        {
            int strByteLength = Read7BitEncodedInt();

            if (strByteLength < 0)
                throw new Exception("Invalid string length");

            if (strByteLength == 0)
                return string.Empty;

            ExpandCharBuffer(strByteLength);

            // Advance returns the pre-advance cursor, i.e. the start offset of the string bytes (the
            // original captured "index" before advancing). We decode straight from the pinned buffer
            // via the unsafe Encoding.GetChars overload instead of the byte[] overload the original
            // used; the input bytes are identical, so the decoded chars are byte-for-byte the same.
            int start = Advance(strByteLength);
            int charCount;
            fixed (char* charPtr = charBuffer)
                charCount = utf8.GetChars(Ptr + start, strByteLength, charPtr, charBuffer.Length);

            return new string(charBuffer, 0, charCount);
        }

        private void ExpandCharBuffer(int length)
        {
            if (charBuffer == null || charBuffer.Length < length)
                charBuffer = new char[(int)(length * 1.5)];
        }

        // 7-bit-encoded length prefix (the BinaryWriter/BinaryReader string-length format): each byte
        // contributes its low 7 bits, and the high bit flags that another byte follows. After 5 bytes
        // (shift reaches 35) the value can no longer fit in an Int32, which is an error.
        public int Read7BitEncodedInt()
        {
            int num = 0;
            int num2 = 0;
            byte b;
            do
            {
                if (num2 == 35)
                {
                    throw new FormatException("Too many bytes in what should have been a 7 bit encoded Int32.");
                }
                b = ReadByte();
                num |= (b & 0x7F) << num2;
                num2 += 7;
            }
            while ((b & 0x80u) != 0);
            return num;
        }

        public Vector2 ReadVector2()
        {
            int valIdx = Advance(8);
            return *(Vector2*)(Ptr + valIdx);
        }

        public Vector3 ReadVector3()
        {
            int valIdx = Advance(12);
            return *(Vector3*)(Ptr + valIdx);
        }

        public Vector4 ReadVector4()
        {
            int valIdx = Advance(16);
            return *(Vector4*)(Ptr + valIdx);
        }

        public Quaternion ReadQuaternion()
        {
            int valIdx = Advance(16);
            return *(Quaternion*)(Ptr + valIdx);
        }

        public Color ReadColor()
        {
            int valIdx = Advance(16);
            return *(Color*)(Ptr + valIdx);
        }

        public Color32 ReadColor32()
        {
            int valIdx = Advance(4);
            return *(Color32*)(Ptr + valIdx);
        }

        public BoneWeight ReadBoneWeight()
        {
            int valIdx = Advance(32);
            // data isn't packed with the same layout as the struct, so we fallback to setting every field
            return new BoneWeight()
            {
                boneIndex0 = *(int*)(Ptr + valIdx),
                weight0 =  *(float*)(Ptr + valIdx + 4),
                boneIndex1 = *(int*)(Ptr + valIdx + 8),
                weight1 =  *(float*)(Ptr + valIdx + 12),
                boneIndex2 = *(int*)(Ptr + valIdx + 16),
                weight2 =  *(float*)(Ptr + valIdx + 20),
                boneIndex3 = *(int*)(Ptr + valIdx + 24),
                weight3 =  *(float*)(Ptr + valIdx + 28)
            };
        }

        public Matrix4x4 ReadMatrix4x4()
        {
            int valIdx = Advance(64);
            // data isn't packed with the same layout as the struct, so we fallback to setting every field
            return new Matrix4x4()
            {
                m00 = *(float*)(Ptr + valIdx),
                m01 = *(float*)(Ptr + valIdx + 4),
                m02 = *(float*)(Ptr + valIdx + 8),
                m03 = *(float*)(Ptr + valIdx + 12),
                m10 = *(float*)(Ptr + valIdx + 16),
                m11 = *(float*)(Ptr + valIdx + 20),
                m12 = *(float*)(Ptr + valIdx + 24),
                m13 = *(float*)(Ptr + valIdx + 28),
                m20 = *(float*)(Ptr + valIdx + 32),
                m21 = *(float*)(Ptr + valIdx + 36),
                m22 = *(float*)(Ptr + valIdx + 40),
                m23 = *(float*)(Ptr + valIdx + 44),
                m30 = *(float*)(Ptr + valIdx + 48),
                m31 = *(float*)(Ptr + valIdx + 52),
                m32 = *(float*)(Ptr + valIdx + 56),
                m33 = *(float*)(Ptr + valIdx + 60)
            };
        }

        public Keyframe ReadKeyFrame()
        {
            // this is encoded as 4 floats (16 bytes), but there is 4 bytes of padding at the end
            int valIdx = Advance(20);
            return new Keyframe(
                *(float*)(Ptr + valIdx),
                *(float*)(Ptr + valIdx + 4),
                *(float*)(Ptr + valIdx + 8),
                *(float*)(Ptr + valIdx + 12));
        }

        // The Fill* helpers bulk-copy N sequentially packed elements straight from the pinned data
        // buffer into a caller-owned destination array. This mirrors MuParser exactly: the mu mesh
        // data is laid out as a contiguous run of the element structs, so we Advance past the run and
        // memcpy it in one shot rather than reading element by element. Unlike MuParser these do NOT
        // own or grow the destination — the caller supplies an array with room for at least
        // <paramref name="count"/> elements (the buffer growth that MuParser did internally now lives
        // with whoever owns the buffers).
        //
        // The 3rd argument to Buffer.MemoryCopy is destinationSizeInBytes — the ONLY overflow guard — so
        // it must be the destination's TRUE byte capacity (real array length * element size), never the
        // copy size. Passing the copy size would defeat the guard and let a too-small destination be
        // overrun as a silent out-of-bounds heap write; passing the real capacity makes MemoryCopy throw
        // ArgumentOutOfRangeException instead.

        public void FillIntBuffer(int[] destination, int intCount)
        {
            int byteCount = intCount * 4;
            int valIdx = Advance(byteCount);

            fixed (int* intBufferPtr = destination)
                // 3rd arg is the destination's real byte capacity, so the copy is bounds-checked.
                Buffer.MemoryCopy(Ptr + valIdx, intBufferPtr, (long)destination.Length * 4, byteCount);
        }

        public void FillVector2Buffer(Vector2[] destination, int vector2Count)
        {
            int byteCount = vector2Count * 8;
            int valIdx = Advance(byteCount);

            fixed (Vector2* vector2BufferPtr = destination)
                // 3rd arg is the destination's real byte capacity, so the copy is bounds-checked.
                Buffer.MemoryCopy(Ptr + valIdx, vector2BufferPtr, (long)destination.Length * 8, byteCount);
        }

        public void FillVector3Buffer(Vector3[] destination, int vector3Count)
        {
            int byteCount = vector3Count * 12;
            int valIdx = Advance(byteCount);

            fixed (Vector3* vector3BufferPtr = destination)
                // 3rd arg is the destination's real byte capacity, so the copy is bounds-checked.
                Buffer.MemoryCopy(Ptr + valIdx, vector3BufferPtr, (long)destination.Length * 12, byteCount);
        }

        public void FillVector4Buffer(Vector4[] destination, int vector4Count)
        {
            int byteCount = vector4Count * 16;
            int valIdx = Advance(byteCount);

            fixed (Vector4* vector4BufferPtr = destination)
                // 3rd arg is the destination's real byte capacity, so the copy is bounds-checked.
                Buffer.MemoryCopy(Ptr + valIdx, vector4BufferPtr, (long)destination.Length * 16, byteCount);
        }

        public void FillColor32Buffer(Color32[] destination, int color32Count)
        {
            int byteCount = color32Count * 4;
            int valIdx = Advance(byteCount);

            fixed (Color32* color32BufferPtr = destination)
                // 3rd arg is the destination's real byte capacity, so the copy is bounds-checked.
                Buffer.MemoryCopy(Ptr + valIdx, color32BufferPtr, (long)destination.Length * 4, byteCount);
        }
    }
}
