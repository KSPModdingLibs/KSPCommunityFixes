namespace KSPCommunityFixes.Library.TextureBundle
{
    /// <summary>
    /// The Unity runtime class ids the texture-bundle writer can emit into a generated bundle. The
    /// serialized field layouts these ids map to are not transcribed here: the writer copies each
    /// class's type tree verbatim from the embedded reference artifact (see
    /// <see cref="ReferenceTypeTrees"/>).
    /// </summary>
    internal static class SerializedTypeTrees
    {
        public const int Texture2DClassId = 28;
        public const int MeshClassId = 43;
        public const int AssetBundleClassId = 142;
    }
}
