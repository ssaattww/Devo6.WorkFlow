namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// Holds structured execution history without storing step input, configuration, or step output values.
/// </summary>
public sealed class ExecutionTrace
{
    /// <summary>
    /// Creates an execution trace from the ordered step history.
    /// </summary>
    /// <param name="steps">The ordered step history to expose from the trace.</param>
    public ExecutionTrace(IReadOnlyList<ExecutionTraceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        Steps = steps.ToArray();
    }

    /// <summary>
    /// Gets the ordered step history captured during workflow execution.
    /// </summary>
    public IReadOnlyList<ExecutionTraceStep> Steps { get; }
}

/// <summary>
/// Describes one step execution in the trace without exposing input, configuration, or output values.
/// </summary>
/// <param name="StepName">The step type name recorded for the execution.</param>
/// <param name="Status">The final execution status for the step.</param>
/// <param name="Duration">The elapsed time spent executing the step.</param>
/// <param name="ErrorCode">The workflow error code when the step failed.</param>
public sealed record ExecutionTraceStep(
    string StepName,
    ExecutionTraceStepStatus Status,
    TimeSpan Duration,
    string? ErrorCode);

/// <summary>
/// Defines the supported final states for a traced step execution.
/// </summary>
public enum ExecutionTraceStepStatus
{
    /// <summary>
    /// The step completed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The step failed before the workflow completed.
    /// </summary>
    Failed,
}
