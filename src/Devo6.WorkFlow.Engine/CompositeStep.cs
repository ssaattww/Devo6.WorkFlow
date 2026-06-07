using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// composite entry を作成するための入口を提供します。
/// </summary>
public static class CompositeStep
{
    /// <summary>
    /// 指定した Entry 名で composite entry 定義を開始します。
    /// </summary>
    /// <param name="name">作成する短い Entry 名。</param>
    /// <param name="namespaceName">Entry の名前空間名。未指定の場合は名前空間なし Entry として扱います。</param>
    /// <returns>最初の Step を登録できる composite entry 定義。</returns>
    public static CompositeStepDefinition Define(string name, string? namespaceName = null)
    {
        return new CompositeStepDefinition(name, namespaceName);
    }
}

/// <summary>
/// 最初の Step を登録する前の composite entry 定義を表します。
/// </summary>
public sealed class CompositeStepDefinition
{
    /// <summary>
    /// composite entry 定義を初期化します。
    /// </summary>
    /// <param name="name">短い Entry 名。</param>
    /// <param name="namespaceName">Entry の名前空間名。</param>
    internal CompositeStepDefinition(string name, string? namespaceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (namespaceName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        }

        Name = name;
        NamespaceName = namespaceName;
        QualifiedName = CreateQualifiedName(name, namespaceName);
    }

    /// <summary>
    /// 短い Entry 名を取得します。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Entry の名前空間名を取得します。名前空間なし Entry の場合は null を返します。
    /// </summary>
    public string? NamespaceName { get; }

    /// <summary>
    /// Entry の完全修飾名を取得します。
    /// </summary>
    public string QualifiedName { get; }

    /// <summary>
    /// 最初の同期 Step を登録します。
    /// </summary>
    /// <typeparam name="TStep">実行する同期 Step 型。</typeparam>
    /// <typeparam name="TOut">同期 Step が返す出力型。</typeparam>
    /// <returns>拡張または実行できる composite step。</returns>
    public CompositeStep<TOut> Run<TStep, TOut>()
        where TStep : IStep<TOut>, new()
    {
        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, [StepRegistration.Create<TStep, TOut>()]);
    }

    /// <summary>
    /// 最初の非同期 Step を登録します。
    /// </summary>
    /// <typeparam name="TStep">実行する非同期 Step 型。</typeparam>
    /// <typeparam name="TOut">非同期 Step が返す出力型。</typeparam>
    /// <returns>拡張または実行できる composite step。</returns>
    public CompositeStep<TOut> RunAsync<TStep, TOut>()
        where TStep : IAsyncStep<TOut>, new()
    {
        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, [StepRegistration.CreateAsync<TStep, TOut>()]);
    }

    /// <summary>
    /// 短い Entry 名と名前空間名から完全修飾名を作成します。
    /// </summary>
    /// <param name="name">短い Entry 名。</param>
    /// <param name="namespaceName">Entry の名前空間名。</param>
    /// <returns>Entry の完全修飾名。</returns>
    private static string CreateQualifiedName(string name, string? namespaceName)
    {
        return namespaceName is null ? name : $"{namespaceName}.{name}";
    }
}

/// <summary>
/// 登録済み Step 列を実行できる composite entry を表します。
/// </summary>
/// <typeparam name="TOut">現在の末尾 Step が返す出力型。</typeparam>
public sealed class CompositeStep<TOut> : IStep<TOut>, IAsyncStep<TOut>
{
    private readonly IReadOnlyList<StepRegistration> steps;

    /// <summary>
    /// composite entry を初期化します。
    /// </summary>
    /// <param name="name">短い Entry 名。</param>
    /// <param name="namespaceName">Entry の名前空間名。</param>
    /// <param name="qualifiedName">Entry の完全修飾名。</param>
    /// <param name="steps">登録済み Step 列。</param>
    /// <param name="configType">Entry が要求する標準 Config 型。</param>
    internal CompositeStep(
        string name,
        string? namespaceName,
        string qualifiedName,
        IReadOnlyList<StepRegistration> steps,
        Type? configType = null)
    {
        Name = name;
        NamespaceName = namespaceName;
        QualifiedName = qualifiedName;
        this.steps = steps.ToArray();
        ConfigType = configType;
    }

