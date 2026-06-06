using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Devo6.WorkFlow.Engine;

public static class CompositeStep
{
    public static CompositeStepDefinition Define(string name)
    {
        return new CompositeStepDefinition(name);
    }
}

public sealed class CompositeStepDefinition
{
    internal CompositeStepDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    public string Name { get; }

    public CompositeStep<TOut> Run<TStep, TOut>()
        where TStep : IStep<TOut>, new()
    {
        return new CompositeStep<TOut>(Name, [StepRegistration.Create<TStep, TOut>()]);
    }

    /// <summary>
    /// Registers the first asynchronous step in this composite entry.
    /// </summary>
    /// <typeparam name="TStep">The asynchronous step type to run.</typeparam>
    /// <typeparam name="TOut">The output type produced by the asynchronous step.</typeparam>
    /// <returns>A composite step that can be extended or executed.</returns>
    public CompositeStep<TOut> RunAsync<TStep, TOut>()
        where TStep : IAsyncStep<TOut>, new()
    {
        return new CompositeStep<TOut>(Name, [StepRegistration.CreateAsync<TStep, TOut>()]);
    }
}

public sealed class CompositeStep<TOut> : IStep<TOut>, IAsyncStep<TOut>
{
    private readonly IReadOnlyList<StepRegistration> steps;

    internal CompositeStep(string name, IReadOnlyList<StepRegistration> steps)
    {
        Name = name;
        this.steps = steps.ToArray();
    }

    public string Name { get; }

    public CompositeStep<TNext> Run<TStep, TNext>()
        where TStep : IStep<TNext>, new()
    {
        return new CompositeStep<TNext>(Name, Append(StepRegistration.Create<TStep, TNext>()));
    }

    /// <summary>
    /// Appends an asynchronous step to this composite entry.
    /// </summary>
    /// <typeparam name="TStep">The asynchronous step type to run.</typeparam>
    /// <typeparam name="TNext">The output type produced by the asynchronous step.</typeparam>
    /// <returns>A composite step whose current output type is the appended asynchronous step output.</returns>
    public CompositeStep<TNext> RunAsync<TStep, TNext>()
        where TStep : IAsyncStep<TNext>, new()
    {
        return new CompositeStep<TNext>(Name, Append(StepRegistration.CreateAsync<TStep, TNext>()));
    }

    public CompositeStep<TOut> Produce<TValue>(Func<TOut, TValue> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return WithCurrentStep(CurrentStep.AddProducer((input, value) => input.Add(selector((TOut)value!))));
    }

    public CompositeStep<TOut> Produce<TValue>(string name, Func<TOut, TValue> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return WithCurrentStep(CurrentStep.AddProducer((input, value) => input.Add(name, selector((TOut)value!))));
    }

    public CompositeStep<TOut> StoreAs()
    {
        return Produce<TOut>(value => value);
    }

    public CompositeStep<TOut> Discard()
    {
        return WithCurrentStep(CurrentStep.ClearProducers());
    }

