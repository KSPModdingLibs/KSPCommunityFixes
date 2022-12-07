using System.Runtime.CompilerServices;

namespace KSPCommunityFixes.Library.Buffers
{
    internal static class Utilities
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int SelectBucketIndex(int bufferSize)
        {
            uint num = (uint)(bufferSize - 1) >> 4;
            int num2 = 0;
            if (num > 65535)
            {
                num >>= 16;
                num2 = 16;
            }
            if (num > 255)
            {
                num >>= 8;
                num2 += 8;
            }
            if (num > 15)
            {
                num >>= 4;
                num2 += 4;
            }
            if (num > 3)
            {
                num >>= 2;
                num2 += 2;
            }
            if (num > 1)
            {
                num >>= 1;
                num2++;
            }
            return num2 + (int)num;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetMaxSizeForBucket(int binIndex)
        {
            return 16 << binIndex;
        }
    }

}

