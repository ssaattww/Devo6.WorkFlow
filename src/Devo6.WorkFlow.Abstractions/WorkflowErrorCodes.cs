namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// Provides stable workflow error code constants used by validation, execution results, logs, and traces.
/// </summary>
public static class WorkflowErrorCodes
{
    /// <summary>
    /// Indicates that the entry script file was not found.
    /// </summary>
    public const string EntryScriptNotFound = "ENTRY_SCRIPT_NOT_FOUND";

    /// <summary>
    /// Indicates that the requested entry step was not found.
    /// </summary>
    public const string EntryStepNotFound = "ENTRY_STEP_NOT_FOUND";

    /// <summary>
    /// Indicates that multiple public steps used the same name.
    /// </summary>
    public const string DuplicateStepName = "DUPLICATE_STEP_NAME";

    /// <summary>
    /// Indicates that script compilation failed.
    /// </summary>
    public const string ScriptCompileFailed = "SCRIPT_COMPILE_FAILED";

    /// <summary>
    /// Indicates that script loading failed.
    /// </summary>
    public const string ScriptLoadFailed = "SCRIPT_LOAD_FAILED";

    /// <summary>
    /// Indicates that script loading detected a cycle.
    /// </summary>
    public const string ScriptLoadCycleDetected = "SCRIPT_LOAD_CYCLE_DETECTED";

    /// <summary>
    /// Indicates that a script reference was not allowed by policy.
    /// </summary>
    public const string ScriptReferenceNotAllowed = "SCRIPT_REFERENCE_NOT_ALLOWED";

    /// <summary>
    /// Indicates that restoring script NuGet dependencies failed.
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
    /// Indicates that the loaded script used an incompatible workflow API identity.
    /// </summary>
    public const string ScriptApiIdentityMismatch = "SCRIPT_API_IDENTITY_MISMATCH";

    /// <summary>
    /// Indicates that a required step input was not found.
    /// </summary>
    public const string StepInputNotFound = "STEP_INPUT_NOT_FOUND";

    /// <summary>
    /// Indicates that a step input existed but could not be read as the requested type.
    /// </summary>
    public const string StepInputTypeMismatch = "STEP_INPUT_TYPE_MISMATCH";

    /// <summary>
    /// Indicates that a required configuration source was not found.
    /// </summary>
    public const string ConfigNotFound = "CONFIG_NOT_FOUND";

    /// <summary>
    /// Indicates that configuration loading failed.
    /// </summary>
    public const string ConfigLoadFailed = "CONFIG_LOAD_FAILED";

    /// <summary>
    /// Indicates that a step threw or otherwise failed during execution.
    /// </summary>
    public const string StepExecutionFailed = "STEP_EXECUTION_FAILED";

    /// <summary>
    /// Indicates that a step exceeded its configured timeout.
    /// </summary>
    public const string StepTimeout = "STEP_TIMEOUT";

    /// <summary>
    /// Step が外部 CancellationToken により cancel されたことを示します。
    /// </summary>
    public const string StepCanceled = "STEP_CANCELED";

    /// <summary>
    /// Indicates that serializing the execution trace failed.
    /// </summary>
    public const string TraceSerializationFailed = "TRACE_SERIALIZATION_FAILED";
}
