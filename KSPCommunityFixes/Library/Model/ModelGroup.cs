using System.Collections.Generic;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model
{
    /// <summary>
    /// A contiguous, ordered span of model load requests whose static meshes are baked into ONE mesh
    /// <c>AssetBundle</c>. The background compile task folds the ordered compile results into count-capped
    /// groups (so no single <see cref="AssetBundle.LoadFromMemoryAsync"/> becomes huge and native bundle
    /// copies drain often), then the main-thread pump loads each group's bundle and forwards its requests
    /// to the driver. Every request in <see cref="Requests"/> (compiled/skinned/dae/failed) rides the span
    /// in <c>modelAssets</c> order so registration preserves stock load order.
    /// </summary>
    internal sealed class ModelGroup
    {
        /// <summary>The baked mesh-bundle bytes (Phase-2 output). Null when the group contains no static
        /// meshes at all. The main-thread pump nulls this the moment it kicks off
        /// <see cref="AssetBundle.LoadFromMemoryAsync"/> so the managed copy can be collected.</summary>
        public byte[] BundleBytes;

        /// <summary>The in-flight bundle load, set by the main-thread pump. Null until the pump processes
        /// this group (and stays null when <see cref="BundleBytes"/> was null — a mesh-less group).</summary>
        public AssetBundleCreateRequest CreateRequest;

        /// <summary>The native bundle byte count, captured by the pump from <see cref="BundleBytes"/> right
        /// before it nulls that managed copy. Feeds the pump's resident-memory backpressure accounting: the
        /// pump adds it when it kicks off the load and the driver subtracts it on <c>Unload</c>. Stays 0 for a
        /// mesh-less (bundle-less) group, so subtracting it is a harmless no-op.</summary>
        public long BundleSize;

        /// <summary>ALL requests in this group's <c>modelAssets</c> span, in order.</summary>
        public List<ModelLoadRequest> Requests;

        /// <summary>Number of <see cref="ModelLoadRequest.Kind.CompiledMu"/> requests in the group.
        /// Decremented in <c>InsertReadyModel</c> as each registers; when it reaches 0 the group's
        /// bundle is <c>Unload(false)</c>ed (freeing the native copy, keeping the meshes the built
        /// GameObjects now own).</summary>
        public int PendingBundleRefs;
    }
}
