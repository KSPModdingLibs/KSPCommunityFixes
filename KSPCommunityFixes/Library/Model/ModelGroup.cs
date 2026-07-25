using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.Library.Model;

/// <summary>
/// A contiguous, ordered span of model load requests whose static meshes are baked into
/// a single asset bundle.
/// </summary>
internal sealed class ModelGroup
{
    /// <summary>The baked mesh-bundle bytes, or null when the group contains no static meshes.</summary>
    public byte[] BundleBytes;

    public Task<Dictionary<string, UnityEngine.Object>> Index;

    public AssetBundle Bundle;

    /// <summary>ALL requests in this group's <c>modelAssets</c> span, in order.</summary>
    public List<ModelLoadRequest> Requests;
}
