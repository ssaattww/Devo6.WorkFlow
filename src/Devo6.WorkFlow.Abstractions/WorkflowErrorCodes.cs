namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// validation、実行結果、log、trace で使う安定した workflow error code 定数を提供します。
/// </summary>
public static class WorkflowErrorCodes
{
    /// <summary>
    /// entry script file が見つからなかったことを示します。
    /// </summary>
    public const string EntryScriptNotFound = "ENTRY_SCRIPT_NOT_FOUND";

    /// <summary>
    /// 要求された entry Step が見つからなかったことを示します。
    /// </summary>
    public const string EntryStepNotFound = "ENTRY_STEP_NOT_FOUND";

    /// <summary>
    /// 複数の public Step が同じ名前を使用したことを示します。
    /// </summary>
    public const string DuplicateStepName = "DUPLICATE_STEP_NAME";

    /// <summary>
    /// script compile が失敗したことを示します。
    /// </summary>
    public const string ScriptCompileFailed = "SCRIPT_COMPILE_FAILED";

    /// <summary>
    /// script load が失敗したことを示します。
    /// </summary>
    public const string ScriptLoadFailed = "SCRIPT_LOAD_FAILED";

    /// <summary>
    /// script load で循環を検出したことを示します。
    /// </summary>
    public const string ScriptLoadCycleDetected = "SCRIPT_LOAD_CYCLE_DETECTED";

    /// <summary>
    /// script reference が policy で許可されていなかったことを示します。
    /// </summary>
    public const string ScriptReferenceNotAllowed = "SCRIPT_REFERENCE_NOT_ALLOWED";

    /// <summary>
    /// script の NuGet 依存関係 restore が失敗したことを示します。
    /// </summary>
    public const string ScriptNugetRestoreFailed = "SCRIPT_NUGET_RESTORE_FAILED";

    /// <summary>
    /// NuGet lock file が必要な script で見つからなかったことを示します。
    /// </summary>
    public const string ScriptNugetLockMissing = "SCRIPT_NUGET_LOCK_MISSING";

    /// <summary>
    /// NuGet lock file と script の NuGet 依存関係が一致しなかったことを示します。
    /// </summary>
    public const string ScriptNugetLockMismatch = "SCRIPT_NUGET_LOCK_MISMATCH";

    /// <summary>
    /// load された script が互換性のない workflow API identity を使用したことを示します。
    /// </summary>
    public const string ScriptApiIdentityMismatch = "SCRIPT_API_IDENTITY_MISMATCH";

    /// <summary>
    /// 必須の Step 入力が見つからなかったことを示します。
    /// </summary>
    public const string StepInputNotFound = "STEP_INPUT_NOT_FOUND";

    /// <summary>
    /// Step 入力は存在したが要求された型として読めなかったことを示します。
    /// </summary>
    public const string StepInputTypeMismatch = "STEP_INPUT_TYPE_MISMATCH";

    /// <summary>
    /// 必須の config source が見つからなかったことを示します。
    /// </summary>
    public const string ConfigNotFound = "CONFIG_NOT_FOUND";

    /// <summary>
    /// config load が失敗したことを示します。
    /// </summary>
    public const string ConfigLoadFailed = "CONFIG_LOAD_FAILED";

    /// <summary>
    /// Step が例外またはその他の理由で実行中に失敗したことを示します。
    /// </summary>
    public const string StepExecutionFailed = "STEP_EXECUTION_FAILED";

    /// <summary>
    /// Step が設定された timeout を超過したことを示します。
    /// </summary>
    public const string StepTimeout = "STEP_TIMEOUT";

    /// <summary>
    /// Step が外部 CancellationToken により cancel されたことを示します。
    /// </summary>
    public const string StepCanceled = "STEP_CANCELED";

    /// <summary>
    /// execution trace の serialize が失敗したことを示します。
    /// </summary>
    public const string TraceSerializationFailed = "TRACE_SERIALIZATION_FAILED";
}
