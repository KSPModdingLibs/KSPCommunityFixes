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

        /// <summary>Diagnostics collected on the worker thread during compilation. KSP installs its own
        /// <c>ILogHandler</c> and mods chain handlers onto <c>Application.logMessageReceived</c>, none of
        /// which are thread-safe, so these must NEVER be logged off-thread; they are buffered here and
        /// flushed on the MAIN thread via <see cref="FlushLogs"/>.</summary>
        public System.Collections.Generic.List<DeferredLog> Logs;

        /// <summary>
        /// MAIN THREAD ONLY. Emits every buffered <see cref="DeferredLog"/> through
        /// <c>UnityEngine.Debug</c>, mapping <see cref="UnityEngine.LogType"/> to the matching Debug call
        /// (Error/Exception → LogError, Warning → LogWarning, else → Log). Null-safe (no-op when
        /// <see cref="Logs"/> was never populated).
        /// </summary>
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

    /// <summary>A single diagnostic buffered during off-thread compilation, to be emitted later on the
    /// main thread via <see cref="CompiledModel.FlushLogs"/>.</summary>
    internal readonly struct DeferredLog
    {
        public readonly UnityEngine.LogType Type;
        public readonly string Message;
        public DeferredLog(UnityEngine.LogType type, string message) { Type = type; Message = message; }
    }
}
