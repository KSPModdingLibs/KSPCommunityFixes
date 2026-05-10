namespace KSPCommunityFixes.Library
{
    internal static class HashCode
    {
        // Source: https://github.com/dotnet/coreclr/blob/456afea9fbe721e57986a21eb3b4bb1c9c7e4c56/src/System.Private.CoreLib/shared/System/Numerics/Hashing/HashHelpers.cs#L11-L17
        internal static int Combine(int h1, int h2)
        {
            uint rol5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
            return ((int)rol5 + h1) ^ h2;
        }

        internal static int Combine(int h1, int h2, int h3) => Combine(Combine(h1, h2), h3);
    }
}
