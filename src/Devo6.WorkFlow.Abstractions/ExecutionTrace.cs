namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// workflow 実行中に記録した構造化履歴を保持します。
/// </summary>
public sealed class ExecutionTrace
{
    /// <summary>
    /// 順序付き Step 履歴から trace を作成します。
    /// </summary>
    /// <param name="steps">trace として公開する順序付き Step 履歴。</param>
    public ExecutionTrace(IReadOnlyList<ExecutionTraceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        Steps = steps.ToArray();
    }

    /// <summary>
    /// workflow 実行中に記録した順序付き Step 履歴を取得します。
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
    /// 明示 capture で記録された生成値を含む Step 試行履歴を作成します。
    /// </summary>
    /// <param name="stepName">記録対象の Step 型名。</param>
    /// <param name="status">Step 試行の最終状態。</param>
    /// <param name="duration">Step 試行にかかった時間。</param>
    /// <param name="errorCode">Step 試行が失敗した場合の workflow error code。</param>
    /// <param name="attempt">1 始まりの試行番号。</param>
    public ExecutionTraceStep(
        string stepName,
        ExecutionTraceStepStatus status,
        TimeSpan duration,
        string? errorCode,
        int attempt,
        IReadOnlyList<ExecutionTraceValue> producedValues)
        : this(stepName, status, duration, errorCode, attempt)
    {
        ArgumentNullException.ThrowIfNull(producedValues);

        ProducedValues = producedValues.ToArray();
    }

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

    /// <summary>
    /// Step 成功後に明示 capture で記録された生成値を取得します。
    /// </summary>
    public IReadOnlyList<ExecutionTraceValue> ProducedValues { get; init; } = Array.Empty<ExecutionTraceValue>();
}

/// <summary>
/// trace に記録できる Step 実行の最終状態を表します。
/// </summary>
public enum ExecutionTraceStepStatus
{
    /// <summary>
    /// Step が成功したことを表します。
    /// </summary>
    Succeeded,

    /// <summary>
    /// Step が workflow 完了前に失敗したことを表します。
    /// </summary>
    Failed,
}

/// <summary>
/// trace value の生成元を表します。
/// </summary>
public enum ExecutionTraceValueSource
{
    /// <summary>
    /// Produce で登録された値を表します。
    /// </summary>
    Produce,

    /// <summary>
    /// StoreAs で登録された値を表します。
    /// </summary>
    StoreAs,
}

/// <summary>
/// trace value の保存状態を表します。
/// </summary>
public enum ExecutionTraceValueCaptureStatus
{
    /// <summary>
    /// JSON 文字列として保存されたことを表します。
    /// </summary>
    Serialized,

    /// <summary>
    /// 値本文を保存せず metadata のみ記録したことを表します。
    /// </summary>
    Redacted,

    /// <summary>
    /// 直列化できず値本文を保存しなかったことを表します。
    /// </summary>
    NotSerializable,
}

/// <summary>
/// Produce または StoreAs で生成された trace value を表します。
/// </summary>
/// <param name="TypeName">値の型名。</param>
/// <param name="Name">名前付き値の名前。型だけで登録した値では null。</param>
/// <param name="Source">値を登録した API の種別。</param>
/// <param name="CaptureStatus">値本文の保存状態。</param>
/// <param name="SerializedValue">JSON として保存した値本文。</param>
/// <param name="SerializationFailureReason">直列化できなかった理由。</param>
public sealed record ExecutionTraceValue(
    string TypeName,
    string? Name,
    ExecutionTraceValueSource Source,
    ExecutionTraceValueCaptureStatus CaptureStatus,
    string? SerializedValue,
    string? SerializationFailureReason);

/// <summary>
/// Produce または StoreAs で trace value を記録する方法を表します。
/// </summary>
public enum TraceValueCapture
{
    /// <summary>
    /// 値本文を JSON 文字列として保存します。
    /// </summary>
    Serialized = 1,

    /// <summary>
    /// 値本文を保存せず metadata のみ記録します。
    /// </summary>
    Redacted = 2,
}
