namespace Devo6.WorkFlow.Abstractions;

/// <summary>
/// Holds command-line engine arguments that workflow steps can read from StepContext.
/// </summary>
public sealed class EngineArguments
{
    /// <summary>
    /// Gets the entry .csx file path selected for the workflow run.
    /// </summary>
    public string EntryPath { get; init; } = "";

    /// <summary>
    /// Gets the resolved config file path, or an empty string when no config file was specified.
    /// </summary>
    public string ConfigPath { get; init; } = "";

    /// <summary>
    /// Gets string override settings supplied by repeated --set key=value arguments.
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
}
