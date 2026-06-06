using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Provides optional engine execution dependencies for workflow runs.
/// </summary>
public sealed class WorkflowExecutionOptions
{
    /// <summary>
    /// Creates options for a workflow execution.
    /// </summary>
    /// <param name="loggerFactory">The logger factory used by the engine and step context, or null to disable provider-backed logging.</param>
    /// <param name="engineArguments">The command-line engine arguments to expose through StepContext.</param>
    public WorkflowExecutionOptions(ILoggerFactory? loggerFactory = null, EngineArguments? engineArguments = null)
    {
        LoggerFactory = loggerFactory;
        EngineArguments = engineArguments;
    }

    /// <summary>
    /// Gets the logger factory used to create engine and step loggers.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; }

    /// <summary>
    /// Gets command-line engine arguments exposed through StepContext during workflow execution.
    /// </summary>
    public EngineArguments? EngineArguments { get; }

    /// <summary>
    /// Step ごとに適用する timeout を取得または設定します。null の場合は timeout を適用しません。
    /// </summary>
    public TimeSpan? StepTimeout { get; set; }
}
