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
    public WorkflowExecutionOptions(ILoggerFactory? loggerFactory = null)
    {
        LoggerFactory = loggerFactory;
    }

    /// <summary>
    /// Gets the logger factory used to create engine and step loggers.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; }
}
