namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// Represents the observable outcome returned by the engine after running a workflow entry.
/// </summary>
public sealed class WorkflowResult
{
    /// <summary>
    /// Gets the entry name that was executed.
    /// </summary>
    public string EntryName { get; init; } = "";

    /// <summary>
    /// Gets whether the entry completed without an engine-level failure.
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// Gets the workflow error code when execution failed.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Gets the human-readable failure message when execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the structured execution trace recorded for the run.
    /// </summary>
    public ExecutionTrace? Trace { get; init; }
}