    /// <summary>
    /// 短い Entry 名を取得します。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Entry の名前空間名を取得します。名前空間なし Entry の場合は null を返します。
    /// </summary>
    public string? NamespaceName { get; }

    /// <summary>
    /// Entry の完全修飾名を取得します。
    /// </summary>
    public string QualifiedName { get; }

    /// <summary>
    /// Entry が要求する標準 Config 型を取得します。未指定の場合は null を返します。
    /// </summary>
    public Type? ConfigType { get; }

    /// <summary>
    /// 同期 Step を末尾へ追加します。
    /// </summary>
    /// <typeparam name="TStep">追加する同期 Step 型。</typeparam>
    /// <typeparam name="TNext">追加した Step が返す出力型。</typeparam>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> Run<TStep, TNext>()
        where TStep : IStep<TNext>, new()
    {
        return new CompositeStep<TNext>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.Create<TStep, TNext>()),
            ConfigType);
    }

    /// <summary>
    /// 非同期 Step を末尾へ追加します。
    /// </summary>
    /// <typeparam name="TStep">追加する非同期 Step 型。</typeparam>
    /// <typeparam name="TNext">追加した Step が返す出力型。</typeparam>
    /// <returns>末尾 Step の出力型を更新した composite step。</returns>
    public CompositeStep<TNext> RunAsync<TStep, TNext>()
        where TStep : IAsyncStep<TNext>, new()
    {
        return new CompositeStep<TNext>(
            Name,
            NamespaceName,
            QualifiedName,
            Append(StepRegistration.CreateAsync<TStep, TNext>()),
            ConfigType);
    }

    /// <summary>
    /// Entry が要求する標準 Config 型を metadata として設定します。
    /// </summary>
    /// <typeparam name="TConfig">StepContext に登録する標準 Config 型。</typeparam>
    /// <returns>標準 Config 型 metadata を持つ composite entry。</returns>
    public CompositeStep<TOut> WithConfig<TConfig>()
    {
        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, steps, typeof(TConfig));
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す型付き値を登録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <returns>型付き値を生成する現在の composite step。</returns>
    public CompositeStep<TOut> Produce<TValue>(Func<TOut, TValue> selector)
    {
        return AddProducer(selector, null, ExecutionTraceValueSource.Produce, null);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す名前付き値を登録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="name">登録値の名前。</param>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <returns>名前付き値を生成する現在の composite step。</returns>
    public CompositeStep<TOut> Produce<TValue>(string name, Func<TOut, TValue> selector)
    {
        return AddProducer(selector, name, ExecutionTraceValueSource.Produce, null);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す型付き値を登録し、trace value を記録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>型付き値を生成する現在の composite step。</returns>
    public CompositeStep<TOut> Produce<TValue>(Func<TOut, TValue> selector, TraceValueCapture capture)
    {
        return AddProducer(selector, null, ExecutionTraceValueSource.Produce, capture);
    }

    /// <summary>
    /// 現在の Step 出力から後続 Step へ渡す名前付き値を登録し、trace value を記録します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="name">登録値の名前。</param>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>名前付き値を生成する現在の composite step。</returns>
    public CompositeStep<TOut> Produce<TValue>(string name, Func<TOut, TValue> selector, TraceValueCapture capture)
    {
        return AddProducer(selector, name, ExecutionTraceValueSource.Produce, capture);
    }

    /// <summary>
    /// 現在の Step 出力を後続 Step へ渡す値として登録します。
    /// </summary>
    /// <returns>現在の Step 出力を生成値として登録する composite step。</returns>
    public CompositeStep<TOut> StoreAs()
    {
        return AddProducer<TOut>(value => value, null, ExecutionTraceValueSource.StoreAs, null);
    }

    /// <summary>
    /// 現在の Step 出力を後続 Step へ渡す値として登録し、trace value を記録します。
    /// </summary>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>現在の Step 出力を生成値として登録する composite step。</returns>
    public CompositeStep<TOut> StoreAs(TraceValueCapture capture)
    {
        return AddProducer<TOut>(value => value, null, ExecutionTraceValueSource.StoreAs, capture);
    }

    /// <summary>
    /// 現在の Step に登録された値生成処理を削除します。
    /// </summary>
    /// <returns>現在の Step が値を生成しない composite step。</returns>
    public CompositeStep<TOut> Discard()
    {
        return WithCurrentStep(CurrentStep.ClearProducers());
    }

    /// <summary>
    /// 指定された入力値で composite step を同期実行します。
    /// </summary>
    /// <param name="input">最初の Step へ渡す入力値。</param>
    /// <returns>末尾 Step が返した出力値。</returns>
    public TOut Execute(StepInput input)
    {
        return ExecuteAsync(input, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 指定された入力値で composite step を非同期実行します。
    /// </summary>
    /// <param name="input">最初の Step へ渡す入力値。</param>
    /// <param name="cancellationToken">非同期 Step へ渡す cancellation token。</param>
    /// <returns>末尾 Step が返した出力値。</returns>
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
    /// engine 経路で composite entry を同期実行し、結果と trace を返します。
    /// </summary>
    /// <param name="options">実行時の依存関係。null の場合は既定値を使います。</param>
    /// <returns>成功、失敗、記録した trace を含む workflow 結果。</returns>
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

        if (options.StandardConfig is not null)
        {
            SetStandardConfig(context, ConfigType, options.StandardConfig);
        }

        var input = new StepInput(context);
        object? currentValue = default(TOut);

        using IDisposable? entryScope = engineLogger.BeginScope(new Dictionary<string, object?>
        {
            ["EntryName"] = QualifiedName,
            ["Attempt"] = 1,
        });

        engineLogger.LogInformation("Entry started");

        int maxAttempts = GetMaxAttempts(options.Retry);

        for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
        {
            StepRegistration step = steps[stepIndex];
            var succeededAttempt = 1;
            Stopwatch? succeededAttemptStopwatch = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                using IDisposable? stepScope = engineLogger.BeginScope(new Dictionary<string, object?>
                {
                    ["EntryName"] = QualifiedName,
                    ["StepName"] = step.Name,
                    ["Attempt"] = attempt,
                });

                engineLogger.LogInformation("Step started for attempt {Attempt}", attempt);

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
                            attempt,
                            cancellationFailure,
                            engineLogger);
                    }

                    succeededAttempt = attempt;
                    succeededAttemptStopwatch = stopwatch;
                    break;
                }
                catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                {
                    stopwatch.Stop();

                    return ToCancellationWorkflowResult(
                        traceSteps,
                        step,
                        stopwatch.Elapsed,
                        attempt,
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
                        attempt,
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
                        WorkflowErrorCodes.StepExecutionFailed,
                        attempt));

                    if (attempt < maxAttempts)
                    {
                        engineLogger.LogWarning(
                            exception,
                            "Step attempt {Attempt} failed with error code {ErrorCode}; retrying",
                            attempt,
                            WorkflowErrorCodes.StepExecutionFailed);
                        continue;
                    }

                    engineLogger.LogError(
                        exception,
                        "Step failed after attempt {Attempt} with error code {ErrorCode}",
                        attempt,
                        WorkflowErrorCodes.StepExecutionFailed);
                    engineLogger.LogError(
                        exception,
                        "Entry failed after attempt {Attempt} with error code {ErrorCode}",
                        attempt,
                        WorkflowErrorCodes.StepExecutionFailed);

                    return new WorkflowResult
                    {
                        EntryName = QualifiedName,
                        Succeeded = false,
                        ErrorCode = WorkflowErrorCodes.StepExecutionFailed,
                        ErrorMessage = exception.Message,
                        Trace = new ExecutionTrace(traceSteps),
                    };
                }
            }

            if (succeededAttemptStopwatch is null)
            {
                throw new InvalidOperationException("Step retry loop completed without a terminal result.");
            }

            using IDisposable? produceScope = engineLogger.BeginScope(new Dictionary<string, object?>
            {
                ["EntryName"] = QualifiedName,
                ["StepName"] = step.Name,
                ["Attempt"] = succeededAttempt,
            });

            try
            {
                IReadOnlyList<ExecutionTraceValue> producedValues = step.Produce(input, currentValue);
                succeededAttemptStopwatch.Stop();
                traceSteps.Add(new ExecutionTraceStep(
                    step.Name,
                    ExecutionTraceStepStatus.Succeeded,
                    succeededAttemptStopwatch.Elapsed,
                    null,
                    succeededAttempt,
                    producedValues));
                engineLogger.LogInformation("Step succeeded on attempt {Attempt}", succeededAttempt);
            }
            catch (Exception exception)
            {
                succeededAttemptStopwatch.Stop();
                traceSteps.Add(new ExecutionTraceStep(
                    step.Name,
                    ExecutionTraceStepStatus.Failed,
                    succeededAttemptStopwatch.Elapsed,
                    WorkflowErrorCodes.StepExecutionFailed,
                    succeededAttempt));
                engineLogger.LogError(
                    exception,
                    "Step post-processing failed on attempt {Attempt} with error code {ErrorCode}",
                    succeededAttempt,
                    WorkflowErrorCodes.StepExecutionFailed);
                engineLogger.LogError(
                    exception,
                    "Entry failed on attempt {Attempt} with error code {ErrorCode}",
                    succeededAttempt,
                    WorkflowErrorCodes.StepExecutionFailed);

                return new WorkflowResult
                {
                    EntryName = QualifiedName,
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
            EntryName = QualifiedName,
            Succeeded = true,
            Trace = new ExecutionTrace(traceSteps),
        };
    }

    /// <summary>
    /// retry 設定から Step 本体の最大試行回数を取得します。
    /// </summary>
    /// <param name="retry">workflow 実行に適用する retry 設定。</param>
    /// <returns>Step 本体の最大試行回数。</returns>
    private static int GetMaxAttempts(RetryOptions? retry)
    {
        if (retry is null || retry.MaxAttempts <= 1)
        {
            return 1;
        }

        return retry.MaxAttempts;
    }

    /// <summary>
    /// Step 実行用に timeout と外部キャンセルを合成した token を作成します。
    /// </summary>
    /// <param name="stepTimeout">Step ごとに適用する timeout。</param>
    /// <param name="cancellationToken">workflow 実行へ渡された外部キャンセル用 token。</param>
    /// <returns>Step 実行中に使う cancellation 状態。</returns>
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
    /// <param name="step">完了した Step 登録情報。</param>
    /// <param name="stepCancellation">Step 実行に使った cancellation 状態。</param>
    /// <param name="externalCancellationToken">workflow 実行へ渡された外部キャンセル用 token。</param>
    /// <returns>cancellation 系の失敗情報。失敗として扱わない場合は null。</returns>
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
    /// <param name="traceSteps">これまでに記録した trace step。</param>
    /// <param name="step">失敗した Step 登録情報。</param>
    /// <param name="elapsed">失敗までの経過時間。</param>
    /// <param name="attempt">失敗した試行番号。</param>
    /// <param name="failure">cancellation 系の失敗情報。</param>
    /// <param name="engineLogger">engine 用 logger。</param>
    /// <returns>cancellation 系失敗を表す workflow 結果。</returns>
    private WorkflowResult ToCancellationWorkflowResult(
        List<ExecutionTraceStep> traceSteps,
        StepRegistration step,
        TimeSpan elapsed,
        int attempt,
        StepCancellationFailure failure,
        ILogger engineLogger)
    {
        traceSteps.Add(new ExecutionTraceStep(
            step.Name,
            ExecutionTraceStepStatus.Failed,
            elapsed,
            failure.ErrorCode,
            attempt));
        engineLogger.LogWarning(
            "Step stopped on attempt {Attempt} with error code {ErrorCode}",
            attempt,
            failure.ErrorCode);
        engineLogger.LogWarning(
            "Entry failed on attempt {Attempt} with error code {ErrorCode}",
            attempt,
            failure.ErrorCode);

        return new WorkflowResult
        {
            EntryName = QualifiedName,
            Succeeded = false,
            ErrorCode = failure.ErrorCode,
            ErrorMessage = failure.Message,
            Trace = new ExecutionTrace(traceSteps),
        };
    }

    /// <summary>
    /// 値生成処理を追加または削除する対象の現在 Step を取得します。
    /// </summary>
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

    /// <summary>
    /// 現在の Step に値生成処理を追加します。
    /// </summary>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="name">登録値の名前。</param>
    /// <param name="source">trace value に記録する生成元。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>値生成処理を追加した composite step。</returns>
    private CompositeStep<TOut> AddProducer<TValue>(
        Func<TOut, TValue> selector,
        string? name,
        ExecutionTraceValueSource source,
        TraceValueCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return WithCurrentStep(CurrentStep.AddProducer(
            StepValueProducer.Create(selector, name, source, capture)));
    }

    /// <summary>
    /// 末尾へ Step 登録情報を追加した配列を作成します。
    /// </summary>
    /// <param name="registration">追加する Step 登録情報。</param>
    /// <returns>追加後の Step 登録情報列。</returns>
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

    /// <summary>
    /// 現在の Step 登録情報を差し替えた composite step を作成します。
    /// </summary>
    /// <param name="registration">差し替え後の Step 登録情報。</param>
    /// <returns>現在の Step 登録情報を差し替えた composite step。</returns>
    private CompositeStep<TOut> WithCurrentStep(StepRegistration registration)
    {
        StepRegistration[] nextSteps = steps.ToArray();
        nextSteps[^1] = registration;

        return new CompositeStep<TOut>(Name, NamespaceName, QualifiedName, nextSteps, ConfigType);
    }

    /// <summary>
    /// 標準 Config instance を宣言された Config 型で StepContext に登録します。
    /// </summary>
    /// <param name="context">標準 Config instance を登録する StepContext。</param>
    /// <param name="configType">宣言された標準 Config 型。</param>
    /// <param name="standardConfig">登録する標準 Config instance。</param>
    private static void SetStandardConfig(StepContext context, Type? configType, object standardConfig)
    {
        Type targetType = configType ?? standardConfig.GetType();
        typeof(StepContext)
            .GetMethods()
            .Single(method => method.Name == nameof(StepContext.Set)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 1)
            .MakeGenericMethod(targetType)
            .Invoke(context, [standardConfig]);
    }
}

