using UnityEngine;

namespace KSPCommunityFixes.Library.Model;

/// <summary>
/// The per-file work item and result carrier for the background model pipeline.
/// </summary>
internal sealed class ModelLoadRequest
{
    public enum State : byte { Pending, Ready, Failed }

    public enum Kind : byte
    {
        /// <summary>
        /// This request contains a set of instructions that need to be executed
        /// in order to build the model.
        /// </summary>
        CompiledMu,
        /// <summary>
        /// This request contains a DAE model.
        /// </summary>
        Dae,
        /// <summary>
        /// An error occurred when loading the model for this request.
        /// </summary>
        Failed
    }

    public UrlDir.UrlFile File;
    public Kind ModelKind;

    /// <summary>The compiled model, if any.</summary>
    public CompiledModel Compiled;

    /// <summary>The owning <see cref="ModelGroup"/>, if any.</summary>
    public ModelGroup Group;

    public string FailureMessage;

    /// <summary>Byte length of the model file.</summary>
    public long FileLength;

    public volatile State Status;

    public GameObject Result;
}
