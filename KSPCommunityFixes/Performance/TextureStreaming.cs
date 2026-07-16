using System;
using System.Collections;
using UnityEngine;

namespace KSPCommunityFixes.Performance;

internal class TextureStreaming : BasePatch
{
    /// <summary>
    /// Whether mipmap streaming is enabled.
    /// </summary>
    internal static bool MipmapStreamingEnabled = false;

    // The minimum amount of memory reserved for streaming, in megabytes.
    internal static int MipmapStreamingBudgetMb = 1024;

    // The maximum number of mipmap levels that we allow streaing to unload.
    //
    // Note that this also affects which textures are eligible for mipmap streaming.
    // Unity will crash if it attempts to load a compressed texture whose size is
    // not a multiple of the block size, so the texture loader will disable streaming
    // for any texture whose mipmaps match that.
    //
    // The value of 3 is meant to be a balance between memory improvements (64x less
    // memory for a fully streamed out texture) and allowing more textures to be
    // streamed.
    internal const int MaxStreamingMipReduction = 3;

    internal static string LOC_StreamingEnabledTitle = "Texture streaming";
    internal static string LOC_StreamingEnabledTooltip =
        "Stream part textures from files on disk as they are needed. "
        + "Ensures VRAM usage for part and IVA textures stays within the "
        + "budget that you set.";
    internal static string LOC_StreamingBudgetTitle = "Streaming memory budget (MB)";
    internal static string LOC_StreamingBudgetTooltip =
        "The amount of memory that is allocated to store streaming textures. "
        + "Unity will automatically load and unload parts of textures to stay "
        + "under this limit.";
    // Format string for the budget slider's value readout; <<1>> is the megabyte value.
    internal static string LOC_F_StreamingBudgetValue = "<<1>> MB";

    protected override Version VersionMin => new(1, 12, 3);

    protected override void ApplyPatches() { }

    protected override void OnLoadData(ConfigNode node)
    {
        node.TryGetValue(nameof(MipmapStreamingEnabled), ref MipmapStreamingEnabled);
        node.TryGetValue(nameof(MipmapStreamingBudgetMb), ref MipmapStreamingBudgetMb);
    }

    protected override void OnPatchApplied()
    {
        if (!KSPCFFastLoader.IsPatchEnabled)
            return;

        var go = new GameObject();
        go.AddComponent<TextureStreamingController>();

        QualitySettings.streamingMipmapsActive = MipmapStreamingEnabled;
    }

    internal void OnSettingsUpdated()
    {
        QualitySettings.streamingMipmapsActive = MipmapStreamingEnabled;
    }
}

internal class TextureStreamingController : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(this);

        QualitySettings.streamingMipmapsAddAllCameras = true;
        QualitySettings.streamingMipmapsMaxLevelReduction = TextureStreaming.MaxStreamingMipReduction;
    }

    void Start()
    {
        StartCoroutine(UpdateMemoryLimitCoroutine());
    }

    static readonly WaitForSecondsRealtime WaitForHalfSecond = new(0.5f);
    static readonly WaitForEndOfFrame WaitForEndOfFrame = new();

    IEnumerator UpdateMemoryLimitCoroutine()
    {
        const ulong MB = 1024 * 1024;

        while (true)
        {
            yield return WaitForHalfSecond;
            yield return WaitForEndOfFrame;

            if (QualitySettings.streamingMipmapsActive)
            {
                ulong totalGraphicsMemory = (ulong)SystemInfo.graphicsMemorySize * 4 / 5;
                ulong totalTextureMemory = Texture.nonStreamingTextureMemory;
                ulong requestedMemory = totalTextureMemory + (ulong)TextureStreaming.MipmapStreamingBudgetMb * MB;

                // Always request at least the configured budget, but if there is more
                // memory available then we might as well allow unity to use it.
                //
                // We do have to deal with other mods loading large textures on-demand
                // so it doesn't really work to just set a fixed budget.
                QualitySettings.streamingMipmapsMemoryBudget = Math.Max(
                    (requestedMemory + (MB - 1)) / MB,
                    totalGraphicsMemory);
            }
            else if (QualitySettings.streamingMipmapsMemoryBudget != 0)
            {
                QualitySettings.streamingMipmapsMemoryBudget = 0;
            }
        }
    }
}