/// <summary>
/// 1 つの Step 実行と値生成処理の登録情報を保持します。
/// </summary>
internal sealed class StepRegistration
{
    private readonly string name;
    private readonly Func<StepInput, CancellationToken, Task<object?>> executeAsync;
    private readonly IReadOnlyList<StepValueProducer> producers;

    /// <summary>
    /// Step 登録情報を初期化します。
    /// </summary>
    /// <param name="name">trace と log に記録する Step 名。</param>
    /// <param name="executeAsync">登録済み Step を実行する処理。</param>
    /// <param name="producers">Step 成功後に実行する値生成処理。</param>
    private StepRegistration(
        string name,
        Func<StepInput, CancellationToken, Task<object?>> executeAsync,
        IReadOnlyList<StepValueProducer> producers)
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
    /// <typeparam name="TStep">登録する同期 Step 型。</typeparam>
    /// <typeparam name="TOut">同期 Step が返す出力型。</typeparam>
    /// <returns>作成した Step 登録情報。</returns>
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
    /// <typeparam name="TStep">登録する非同期 Step 型。</typeparam>
    /// <typeparam name="TOut">非同期 Step が返す出力型。</typeparam>
    /// <returns>作成した Step 登録情報。</returns>
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
    /// <param name="input">Step へ渡す入力値。</param>
    /// <param name="cancellationToken">Step へ渡す cancellation token。</param>
    /// <returns>Step が返した出力値。</returns>
    public Task<object?> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
    {
        return executeAsync(input, cancellationToken);
    }

