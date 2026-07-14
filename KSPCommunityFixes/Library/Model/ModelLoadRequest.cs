using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// The per-file work item + result carrier for the background model pipeline, the model analogue of
    /// FastLoader's <c>TextureLoadRequest</c>. One is produced for every entry in <c>modelAssets</c> and
    /// flows compile task -> pump -> driver in <c>modelAssets</c> order. <see cref="ModelKind"/> selects
    /// how the main-thread loader builds <see cref="Result"/>.
    /// </summary>
    internal sealed class ModelLoadRequest
    {
        /// <summary>Matches <c>TextureLoadRequest.State</c>: the driver's strict-FIFO drain peeks the head
        /// and waits while it is <see cref="Pending"/>, never reordering.</summary>
        public enum State : byte { Pending, Ready, Failed }

        /// <summary>How the main-thread loader builds the model:
        /// <list type="bullet">
        /// <item><see cref="CompiledMu"/>: replay the compiled instructions against meshes loaded from the
        /// group's bundle.</item>
        /// <item><see cref="Skinned"/>: v1 fallback to the synchronous <c>MuParser.Parse</c> (baking
        /// skinned meshes is deferred), using the retained <see cref="RawBytes"/>.</item>
        /// <item><see cref="Dae"/>: reload via the stock DAE loader.</item>
        /// <item><see cref="Failed"/>: file read failed or compilation failed; hard failure.</item>
        /// </list></summary>
        public enum Kind : byte { CompiledMu, Skinned, Dae, Failed }

        public UrlDir.UrlFile File;
        public Kind ModelKind;

        /// <summary>Carried for CompiledMu/Skinned/Failed (any request produced by the compiler): supplies
        /// Instructions/Bindings/LocalCount for replay and, always, the buffered <c>Logs</c> that
        /// <c>InsertReadyModel</c> flushes on the main thread. <c>Blobs</c> is nulled once its group's
        /// bundle is built. Null for Dae and file-read failures.</summary>
        public CompiledModel Compiled;

        /// <summary>CompiledMu only: back-reference to the owning group for the bundle-load await and the
        /// <c>Unload</c> ref-count.</summary>
        public ModelGroup Group;

        /// <summary>Skinned only: the retained raw <c>.mu</c> bytes handed to <c>MuParser.Parse</c>
        /// (nulled right after the parse).</summary>
        public byte[] RawBytes;
        public int RawLength;

        public string FailureMessage;

        /// <summary>Byte count added to <c>KSPCFFastLoaderReport.modelsBytesLoaded</c> on success.</summary>
        public long FileLength;

        /// <summary>Pending -> Ready/Failed. Volatile like <c>TextureLoadRequest.Status</c> (the loader
        /// coroutine writes it, the driver polls it).</summary>
        public volatile State Status;

        public GameObject Result;
    }
}
