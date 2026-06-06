using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Provides optional engine execution dependencies for workflow runs.
/// </summary>
public sealed class WorkflowExecutionOptions
{
    /// <summary>
    /// workflow 実行に使う任意の依存関係を作成します。
    /// </summary>
    /// <param name="loggerFactory">engine と StepContext に使う logger factory。null の場合は provider 付き logging を無効にします。</param>
    /// <param name="engineArguments">StepContext から取得できる CLI 引数。</param>
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

    /// <summary>
    /// Step 本体の通常例外に適用する retry 設定を取得または設定します。null の場合は retry を行いません。
    /// </summary>
    public RetryOptions? Retry { get; set; }

    /// <summary>
    /// StepContext に登録する標準 Config instance を取得します。
    /// </summary>
    internal object? StandardConfig { get; private init; }

    /// <summary>
    /// 標準 Config instance を追加した実行 option を作成します。
    /// </summary>
    /// <param name="standardConfig">StepContext に登録する標準 Config instance。</param>
    /// <returns>既存設定を引き継ぎ、標準 Config instance を持つ実行 option。</returns>
    internal WorkflowExecutionOptions WithStandardConfig(object standardConfig)
    {
        ArgumentNullException.ThrowIfNull(standardConfig);

        return new WorkflowExecutionOptions(LoggerFactory, EngineArguments)
        {
            Retry = Retry,
            StandardConfig = standardConfig,
            StepTimeout = StepTimeout,
        };
    }
}