    /// <summary>
    /// Step 成功後に入力へ値を追加する producer を加えます。
    /// </summary>
    /// <param name="producer">追加する値生成処理。</param>
    /// <returns>値生成処理を追加した Step 登録情報。</returns>
    public StepRegistration AddProducer(StepValueProducer producer)
    {
        ArgumentNullException.ThrowIfNull(producer);

        StepValueProducer[] nextProducers = new StepValueProducer[producers.Count + 1];

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
    /// <returns>値生成処理を削除した Step 登録情報。</returns>
    public StepRegistration ClearProducers()
    {
        return new StepRegistration(name, executeAsync, []);
    }

    /// <summary>
    /// 登録済み producer を実行し、成功した場合だけ trace value を返します。
    /// </summary>
    /// <param name="input">値を追加する StepInput。</param>
    /// <param name="value">現在の Step 出力。</param>
    /// <returns>作成された trace value の一覧。</returns>
    public IReadOnlyList<ExecutionTraceValue> Produce(StepInput input, object? value)
    {
        var producedValues = new List<ExecutionTraceValue>();

        foreach (StepValueProducer producer in producers)
        {
            ExecutionTraceValue? producedValue = producer.Produce(input, value);
            if (producedValue is not null)
            {
                producedValues.Add(producedValue);
            }
        }

        return producedValues;
    }
}

/// <summary>
/// Step 出力から後続 Step 用の値と trace value を生成します。
/// </summary>
internal sealed class StepValueProducer
{
    private readonly Type valueType;
    private readonly string? name;
    private readonly ExecutionTraceValueSource source;
    private readonly TraceValueCapture? capture;
    private readonly Func<object?, object?> selectValue;
    private readonly Action<StepInput, object?> addValue;

