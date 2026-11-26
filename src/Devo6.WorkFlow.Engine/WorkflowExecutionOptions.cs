using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// workflow entry の事前取消に対する内部実行方針を表します。
/// </summary>
internal enum WorkflowEntryCancellationPolicy
{
    /// <summary>
    /// 公開 Engine 経路の既存契約として、開始前に取消済みでも最初の同期 Step を完了させます。
    /// </summary>
    PreservePreCancelledSynchronousStep,

    /// <summary>
    /// 取消済みなら最初の同期 Step を開始しません。
    /// </summary>
    StopBeforeSynchronousStep,
}

/// <summary>
/// workflow 実行に使う任意の依存関係を提供します。
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
    /// engine と step logger を作成する logger factory を取得します。
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; }

    /// <summary>
    /// workflow 実行中に StepContext へ公開する command-line engine 引数を取得します。
    /// </summary>
    public EngineArguments? EngineArguments { get; }

    /// <summary>
    /// Step ごとに適用する timeout を取得または設定します。null の場合は timeout を適用しません。
    /// </summary>
    public TimeSpan? StepTimeout { get; set; }

    /// <summary>
    /// テストで実際の Step timeout token を観測する任意 callback。
    /// </summary>
    internal Action<CancellationToken>? StepTimeoutTokenObserver { get; set; }

    /// <summary>
    /// Step 本体の通常例外に適用する retry 設定を取得または設定します。null の場合は retry を行いません。
    /// </summary>
    public RetryOptions? Retry { get; set; }

    /// <summary>
    /// StepContext に登録する標準 Config instance を取得します。
    /// </summary>
    internal object? StandardConfig { get; private init; }

    /// <summary>
    /// Step 実行直前に StepContext へ登録する Step Config instance の一覧を取得します。
    /// </summary>
    internal IReadOnlyList<StepConfigValue> StepConfigs { get; private init; } = [];

    /// <summary>
    /// workflow entry の開始前取消を扱う内部方針を取得します。
    /// </summary>
    internal WorkflowEntryCancellationPolicy CancellationPolicy { get; private init; } =
        WorkflowEntryCancellationPolicy.PreservePreCancelledSynchronousStep;

    /// <summary>
    /// entry 取消方針だけを変更し、その他の実行設定を維持した option を作成します。
    /// </summary>
    /// <param name="cancellationPolicy">適用する内部取消方針。</param>
    /// <returns>指定方針と既存設定を持つ実行 option。</returns>
    internal WorkflowExecutionOptions WithCancellationPolicy(WorkflowEntryCancellationPolicy cancellationPolicy)
    {
        return new WorkflowExecutionOptions(LoggerFactory, EngineArguments)
        {
            Retry = Retry,
            StandardConfig = StandardConfig,
            StepConfigs = StepConfigs,
            StepTimeout = StepTimeout,
            StepTimeoutTokenObserver = StepTimeoutTokenObserver,
            CancellationPolicy = cancellationPolicy,
        };
    }

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
            StepConfigs = StepConfigs,
            StandardConfig = standardConfig,
            StepTimeout = StepTimeout,
            StepTimeoutTokenObserver = StepTimeoutTokenObserver,
            CancellationPolicy = CancellationPolicy,
        };
    }

    /// <summary>
    /// Step Config instance の一覧を追加した実行 option を作成します。
    /// </summary>
    /// <param name="stepConfigs">Step 実行直前に登録する Config instance の一覧。</param>
    /// <returns>既存設定を引き継ぎ、Step Config instance の一覧を持つ実行 option。</returns>
    internal WorkflowExecutionOptions WithStepConfigs(IReadOnlyList<StepConfigValue> stepConfigs)
    {
        ArgumentNullException.ThrowIfNull(stepConfigs);

        return new WorkflowExecutionOptions(LoggerFactory, EngineArguments)
        {
            Retry = Retry,
            StandardConfig = StandardConfig,
            StepConfigs = stepConfigs.ToArray(),
            StepTimeout = StepTimeout,
            StepTimeoutTokenObserver = StepTimeoutTokenObserver,
            CancellationPolicy = CancellationPolicy,
        };
    }
}

/// <summary>
/// Step 実行直前に登録する検証済み Config instance を保持します。
/// </summary>
/// <param name="StepIndex">Config を登録する Step の登録順 index。</param>
/// <param name="ConfigType">StepContext へ登録する Config 型。</param>
/// <param name="Config">StepContext へ登録する Config instance。</param>
internal sealed record StepConfigValue(int StepIndex, Type ConfigType, object Config);
