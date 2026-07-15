using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model;

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

    public Task<Dictionary<string, UnityEngine.Object>> Index;

    public AssetBundle Bundle;

    /// <summary>ALL requests in this group's <c>modelAssets</c> span, in order.</summary>
    public List<ModelLoadRequest> Requests;
}