    /// <summary>
    /// 値生成処理を初期化します。
    /// </summary>
    /// <param name="valueType">登録する値の型。</param>
    /// <param name="name">登録値の名前。</param>
    /// <param name="source">trace value に記録する生成元。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <param name="selectValue">Step 出力から登録値を選択する処理。</param>
    /// <param name="addValue">選択済み値を StepInput へ登録する処理。</param>
    private StepValueProducer(
        Type valueType,
        string? name,
        ExecutionTraceValueSource source,
        TraceValueCapture? capture,
        Func<object?, object?> selectValue,
        Action<StepInput, object?> addValue)
    {
        this.valueType = valueType;
        this.name = name;
        this.source = source;
        this.capture = capture;
        this.selectValue = selectValue;
        this.addValue = addValue;
    }

    /// <summary>
    /// 型付き selector から値生成処理を作成します。
    /// </summary>
    /// <typeparam name="TCurrent">現在の Step 出力型。</typeparam>
    /// <typeparam name="TValue">登録する値の型。</typeparam>
    /// <param name="selector">現在の Step 出力から登録値を選択する処理。</param>
    /// <param name="name">登録値の名前。</param>
    /// <param name="source">trace value に記録する生成元。</param>
    /// <param name="capture">trace value の記録方法。</param>
    /// <returns>作成した値生成処理。</returns>
    public static StepValueProducer Create<TCurrent, TValue>(
        Func<TCurrent, TValue> selector,
        string? name,
        ExecutionTraceValueSource source,
        TraceValueCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ValidateCapture(capture);

        return new StepValueProducer(
            typeof(TValue),
            name,
            source,
            capture,
            value => selector((TCurrent)value!),
            (input, producedValue) =>
            {
                if (name is null)
                {
                    input.Add((TValue)producedValue!);
                    return;
                }

                input.Add(name, (TValue)producedValue!);
            });
    }