    public TOut Execute(StepInput input)
    {
        return ExecuteAsync(input, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Executes this composite step asynchronously with the supplied input values.
    /// </summary>
    /// <param name="input">The input values available to the first step.</param>
    /// <param name="cancellationToken">The cancellation token passed to asynchronous steps.</param>
    /// <returns>The output produced by the final step in the composite entry.</returns>
    public async Task<TOut> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        object? currentValue = default(TOut);

        foreach (StepRegistration step in steps)
        {
            currentValue = await step.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
            step.Produce(input, currentValue);
        }

        return (TOut)currentValue!;
    }

    /// <summary>
    /// Executes this composite entry through the engine path and returns a workflow result with logs and trace history.
    /// </summary>
    /// <param name="options">The execution dependencies to use, or null for default options.</param>
    /// <returns>The workflow result describing success, failure, and captured trace history.</returns>
    public WorkflowResult ExecuteWorkflow(WorkflowExecutionOptions? options = null)
    {
        return ExecuteWorkflowAsync(options, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// engine 経路で composite entry を非同期実行し、結果と trace を返します。
    /// </summary>
    /// <param name="options">実行時の依存関係。null の場合は既定値を使います。</param>
    /// <param name="cancellationToken">非同期 Step へ渡す外部キャンセル用 token。</param>
    /// <returns>成功、失敗、記録した trace を含む workflow 結果。</returns>
    public async Task<WorkflowResult> ExecuteWorkflowAsync(
        WorkflowExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WorkflowExecutionOptions();

        ILoggerFactory loggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance;
        ILogger engineLogger = loggerFactory.CreateLogger("Devo6.WorkFlow.Engine");
        ILogger stepLogger = loggerFactory.CreateLogger("Devo6.WorkFlow.Step");
        var traceSteps = new List<ExecutionTraceStep>();
        var context = new StepContext(stepLogger);
        if (options.EngineArguments is not null)
        {
            context.Set(options.EngineArguments);
        }

        var input = new StepInput(context);
        object? currentValue = default(TOut);

        using IDisposable? entryScope = engineLogger.BeginScope(new Dictionary<string, object?>
        {
            ["EntryName"] = Name,
            ["Attempt"] = 1,
        });

        engineLogger.LogInformation("Entry started");

        for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
        {
            StepRegistration step = steps[stepIndex];
            Stopwatch stopwatch = Stopwatch.StartNew();
            using IDisposable? stepScope = engineLogger.BeginScope(new Dictionary<string, object?>
            {
                ["EntryName"] = Name,
                ["StepName"] = step.Name,
                ["Attempt"] = 1,
            });

            engineLogger.LogInformation("Step started");

            using StepExecutionCancellation stepCancellation = CreateStepExecutionCancellation(options.StepTimeout, cancellationToken);

            try
            {
                currentValue = await step.ExecuteAsync(input, stepCancellation.Token).ConfigureAwait(false);

                StepCancellationFailure? cancellationFailure = DetectCancellationFailure(
                    step,
                    stepCancellation,
                    cancellationToken);
                if (cancellationFailure is not null)
                {
                    stopwatch.Stop();

                    return ToCancellationWorkflowResult(
                        traceSteps,
                        step,
                        stopwatch.Elapsed,
                        cancellationFailure,
                        engineLogger);
                }

                step.Produce(input, currentValue);
                stopwatch.Stop();
                traceSteps.Add(new ExecutionTraceStep(step.Name, ExecutionTraceStepStatus.Succeeded, stopwatch.Elapsed, null));
                engineLogger.LogInformation("Step succeeded");
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();

                return ToCancellationWorkflowResult(
                    traceSteps,
                    step,
                    stopwatch.Elapsed,
                    StepCancellationFailure.Canceled(exception.Message),
                    engineLogger);
            }
            catch (OperationCanceledException exception) when (stepCancellation.TimeoutWasRequested)
            {
                stopwatch.Stop();

                return ToCancellationWorkflowResult(
                    traceSteps,
                    step,
                    stopwatch.Elapsed,
                    StepCancellationFailure.TimedOut(step.Name, stepCancellation.Timeout!.Value, exception.Message),
                    engineLogger);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                traceSteps.Add(new ExecutionTraceStep(
                    step.Name,
                    ExecutionTraceStepStatus.Failed,
                    stopwatch.Elapsed,
                    WorkflowErrorCodes.StepExecutionFailed));
                engineLogger.LogError(
                    exception,
                    "Step failed with error code {ErrorCode}",
                    WorkflowErrorCodes.StepExecutionFailed);
                engineLogger.LogError(
                    exception,
                    "Entry failed with error code {ErrorCode}",
                    WorkflowErrorCodes.StepExecutionFailed);

                return new WorkflowResult
                {
                    EntryName = Name,
                    Succeeded = false,
                    ErrorCode = WorkflowErrorCodes.StepExecutionFailed,
                    ErrorMessage = exception.Message,
                    Trace = new ExecutionTrace(traceSteps),
                };
            }
        }

        engineLogger.LogInformation("Entry succeeded");

        return new WorkflowResult
        {
            EntryName = Name,
            Succeeded = true,
            Trace = new ExecutionTrace(traceSteps),
        };
    }

    /// <summary>
    /// Step 実行用に timeout と外部キャンセルを合成した token を作成します。
    /// </summary>
    private static StepExecutionCancellation CreateStepExecutionCancellation(
        TimeSpan? stepTimeout,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? timeoutSource = null;
        CancellationTokenSource? linkedSource = null;

        try
        {
            if (stepTimeout is null)
            {
                return new StepExecutionCancellation(cancellationToken, null, null, null);
            }

            timeoutSource = new CancellationTokenSource();
            timeoutSource.CancelAfter(stepTimeout.Value);
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            return new StepExecutionCancellation(linkedSource.Token, stepTimeout, timeoutSource, linkedSource);
        }
        catch
        {
            linkedSource?.Dispose();
            timeoutSource?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Step 完了後に timeout または外部キャンセルとして扱うべき状態を判定します。
    /// </summary>
    private static StepCancellationFailure? DetectCancellationFailure(
        StepRegistration step,
        StepExecutionCancellation stepCancellation,
        CancellationToken externalCancellationToken)
    {
        if (externalCancellationToken.IsCancellationRequested)
        {
            return StepCancellationFailure.Canceled(null);
        }

        if (!stepCancellation.TimeoutWasRequested)
        {
            return null;
        }

        return StepCancellationFailure.TimedOut(step.Name, stepCancellation.Timeout!.Value, null);
    }

    /// <summary>
    /// timeout または外部キャンセルを WorkflowResult と trace に変換します。
    /// </summary>
    private WorkflowResult ToCancellationWorkflowResult(
        List<ExecutionTraceStep> traceSteps,
        StepRegistration step,
        TimeSpan elapsed,
        StepCancellationFailure failure,
        ILogger engineLogger)
    {
        traceSteps.Add(new ExecutionTraceStep(
            step.Name,
            ExecutionTraceStepStatus.Failed,
            elapsed,
            failure.ErrorCode));
        engineLogger.LogWarning(
            "Step stopped with error code {ErrorCode}",
            failure.ErrorCode);
        engineLogger.LogWarning(
            "Entry failed with error code {ErrorCode}",
            failure.ErrorCode);

        return new WorkflowResult
        {
            EntryName = Name,
            Succeeded = false,
            ErrorCode = failure.ErrorCode,
            ErrorMessage = failure.Message,
            Trace = new ExecutionTrace(traceSteps),
        };
    }

    private StepRegistration CurrentStep
    {
        get
        {
            if (steps.Count == 0)
            {
                throw new InvalidOperationException("No step is registered.");
            }

            return steps[^1];
        }
    }

    private IReadOnlyList<StepRegistration> Append(StepRegistration registration)
    {
        StepRegistration[] nextSteps = new StepRegistration[steps.Count + 1];

        for (int i = 0; i < steps.Count; i++)
        {
            nextSteps[i] = steps[i];
        }

        nextSteps[^1] = registration;

        return nextSteps;
    }

    private CompositeStep<TOut> WithCurrentStep(StepRegistration registration)
    {
        StepRegistration[] nextSteps = steps.ToArray();
        nextSteps[^1] = registration;

        return new CompositeStep<TOut>(Name, nextSteps);
    }
}

internal sealed class StepRegistration
{
    private readonly string name;
    private readonly Func<StepInput, CancellationToken, Task<object?>> executeAsync;
    private readonly IReadOnlyList<Action<StepInput, object?>> producers;

    private StepRegistration(
        string name,
        Func<StepInput, CancellationToken, Task<object?>> executeAsync,
        IReadOnlyList<Action<StepInput, object?>> producers)
    {
        this.name = name;
        this.executeAsync = executeAsync;
        this.producers = producers.ToArray();
    }

    /// <summary>
    /// trace と log に記録する Step 名を取得します。
    /// </summary>
    public string Name => name;

    /// <summary>
    /// 同期 Step の登録情報を作成します。
    /// </summary>
    public static StepRegistration Create<TStep, TOut>()
        where TStep : IStep<TOut>, new()
    {
        return new StepRegistration(
            typeof(TStep).Name,
            (input, cancellationToken) =>
            {
                return Task.FromResult<object?>(new TStep().Execute(input));
            },
            []);
    }

    /// <summary>
    /// 非同期 Step の登録情報を作成します。
    /// </summary>
    public static StepRegistration CreateAsync<TStep, TOut>()
        where TStep : IAsyncStep<TOut>, new()
    {
        return new StepRegistration(
            typeof(TStep).Name,
            async (input, cancellationToken) => await new TStep().ExecuteAsync(input, cancellationToken).ConfigureAwait(false),
            []);
    }

    /// <summary>
    /// 登録済み Step を実行します。
    /// </summary>
    public Task<object?> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
    {
        return executeAsync(input, cancellationToken);
    }

    /// <summary>
    /// Step 成功後に入力へ値を追加する producer を加えます。
    /// </summary>
    public StepRegistration AddProducer(Action<StepInput, object?> producer)
    {
        ArgumentNullException.ThrowIfNull(producer);

        Action<StepInput, object?>[] nextProducers = new Action<StepInput, object?>[producers.Count + 1];

        for (int i = 0; i < producers.Count; i++)
        {
            nextProducers[i] = producers[i];
        }

        nextProducers[^1] = producer;

        return new StepRegistration(name, executeAsync, nextProducers);
    }

    /// <summary>
    /// 登録済み producer を削除した Step 登録情報を作成します。
    /// </summary>
    public StepRegistration ClearProducers()
    {
        return new StepRegistration(name, executeAsync, []);
    }

    /// <summary>
    /// 登録済み producer を実行します。
    /// </summary>
    public void Produce(StepInput input, object? value)
    {
        foreach (Action<StepInput, object?> producer in producers)
        {
            producer(input, value);
        }
    }
}

internal sealed class StepExecutionCancellation : IDisposable
{
    private readonly CancellationTokenSource? timeoutSource;
    private readonly CancellationTokenSource? linkedSource;

    /// <summary>
    /// Step 実行中に使う cancellation token と所有する source を初期化します。
    /// </summary>
    public StepExecutionCancellation(
        CancellationToken token,
        TimeSpan? timeout,
        CancellationTokenSource? timeoutSource,
        CancellationTokenSource? linkedSource)
    {
        Token = token;
        Timeout = timeout;
        this.timeoutSource = timeoutSource;
        this.linkedSource = linkedSource;
    }

    /// <summary>
    /// Step へ渡す合成済み cancellation token を取得します。
    /// </summary>
    public CancellationToken Token { get; }

    /// <summary>
    /// timeout が発火したかどうかを取得します。
    /// </summary>
    public bool TimeoutWasRequested => timeoutSource?.IsCancellationRequested == true;

    /// <summary>
    /// 設定された Step timeout を取得します。
    /// </summary>
    public TimeSpan? Timeout { get; }

    /// <summary>
    /// Step 実行用に作成した cancellation source を解放します。
    /// </summary>
    public void Dispose()
    {
        linkedSource?.Dispose();
        timeoutSource?.Dispose();
    }
}

internal sealed class StepCancellationFailure
{
    /// <summary>
    /// cancellation 系の失敗情報を初期化します。
    /// </summary>
    private StepCancellationFailure(string errorCode, string message)
    {
        ErrorCode = errorCode;
        Message = message;
    }

    /// <summary>
    /// WorkflowResult と trace に記録する error code を取得します。
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// WorkflowResult に記録する説明文を取得します。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// timeout 失敗を表す値を作成します。
    /// </summary>
    public static StepCancellationFailure TimedOut(string stepName, TimeSpan timeout, string? message)
    {
        return new StepCancellationFailure(
            WorkflowErrorCodes.StepTimeout,
            message ?? $"Step '{stepName}' timed out after {timeout}.");
    }

    /// <summary>
    /// 外部キャンセル失敗を表す値を作成します。
    /// </summary>
    public static StepCancellationFailure Canceled(string? message)
    {
        return new StepCancellationFailure(
            WorkflowErrorCodes.StepCanceled,
            message ?? "Step was canceled.");
    }
}
