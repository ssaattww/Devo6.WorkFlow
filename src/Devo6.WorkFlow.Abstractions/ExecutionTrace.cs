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
/// 入力、設定、出力値を含めずに 1 回の Step 試行履歴を表します。
/// </summary>
/// <param name="StepName">記録対象の Step 型名。</param>
/// <param name="Status">Step 試行の最終状態。</param>
/// <param name="Duration">Step 試行にかかった時間。</param>
/// <param name="ErrorCode">Step 試行が失敗した場合の workflow error code。</param>
/// <param name="Attempt">1 始まりの試行番号。</param>
public sealed record ExecutionTraceStep(
    string StepName,
    ExecutionTraceStepStatus Status,
    TimeSpan Duration,
    string? ErrorCode,
    int Attempt)
{
    /// <summary>
    /// 互換性のため、試行番号 1 の Step 試行履歴を作成します。
    /// </summary>
    /// <param name="stepName">記録対象の Step 型名。</param>
    /// <param name="status">Step 試行の最終状態。</param>
    /// <param name="duration">Step 試行にかかった時間。</param>
    /// <param name="errorCode">Step 試行が失敗した場合の workflow error code。</param>
    public ExecutionTraceStep(
        string stepName,
        ExecutionTraceStepStatus status,
        TimeSpan duration,
        string? errorCode)
        : this(stepName, status, duration, errorCode, 1)
    {
    }
}

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
