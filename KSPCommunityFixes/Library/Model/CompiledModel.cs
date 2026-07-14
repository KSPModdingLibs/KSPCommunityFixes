namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// The fully compiled, main-thread-ready representation of a single <c>.mu</c> model file, produced
    /// by the background model compiler (a later task) as a faithful split of what
    /// <see cref="KSPCommunityFixes.Library.MuParser"/> does in one pass. It carries no
    /// <c>UnityEngine.Object</c>: the meshes live as serialization-ready <see cref="MeshBlob"/>s (baked
    /// into an <c>AssetBundle</c> off-thread) and the GameObject hierarchy lives as a flat
    /// <see cref="Instructions"/> list.
    /// <para>
    /// The main-thread driver allocates <c>UnityEngine.Object[] locals</c> of size
    /// <see cref="LocalCount"/>, uses <see cref="Bindings"/> to place each loaded <c>Mesh</c> into its
    /// slot, then calls <c>Execute(locals)</c> on every instruction in order. <c>locals[0]</c> ends up
    /// holding the root <c>GameObject</c> (the equivalent of <c>MuParser.Parse</c>'s return value).
    /// </para>
    /// </summary>
    internal sealed class CompiledModel
    {
        /// <summary>The ordered assembly steps. Replayed front-to-back on the main thread; each reads
        /// and/or writes <c>locals</c> by integer slot index (<c>-1</c> meaning "none").</summary>
        public IModelInstruction[] Instructions;

        /// <summary>Serialization-ready meshes referenced by this model (static meshes only in v1).</summary>
        public MeshBlob[] Blobs;

        /// <summary>Maps each mesh's <c>locals</c> slot to the canonical name the driver looks it up by
        /// in the loaded mesh <c>AssetBundle</c>.</summary>
        public MeshBinding[] Bindings;

        /// <summary>Size of the <c>locals[]</c> array the driver must allocate (high-water slot + 1).</summary>
        public int LocalCount;

        /// <summary>The source <c>file.url</c> — used as the root name, for logging and for registration.</summary>
        public string SourceUrl;

        /// <summary>True when the model contains a skinned mesh renderer. The v1 pipeline falls back to
        /// the synchronous <see cref="KSPCommunityFixes.Library.MuParser"/> for these models rather than
        /// baking skinned meshes.</summary>
        public bool ContainsSkinnedMesh;

        /// <summary>True when compilation failed; <see cref="Instructions"/>/<see cref="Blobs"/> are then
        /// meaningless and the pipeline must fall back.</summary>
        public bool Failed;

        /// <summary>Human-readable reason set when <see cref="Failed"/> is true.</summary>
        public string FailureMessage;
    }
}