    /// <summary>
    /// StepInput へ値を追加し、設定されている場合だけ trace value を作成します。
    /// </summary>
    /// <param name="input">値を追加する StepInput。</param>
    /// <param name="stepOutput">現在の Step 出力。</param>
    /// <returns>作成した trace value。capture が未指定の場合は null。</returns>
    public ExecutionTraceValue? Produce(StepInput input, object? stepOutput)
    {
        object? producedValue = selectValue(stepOutput);
        addValue(input, producedValue);

        if (capture is null)
        {
            return null;
        }

        return CreateTraceValue(producedValue, capture.Value);
    }

    /// <summary>
    /// trace value の記録方法が有効値か確認します。
    /// </summary>
    /// <param name="capture">確認する記録方法。</param>
    private static void ValidateCapture(TraceValueCapture? capture)
    {
        if (capture is null
            or TraceValueCapture.Serialized
            or TraceValueCapture.Redacted)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(capture), capture, "Unsupported trace value capture.");
    }

    /// <summary>
    /// 登録済み値から trace value を作成します。
    /// </summary>
    /// <param name="producedValue">trace に記録する対象値。</param>
    /// <param name="traceCapture">trace value の記録方法。</param>
    /// <returns>作成した trace value。</returns>
    private ExecutionTraceValue CreateTraceValue(object? producedValue, TraceValueCapture traceCapture)
    {
        string typeName = valueType.FullName ?? valueType.Name;

        if (traceCapture == TraceValueCapture.Redacted)
        {
            return new ExecutionTraceValue(
                typeName,
                name,
                source,
                ExecutionTraceValueCaptureStatus.Redacted,
                null,
                null);
        }

        try
        {
            return new ExecutionTraceValue(
                typeName,
                name,
                source,
                ExecutionTraceValueCaptureStatus.Serialized,
                JsonSerializer.Serialize(producedValue, valueType),
                null);
        }
        catch (Exception exception)
        {
            return new ExecutionTraceValue(
                typeName,
                name,
                source,
                ExecutionTraceValueCaptureStatus.NotSerializable,
                null,
                BuildSerializationFailureReason(exception));
        }
    }

