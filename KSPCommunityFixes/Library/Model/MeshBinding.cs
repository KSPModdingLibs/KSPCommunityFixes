namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// Associates one <c>locals[]</c> slot with the canonical name of the mesh that belongs in it.
    /// Before running a <see cref="CompiledModel"/>'s instructions, the main-thread driver — for every
    /// binding — issues <c>bundle.LoadAssetAsync&lt;UnityEngine.Mesh&gt;(CanonicalName)</c> against the
    /// loaded mesh <c>AssetBundle</c> and stores the resulting <c>Mesh</c> into <c>locals[Slot]</c>, so
    /// that the <c>AddMeshFilter</c>/<c>AddMeshCollider</c>/<c>AddSkinnedMeshRenderer</c> instructions
    /// can read their mesh straight out of that slot.
    /// </summary>
    /// <remarks>
    /// <see cref="CanonicalName"/> is <c>MeshBundleBuilder.Canonicalize($"{file.url}#{meshIndex}")</c>,
    /// i.e. the exact verbatim key stored in the bundle's container (lowercased, forward slashes) — see
    /// <see cref="MeshBundleBuilder.Canonicalize"/>.
    /// </remarks>
    internal readonly struct MeshBinding
    {
        /// <summary>Index into <c>locals[]</c> where the loaded mesh is stored.</summary>
        public readonly int Slot;

        /// <summary>The bundle container lookup key (already canonicalized).</summary>
        public readonly string CanonicalName;

        public MeshBinding(int slot, string canonicalName)
        {
            Slot = slot;
            CanonicalName = canonicalName;
        }
    }
}
