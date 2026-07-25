namespace KSPCommunityFixes.Library.Model;

/// <summary>
/// A representation of a .mu file that is ready to be executed.
/// </summary>
internal sealed class CompiledModel
{
    /// <summary>The ordered assembly steps.</summary>
    public IModelInstruction[] Instructions;

    /// <summary>Serialization-ready meshes referenced by this model (static meshes only).</summary>
    public MeshBlob[] Blobs;

    /// <summary>Maps each mesh's <c>locals</c> slot to the canonical name the driver looks it up by
    /// in the loaded mesh <c>AssetBundle</c>.</summary>
    public MeshBinding[] Bindings;

    /// <summary>Size of the <c>locals[]</c> array the driver must allocate (high-water slot + 1).</summary>
    public int LocalCount;

    /// <summary>The source <c>file.url</c>.</summary>
    public string SourceUrl;

    /// <summary>Diagnostics collected on the worker thread during compilation. KSP installs its own
    /// <c>ILogHandler</c> and mods chain handlers onto <c>Application.logMessageReceived</c>, none of
    /// which are thread-safe, so these must NEVER be logged off-thread; they are buffered here and
    /// flushed on the MAIN thread via <see cref="FlushLogs"/>.</summary>
    public System.Collections.Generic.List<DeferredLog> Logs;

    /// <summary>Emits the buffered diagnostics through <c>UnityEngine.Debug</c>.</summary>
    public void FlushLogs()
    {
        if (Logs == null)
            return;

        for (int i = 0; i < Logs.Count; i++)
        {
            DeferredLog log = Logs[i];
            switch (log.Type)
            {
                case UnityEngine.LogType.Error:
                case UnityEngine.LogType.Exception:
                    UnityEngine.Debug.LogError(log.Message);
                    break;
                case UnityEngine.LogType.Warning:
                    UnityEngine.Debug.LogWarning(log.Message);
                    break;
                default:
                    UnityEngine.Debug.Log(log.Message);
                    break;
            }
        }
    }
}

/// <summary>A single diagnostic buffered during off-thread compilation.</summary>
internal readonly struct DeferredLog(UnityEngine.LogType type, string message)
{
    public readonly UnityEngine.LogType Type = type;
    public readonly string Message = message;
}
