using System;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model;

/// <summary>
/// Binary reader for .mu files.
/// </summary>
internal unsafe struct MuBinaryReader
{
    private static readonly UTF8Encoding utf8 = new();

    /// <summary>Base pointer to the caller-pinned data buffer.</summary>
    public readonly byte* Ptr;

    /// <summary>Valid length of the data buffer, in bytes.</summary>
    public readonly int Length;

    /// <summary>Current read cursor, in bytes.</summary>
    public int Position;

    // Per-instance UTF8 decode scratch buffer, lazily allocated and grown by ReadString. Being
    // per-instance rather than static is what makes ReadString thread-safe.
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
        // nextIndex negative and slip past a plain "> Length" check).
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

    // The BinaryWriter/BinaryReader 7-bit-encoded length-prefix format.
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

    // The Fill* helpers bulk-copy N contiguously packed element structs straight from the pinned
    // data buffer into a caller-owned destination array in one memcpy, rather than reading element
    // by element. They do NOT grow the destination: the caller must supply an array with room for
    // at least the requested element count.
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
            Buffer.MemoryCopy(Ptr + valIdx, intBufferPtr, (long)destination.Length * 4, byteCount);
    }

    public void FillVector2Buffer(Vector2[] destination, int vector2Count)
    {
        int byteCount = vector2Count * 8;
        int valIdx = Advance(byteCount);

        fixed (Vector2* vector2BufferPtr = destination)
            Buffer.MemoryCopy(Ptr + valIdx, vector2BufferPtr, (long)destination.Length * 8, byteCount);
    }

    public void FillVector3Buffer(Vector3[] destination, int vector3Count)
    {
        int byteCount = vector3Count * 12;
        int valIdx = Advance(byteCount);

        fixed (Vector3* vector3BufferPtr = destination)
            Buffer.MemoryCopy(Ptr + valIdx, vector3BufferPtr, (long)destination.Length * 12, byteCount);
    }

    public void FillVector4Buffer(Vector4[] destination, int vector4Count)
    {
        int byteCount = vector4Count * 16;
        int valIdx = Advance(byteCount);

        fixed (Vector4* vector4BufferPtr = destination)
            Buffer.MemoryCopy(Ptr + valIdx, vector4BufferPtr, (long)destination.Length * 16, byteCount);
    }

    public void FillColor32Buffer(Color32[] destination, int color32Count)
    {
        int byteCount = color32Count * 4;
        int valIdx = Advance(byteCount);

        fixed (Color32* color32BufferPtr = destination)
            Buffer.MemoryCopy(Ptr + valIdx, color32BufferPtr, (long)destination.Length * 4, byteCount);
    }
}
