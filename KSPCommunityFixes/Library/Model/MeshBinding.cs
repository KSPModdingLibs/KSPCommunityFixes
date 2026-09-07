namespace KSPCommunityFixes.Library.Model;

/// <summary>
/// Associates one <c>locals[]</c> slot with the canonical name of the mesh that belongs in it.
/// </summary>
internal readonly struct MeshBinding(int slot, string canonicalName)
{
    /// <summary>Index into <c>locals[]</c> where the loaded mesh is stored.</summary>
    public readonly int Slot = slot;

    /// <summary>The key to use to look up the asset in the bundle.</summary>
    public readonly string CanonicalName = canonicalName;
}
