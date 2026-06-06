using Devo6.WorkFlow.Abstractions;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Represents the pre-execution validation outcome for a .csx workflow entry.
/// </summary>
public sealed class WorkflowValidationResult
{
    /// <summary>
    /// Gets whether validation completed without errors.
    /// </summary>
    public bool Succeeded => Errors.Count == 0;

    /// <summary>
    /// Gets the validation errors found before workflow execution.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];
}

/// <summary>
/// Configures additional pre-execution validation inputs for a .csx workflow entry.
/// </summary>
public sealed class CsxValidationOptions
{
    /// <summary>
    /// Gets config file paths to check for existence, resolved relative to the entry .csx directory when not rooted.
    /// </summary>
    public IReadOnlyList<string> ConfigPaths { get; init; } = [];
}
