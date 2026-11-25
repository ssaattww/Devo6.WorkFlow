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
    /// .NET 10 file-based C# workflow input requires a .NET 10 host.
    /// </summary>
    public const string ScriptFileRequiresNet10Host = "SCRIPT_FILE_REQUIRES_NET10_HOST";

    /// <summary>
    /// File-based C# workflow directives do not meet the supported preflight contract.
    /// </summary>
    public const string ScriptFileDirectiveInvalid = "SCRIPT_FILE_DIRECTIVE_INVALID";

    /// <summary>
    /// File-based C# build or execution failed.
    /// </summary>
    public const string ScriptBuildFailed = "SCRIPT_BUILD_FAILED";
    /// <summary>
    /// File-based build environment properties could not be verified before restore.
    /// </summary>
    public const string ScriptBuildEnvironmentUnsupported = "SCRIPT_BUILD_ENVIRONMENT_UNSUPPORTED";

    /// <summary>
    /// The NuGet package version of the running workflow host could not be determined.
    /// </summary>
    public const string PackageVersionUnknown = "PACKAGE_VERSION_UNKNOWN";

    /// <summary>
    /// A compatible .NET SDK could not be selected for a file-based workflow.
    /// </summary>
    public const string ScriptSdkUnavailable = "SCRIPT_SDK_UNAVAILABLE";

    /// <summary>
    /// File-based workflow build succeeded, but provider execution is not implemented yet.
    /// </summary>
    public const string ScriptEntryExecutionUnavailable = "SCRIPT_ENTRY_EXECUTION_UNAVAILABLE";

    /// <summary>
    /// A file-based workflow entry does not meet the supported provider metadata contract.
    /// </summary>
    public const string ScriptEntryProviderInvalid = "SCRIPT_ENTRY_PROVIDER_INVALID";

    /// <summary>
    /// A workflow entry provider constructor or Create() call failed.
    /// </summary>
    public const string ScriptEntryProviderFailed = "SCRIPT_ENTRY_PROVIDER_FAILED";

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
    /// 条件付き実行の条件判定が例外により失敗したことを示します。
    /// </summary>
    public const string ConditionEvaluationFailed = "CONDITION_EVALUATION_FAILED";

    /// <summary>
    /// Switch の分岐 selector が例外により失敗したことを示します。
    /// </summary>
    public const string SwitchSelectorFailed = "SWITCH_SELECTOR_FAILED";

    /// <summary>
    /// execution trace の serialize が失敗したことを示します。
    /// </summary>
    public const string TraceSerializationFailed = "TRACE_SERIALIZATION_FAILED";
}