    /// <summary>
    /// 直列化失敗の理由を trace value 用の短い文字列に変換します。
    /// </summary>
    /// <param name="exception">直列化中に発生した例外。</param>
    /// <returns>利用者へ返す直列化失敗理由。</returns>
    private static string BuildSerializationFailureReason(Exception exception)
    {
        return $"Trace value serialization failed: {exception.GetType().Name}.";
    }
}

/// <summary>
/// Step 実行中に使う timeout と外部キャンセルの合成状態を保持します。
/// </summary>
internal sealed class StepExecutionCancellation : IDisposable
{
    private readonly CancellationTokenSource? timeoutSource;
    private readonly CancellationTokenSource? linkedSource;

    /// <summary>
    /// Step 実行中に使う cancellation token と所有する source を初期化します。
    /// </summary>
    /// <param name="token">Step へ渡す cancellation token。</param>
    /// <param name="timeout">設定された Step timeout。</param>
    /// <param name="timeoutSource">timeout 発火を管理する source。</param>
    /// <param name="linkedSource">外部キャンセルと timeout を合成した source。</param>
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

/// <summary>
/// timeout または外部キャンセルによる Step 失敗情報を表します。
/// </summary>
internal sealed class StepCancellationFailure
{
    /// <summary>
    /// cancellation 系の失敗情報を初期化します。
    /// </summary>
    /// <param name="errorCode">WorkflowResult と trace に記録する error code。</param>
    /// <param name="message">WorkflowResult に記録する説明文。</param>
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
    /// <param name="stepName">timeout した Step 名。</param>
    /// <param name="timeout">設定された Step timeout。</param>
    /// <param name="message">例外から取得した説明文。</param>
    /// <returns>timeout 失敗情報。</returns>
    public static StepCancellationFailure TimedOut(string stepName, TimeSpan timeout, string? message)
    {
        return new StepCancellationFailure(
            WorkflowErrorCodes.StepTimeout,
            message ?? $"Step '{stepName}' timed out after {timeout}.");
    }

    /// <summary>
    /// 外部キャンセル失敗を表す値を作成します。
    /// </summary>
    /// <param name="message">例外から取得した説明文。</param>
    /// <returns>外部キャンセル失敗情報。</returns>
    public static StepCancellationFailure Canceled(string? message)
    {
        return new StepCancellationFailure(
            WorkflowErrorCodes.StepCanceled,
            message ?? "Step was canceled.");
    }
}
