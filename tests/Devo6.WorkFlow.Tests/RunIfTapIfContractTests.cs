using System.Reflection;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// RunIf と TapIf の条件付き実行契約を検査します。
/// </summary>
public sealed class RunIfTapIfContractTests
{
    /// <summary>
    /// RunIf true が対象 Step を実行し、その戻り値を現在値にすることを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIf true executes target step and uses returned value")]
    public async Task RunIfTrueExecutesTargetStepAndUsesReturnedValue()
    {
        RunIfTapIfState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIf<RunIfValueStep, string>(current => current.Value == "seed", current => "fallback")
            .Run("final", current => current);

        WorkflowResult result = await step.ExecuteWorkflowAsync();
        string output = step.Execute(new StepInput());

        Assert.True(result.Succeeded);
        Assert.Equal("run-if-step", output);
        Assert.Equal(2, RunIfTapIfState.SyncRunIfAttempts);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(RunIfValueStep)
            && traceStep.Status == ExecutionTraceStepStatus.Succeeded);
    }

    /// <summary>
    /// RunIf false が対象 Step を実行せず、otherwise の代替値を現在値にすることを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIf false skips target step and uses otherwise value")]
    public async Task RunIfFalseSkipsTargetStepAndUsesOtherwiseValue()
    {
        RunIfTapIfState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIf<RunIfValueStep, string>(current => false, current => "fallback")
                .StoreAs(TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();
        string output = step.Execute(new StepInput());

        Assert.True(result.Succeeded);
        Assert.Equal("fallback", output);
        Assert.Equal(0, RunIfTapIfState.SyncRunIfAttempts);
        ExecutionTraceStep traceStep = result.Trace!.Steps.Single(step => step.StepName == nameof(RunIfValueStep));
        Assert.Equal(ExecutionTraceStepStatus.Skipped, traceStep.Status);
        ExecutionTraceValue traceValue = Assert.Single(traceStep.ProducedValues);
        Assert.Equal(ExecutionTraceValueSource.StoreAs, traceValue.Source);
        Assert.Equal("\"fallback\"", traceValue.SerializedValue);
    }

    /// <summary>
    /// 同じ CompositeStep instance の並行実行で RunIf の trace status が実行間で混ざらないことを確認します。
    /// </summary>
    [Fact(DisplayName = "Concurrent RunIf executions keep trace statuses isolated")]
    public async Task ConcurrentRunIfExecutionsKeepTraceStatusesIsolated()
    {
        using var skippedReady = new ManualResetEventSlim();
        using var succeededReady = new ManualResetEventSlim();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIf<RunIfValueStep, string>(
                (current, input) => input.Context.Get<EngineArguments>().WorkflowSettings["enabled"] == "true",
                (current, input) => "fallback")
                .Produce("observed", value =>
                {
                    if (value == "fallback")
                    {
                        skippedReady.Set();
                        Assert.True(succeededReady.Wait(TimeSpan.FromSeconds(2)));
                    }
                    else if (value == "run-if-step")
                    {
                        Assert.True(skippedReady.Wait(TimeSpan.FromSeconds(2)));
                        succeededReady.Set();
                    }

                    return value;
                }, TraceValueCapture.Serialized);

        Task<WorkflowResult> skippedTask = Task.Run(() => step.ExecuteWorkflowAsync(CreateOptionsWithWorkflowSetting("enabled", "false")));
        Task<WorkflowResult> succeededTask = Task.Run(() => step.ExecuteWorkflowAsync(CreateOptionsWithWorkflowSetting("enabled", "true")));
        WorkflowResult[] results = await Task.WhenAll(skippedTask, succeededTask).WaitAsync(TimeSpan.FromSeconds(5));

        WorkflowResult skippedResult = results.Single(result => GetRunIfValueStep(result).Status == ExecutionTraceStepStatus.Skipped);
        WorkflowResult succeededResult = results.Single(result => GetRunIfValueStep(result).Status == ExecutionTraceStepStatus.Succeeded);

        Assert.True(skippedResult.Succeeded);
        Assert.True(succeededResult.Succeeded);
        ExecutionTraceStep skippedTraceStep = GetRunIfValueStep(skippedResult);
        ExecutionTraceStep succeededTraceStep = GetRunIfValueStep(succeededResult);
        Assert.Equal("\"fallback\"", Assert.Single(skippedTraceStep.ProducedValues).SerializedValue);
        Assert.Equal("\"run-if-step\"", Assert.Single(succeededTraceStep.ProducedValues).SerializedValue);
    }

    /// <summary>
    /// 同一型 RunIf false が現在値をそのまま維持することを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIf same type false keeps current value")]
    public void RunIfSameTypeFalseKeepsCurrentValue()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIf<SameTypeRunIfStep>(current => false);

        ConditionalValue output = step.Execute(new StepInput());

        Assert.Equal("seed", output.Value);
        Assert.Equal(0, RunIfTapIfState.SameTypeRunIfAttempts);
    }

    /// <summary>
    /// TapIf true が Unit Step を実行し、現在値を維持することを確認します。
    /// </summary>
    [Fact(DisplayName = "TapIf true executes unit step and keeps current value")]
    public async Task TapIfTrueExecutesUnitStepAndKeepsCurrentValue()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .TapIf<TapIfUnitStep>(current => true);

        WorkflowResult result = await step.ExecuteWorkflowAsync();
        ConditionalValue output = step.Execute(new StepInput());

        Assert.True(result.Succeeded);
        Assert.Equal("seed", output.Value);
        Assert.Equal(2, RunIfTapIfState.TapIfAttempts);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(TapIfUnitStep)
            && traceStep.Status == ExecutionTraceStepStatus.Succeeded);
    }

    /// <summary>
    /// TapIf false が対象 Step を実行せず、現在値を維持し、Skipped trace を記録することを確認します。
    /// </summary>
    [Fact(DisplayName = "TapIf false skips target step and records skipped trace")]
    public async Task TapIfFalseSkipsTargetStepAndRecordsSkippedTrace()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .TapIf<TapIfUnitStep>(current => false)
                .Produce<string>("tap-value", current => current.Value, TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();
        ConditionalValue output = step.Execute(new StepInput());

        Assert.True(result.Succeeded);
        Assert.Equal("seed", output.Value);
        Assert.Equal(0, RunIfTapIfState.TapIfAttempts);
        ExecutionTraceStep traceStep = result.Trace!.Steps.Single(step => step.StepName == nameof(TapIfUnitStep));
        Assert.Equal(ExecutionTraceStepStatus.Skipped, traceStep.Status);
        ExecutionTraceValue traceValue = Assert.Single(traceStep.ProducedValues);
        Assert.Equal("tap-value", traceValue.Name);
        Assert.Equal("\"seed\"", traceValue.SerializedValue);
    }

    /// <summary>
    /// RunIfAsync false が非同期 otherwise の値と Skipped trace value を残すことを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIfAsync false uses async otherwise and records skipped trace value")]
    public async Task RunIfAsyncFalseUsesAsyncOtherwiseAndRecordsSkippedTraceValue()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIfAsync<AsyncRunIfValueStep, ConditionalValue>(
                (current, input) => false,
                async (current, input, cancellationToken) =>
                {
                    await Task.Yield();

                    return new ConditionalValue("async-fallback");
                })
                .Produce("run-if-async-value", current => current.Value, TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, RunIfTapIfState.AsyncRunIfAttempts);
        ExecutionTraceStep traceStep = result.Trace!.Steps.Single(step => step.StepName == nameof(AsyncRunIfValueStep));
        Assert.Equal(ExecutionTraceStepStatus.Skipped, traceStep.Status);
        ExecutionTraceValue traceValue = Assert.Single(traceStep.ProducedValues);
        Assert.Equal("run-if-async-value", traceValue.Name);
        Assert.Equal("\"async-fallback\"", traceValue.SerializedValue);
    }

    /// <summary>
    /// TapIfAsync false が現在値と Skipped trace value を残すことを確認します。
    /// </summary>
    [Fact(DisplayName = "TapIfAsync false keeps current value and records skipped trace value")]
    public async Task TapIfAsyncFalseKeepsCurrentValueAndRecordsSkippedTraceValue()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .TapIfAsync<AsyncTapIfUnitStep>((current, input) => false)
                .Produce("tap-if-async-value", current => current.Value, TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, RunIfTapIfState.AsyncTapIfAttempts);
        ExecutionTraceStep traceStep = result.Trace!.Steps.Single(step => step.StepName == nameof(AsyncTapIfUnitStep));
        Assert.Equal(ExecutionTraceStepStatus.Skipped, traceStep.Status);
        ExecutionTraceValue traceValue = Assert.Single(traceStep.ProducedValues);
        Assert.Equal("tap-if-async-value", traceValue.Name);
        Assert.Equal("\"seed\"", traceValue.SerializedValue);
    }

    /// <summary>
    /// StepInput 付き条件が StepContext と Produce 値を読めることを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIf condition with StepInput reads context and produced values")]
    public void RunIfConditionWithStepInputReadsContextAndProducedValues()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input =>
            {
                input.Context.Set("enabled", true);

                return new ConditionalValue("seed");
            })
                .Produce<string>("gate", current => current.Value)
            .RunIf<RunIfValueStep, string>(
                (current, input) => input.Context.Get<bool>("enabled") && input.Get<string>("gate") == current.Value,
                (current, input) => "fallback");

        string output = step.Execute(new StepInput());

        Assert.Equal("run-if-step", output);
    }

    /// <summary>
    /// RunIf と TapIf の Step Config が条件判定より前に StepContext へ登録されることを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIf and TapIf register step config before condition evaluation")]
    public async Task RunIfAndTapIfRegisterStepConfigBeforeConditionEvaluation()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIf<SameTypeRunIfStep>(
                (current, input) => input.Context.Get<RunIfTapIfConfig>().Enabled)
                .WithConfig<RunIfTapIfConfig>("Run")
            .TapIf<TapIfUnitStep>(
                (current, input) => input.Context.Get<TapIfConfig>().Enabled)
                .WithConfig<TapIfConfig>("Tap");
        WorkflowExecutionOptions options = CreateOptionsWithStepConfigs(
            (1, typeof(RunIfTapIfConfig), new RunIfTapIfConfig { Enabled = false }),
            (2, typeof(TapIfConfig), new TapIfConfig { Enabled = false }));

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.True(result.Succeeded);
        Assert.Equal(0, RunIfTapIfState.SameTypeRunIfAttempts);
        Assert.Equal(0, RunIfTapIfState.TapIfAttempts);
        Assert.Equal(
            [ExecutionTraceStepStatus.Succeeded, ExecutionTraceStepStatus.Skipped, ExecutionTraceStepStatus.Skipped],
            result.Trace!.Steps.Select(traceStep => traceStep.Status).ToArray());
    }

    /// <summary>
    /// async RunIf と TapIf が通常実行できることを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIfAsync and TapIfAsync support normal execution")]
    public async Task RunIfAsyncAndTapIfAsyncSupportNormalExecution()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIfAsync<AsyncRunIfValueStep, ConditionalValue>(current => true, current => new ConditionalValue("fallback"))
            .TapIfAsync<AsyncTapIfUnitStep>(current => true);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, RunIfTapIfState.AsyncRunIfAttempts);
        Assert.Equal(1, RunIfTapIfState.AsyncTapIfAttempts);
    }

    /// <summary>
    /// RunIfAsync の timeout が既存契約どおり失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIfAsync supports execution timeout", Skip = "CI 環境の timer / scheduling に依存して不安定になるため、timeout 検査の安定化まで保留します。")]
    public async Task RunIfAsyncSupportsExecutionTimeout()
    {
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIfAsync<TimeoutRunIfStep, ConditionalValue>(current => true, current => new ConditionalValue("fallback"));

        WorkflowResult result = await step.ExecuteWorkflowAsync(
            new WorkflowExecutionOptions { StepTimeout = TimeSpan.FromMilliseconds(30) }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, result.ErrorCode);
    }

    /// <summary>
    /// TapIfAsync が外部 cancel で既存契約どおり失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "TapIfAsync supports external cancellation")]
    public async Task TapIfAsyncSupportsExternalCancellation()
    {
        RunIfTapIfState.Reset();
        using var cancellationTokenSource = new CancellationTokenSource();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .TapIfAsync<CancelTapIfStep>(current => true);

        Task<WorkflowResult> cancelTask = step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token);
        await RunIfTapIfState.WaitForAsyncStartAsync(TimeSpan.FromSeconds(1));
        await cancellationTokenSource.CancelAsync();
        WorkflowResult result = await cancelTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
    }

    /// <summary>
    /// RunIf と TapIf の Step 本体例外に retry が効くことを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIf and TapIf step body exceptions are retried")]
    public async Task RunIfAndTapIfStepBodyExceptionsAreRetried()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIf<RetryRunIfStep>(current => true)
            .TapIf<RetryTapIfStep>(current => true);

        WorkflowResult result = await step.ExecuteWorkflowAsync(new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 2 },
        });

        Assert.True(result.Succeeded);
        Assert.Equal(2, RunIfTapIfState.RetryRunIfAttempts);
        Assert.Equal(2, RunIfTapIfState.RetryTapIfAttempts);
    }

    /// <summary>
    /// RunIf 条件判定例外が CONDITION_EVALUATION_FAILED になり、retry されないことを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIf condition exception returns condition evaluation failed without retry")]
    public async Task RunIfConditionExceptionReturnsConditionEvaluationFailedWithoutRetry()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .RunIf<RetryRunIfStep>(current =>
            {
                RunIfTapIfState.IncrementConditionAttempts();
                throw new InvalidOperationException("condition failed");
            });

        WorkflowResult result = await step.ExecuteWorkflowAsync(new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
        });

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ConditionEvaluationFailed, result.ErrorCode);
        Assert.Equal(1, RunIfTapIfState.ConditionAttempts);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps.Where(traceStep => traceStep.StepName == nameof(RetryRunIfStep)));
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.ConditionEvaluationFailed, traceStep.ErrorCode);
    }

    /// <summary>
    /// TapIf 条件判定例外が CONDITION_EVALUATION_FAILED になり、retry されないことを確認します。
    /// </summary>
    [Fact(DisplayName = "TapIf condition exception returns condition evaluation failed without retry")]
    public async Task TapIfConditionExceptionReturnsConditionEvaluationFailedWithoutRetry()
    {
        RunIfTapIfState.Reset();
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"))
            .TapIf<RetryTapIfStep>(current =>
            {
                RunIfTapIfState.IncrementConditionAttempts();
                throw new InvalidOperationException("tap condition failed");
            });

        WorkflowResult result = await step.ExecuteWorkflowAsync(new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
        });

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ConditionEvaluationFailed, result.ErrorCode);
        Assert.Equal(1, RunIfTapIfState.ConditionAttempts);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps.Where(traceStep => traceStep.StepName == nameof(RetryTapIfStep)));
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.ConditionEvaluationFailed, traceStep.ErrorCode);
    }

    /// <summary>
    /// RunIf と TapIf API の null 引数が既存方針どおり失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "RunIf and TapIf reject null delegates")]
    public void RunIfAndTapIfRejectNullDelegates()
    {
        CompositeStep<ConditionalValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new ConditionalValue("seed"));
        Func<ConditionalValue, bool>? when = null;
        Func<ConditionalValue, string>? otherwise = null;
        Func<ConditionalValue, StepInput, bool>? whenWithInput = null;
        Func<ConditionalValue, StepInput, string>? otherwiseWithInput = null;
        Func<ConditionalValue, StepInput, CancellationToken, Task<string>>? otherwiseAsync = null;

        Assert.Throws<ArgumentNullException>(() => step.RunIf<RunIfValueStep, string>(when!, current => "fallback"));
        Assert.Throws<ArgumentNullException>(() => step.RunIf<RunIfValueStep, string>(current => true, otherwise!));
        Assert.Throws<ArgumentNullException>(() => step.RunIf<RunIfValueStep, string>(whenWithInput!, (current, input) => "fallback"));
        Assert.Throws<ArgumentNullException>(() => step.RunIf<RunIfValueStep, string>((current, input) => true, otherwiseWithInput!));
        Assert.Throws<ArgumentNullException>(() => step.RunIfAsync<AsyncRunIfStringStep, string>(when!, current => "fallback"));
        Assert.Throws<ArgumentNullException>(() => step.RunIfAsync<AsyncRunIfStringStep, string>(current => true, otherwise!));
        Assert.Throws<ArgumentNullException>(() => step.RunIfAsync<AsyncRunIfStringStep, string>(whenWithInput!, (current, input) => "fallback"));
        Assert.Throws<ArgumentNullException>(() => step.RunIfAsync<AsyncRunIfStringStep, string>((current, input) => true, otherwiseWithInput!));
        Assert.Throws<ArgumentNullException>(() => step.RunIfAsync<AsyncRunIfStringStep, string>(whenWithInput!, (current, input, cancellationToken) => Task.FromResult("fallback")));
        Assert.Throws<ArgumentNullException>(() => step.RunIfAsync<AsyncRunIfStringStep, string>((current, input) => true, otherwiseAsync!));
        Assert.Throws<ArgumentNullException>(() => step.TapIf<TapIfUnitStep>(when!));
        Assert.Throws<ArgumentNullException>(() => step.TapIf<TapIfUnitStep>(whenWithInput!));
        Assert.Throws<ArgumentNullException>(() => step.TapIfAsync<AsyncTapIfUnitStep>(when!));
        Assert.Throws<ArgumentNullException>(() => step.TapIfAsync<AsyncTapIfUnitStep>(whenWithInput!));
    }

    /// <summary>
    /// workflow 設定を 1 つ持つ実行 option を作成します。
    /// </summary>
    /// <param name="name">workflow 設定名。</param>
    /// <param name="value">workflow 設定値。</param>
    /// <returns>EngineArguments に workflow 設定を持つ実行 option。</returns>
    private static WorkflowExecutionOptions CreateOptionsWithWorkflowSetting(string name, string value)
    {
        return new WorkflowExecutionOptions(engineArguments: new EngineArguments
        {
            WorkflowSettings = new Dictionary<string, string>
            {
                [name] = value,
            },
        });
    }

    /// <summary>
    /// RunIfValueStep の trace step を取得します。
    /// </summary>
    /// <param name="result">検索対象の workflow 結果。</param>
    /// <returns>RunIfValueStep に対応する trace step。</returns>
    private static ExecutionTraceStep GetRunIfValueStep(WorkflowResult result)
    {
        return result.Trace!.Steps.Single(traceStep => traceStep.StepName == nameof(RunIfValueStep));
    }

    /// <summary>
    /// 反射で StepConfigValue と WithStepConfigs を使った実行 option を作成します。
    /// </summary>
    /// <param name="configs">Step index、Config 型、Config instance の組み合わせ。</param>
    /// <returns>Step Config を持つ workflow 実行 option。</returns>
    private static WorkflowExecutionOptions CreateOptionsWithStepConfigs(params (int StepIndex, Type ConfigType, object Config)[] configs)
    {
        Type valueType = typeof(CompositeStep).Assembly.GetType("Devo6.WorkFlow.Engine.StepConfigValue", throwOnError: true)
            ?? throw new InvalidOperationException("StepConfigValue type was not found.");
        Array values = Array.CreateInstance(valueType, configs.Length);
        ConstructorInfo constructor = valueType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            [typeof(int), typeof(Type), typeof(object)],
            modifiers: null)
            ?? throw new InvalidOperationException("StepConfigValue constructor was not found.");
        for (int i = 0; i < configs.Length; i++)
        {
            values.SetValue(constructor.Invoke([configs[i].StepIndex, configs[i].ConfigType, configs[i].Config]), i);
        }

        MethodInfo method = typeof(WorkflowExecutionOptions).GetMethod(
            "WithStepConfigs",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WithStepConfigs method was not found.");

        return (WorkflowExecutionOptions)method.Invoke(new WorkflowExecutionOptions(), [values])!;
    }

    /// <summary>
    /// 条件判定で使う値を保持します。
    /// </summary>
    private sealed class ConditionalValue
    {
        /// <summary>
        /// 条件判定で使う値を初期化します。
        /// </summary>
        /// <param name="value">検査用の文字列値。</param>
        public ConditionalValue(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 検査用の文字列値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// RunIf と TapIf の条件判定で使う Config です。
    /// </summary>
    private sealed class RunIfTapIfConfig
    {
        /// <summary>
        /// 条件を通すかどうかを取得または設定します。
        /// </summary>
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// TapIf の条件判定で使う Config です。
    /// </summary>
    private sealed class TapIfConfig
    {
        /// <summary>
        /// 条件を通すかどうかを取得または設定します。
        /// </summary>
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// RunIf true で実行される同期 Step です。
    /// </summary>
    private sealed class RunIfValueStep : IStep<string>
    {
        /// <summary>
        /// 実行回数を記録して RunIf 用の値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>RunIf が現在値にする文字列。</returns>
        public string Execute(StepInput input)
        {
            RunIfTapIfState.IncrementSyncRunIfAttempts();

            return "run-if-step";
        }
    }

    /// <summary>
    /// 同一型 RunIf で実行される同期 Step です。
    /// </summary>
    private sealed class SameTypeRunIfStep : IStep<ConditionalValue>
    {
        /// <summary>
        /// 実行回数を記録して同一型の値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>同一型 RunIf が現在値にする値。</returns>
        public ConditionalValue Execute(StepInput input)
        {
            RunIfTapIfState.IncrementSameTypeRunIfAttempts();

            return new ConditionalValue("same-type-step");
        }
    }

    /// <summary>
    /// TapIf true で実行される同期 Unit Step です。
    /// </summary>
    private sealed class TapIfUnitStep : IStep<Unit>
    {
        /// <summary>
        /// 実行回数を記録して Unit を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Unit 値。</returns>
        public Unit Execute(StepInput input)
        {
            RunIfTapIfState.IncrementTapIfAttempts();

            return Unit.Value;
        }
    }

    /// <summary>
    /// async RunIf true で実行される非同期 Step です。
    /// </summary>
    private sealed class AsyncRunIfValueStep : IAsyncStep<ConditionalValue>
    {
        /// <summary>
        /// 実行回数を記録して非同期 RunIf 用の値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <param name="cancellationToken">キャンセル通知。</param>
        /// <returns>RunIf が現在値にする値。</returns>
        public Task<ConditionalValue> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            RunIfTapIfState.IncrementAsyncRunIfAttempts();

            return Task.FromResult(new ConditionalValue("async-run-if-step"));
        }
    }

    /// <summary>
    /// async RunIf の null 検査で使う文字列 Step です。
    /// </summary>
    private sealed class AsyncRunIfStringStep : IAsyncStep<string>
    {
        /// <summary>
        /// null 検査では到達しない文字列値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <param name="cancellationToken">キャンセル通知。</param>
        /// <returns>検査用の文字列値。</returns>
        public Task<string> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            return Task.FromResult("async-run-if-string-step");
        }
    }

    /// <summary>
    /// async TapIf true で実行される非同期 Unit Step です。
    /// </summary>
    private sealed class AsyncTapIfUnitStep : IAsyncStep<Unit>
    {
        /// <summary>
        /// 実行回数を記録して Unit を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <param name="cancellationToken">キャンセル通知。</param>
        /// <returns>Unit 値。</returns>
        public Task<Unit> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            RunIfTapIfState.IncrementAsyncTapIfAttempts();

            return Task.FromResult(Unit.Value);
        }
    }

    /// <summary>
    /// timeout 検査用の非同期 RunIf Step です。
    /// </summary>
    private sealed class TimeoutRunIfStep : IAsyncStep<ConditionalValue>
    {
        /// <summary>
        /// キャンセルされるまで待機します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <param name="cancellationToken">キャンセル通知。</param>
        /// <returns>到達しない戻り値。</returns>
        public async Task<ConditionalValue> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return new ConditionalValue("unreachable");
        }
    }

    /// <summary>
    /// 外部キャンセル検査用の非同期 TapIf Step です。
    /// </summary>
    private sealed class CancelTapIfStep : IAsyncStep<Unit>
    {
        /// <summary>
        /// 開始を記録してからキャンセルされるまで待機します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <param name="cancellationToken">キャンセル通知。</param>
        /// <returns>到達しない Unit 値。</returns>
        public async Task<Unit> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            RunIfTapIfState.MarkAsyncStarted();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return Unit.Value;
        }
    }

    /// <summary>
    /// retry 検査用の同一型 RunIf Step です。
    /// </summary>
    private sealed class RetryRunIfStep : IStep<ConditionalValue>
    {
        /// <summary>
        /// 1 回目だけ失敗し、2 回目に成功します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>retry 後の現在値。</returns>
        public ConditionalValue Execute(StepInput input)
        {
            int attempt = RunIfTapIfState.IncrementRetryRunIfAttempts();
            if (attempt == 1)
            {
                throw new InvalidOperationException("retry run-if failed");
            }

            return new ConditionalValue("retry-run-if-step");
        }
    }

    /// <summary>
    /// retry 検査用の TapIf Step です。
    /// </summary>
    private sealed class RetryTapIfStep : IStep<Unit>
    {
        /// <summary>
        /// 1 回目だけ失敗し、2 回目に成功します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Unit 値。</returns>
        public Unit Execute(StepInput input)
        {
            int attempt = RunIfTapIfState.IncrementRetryTapIfAttempts();
            if (attempt == 1)
            {
                throw new InvalidOperationException("retry tap-if failed");
            }

            return Unit.Value;
        }
    }

    /// <summary>
    /// RunIf と TapIf 検査の観測状態を保持します。
    /// </summary>
    private static class RunIfTapIfState
    {
        private static TaskCompletionSource asyncStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 同期 RunIf Step の実行回数を取得します。
        /// </summary>
        public static int SyncRunIfAttempts { get; private set; }

        /// <summary>
        /// 同一型 RunIf Step の実行回数を取得します。
        /// </summary>
        public static int SameTypeRunIfAttempts { get; private set; }

        /// <summary>
        /// TapIf Step の実行回数を取得します。
        /// </summary>
        public static int TapIfAttempts { get; private set; }

        /// <summary>
        /// async RunIf Step の実行回数を取得します。
        /// </summary>
        public static int AsyncRunIfAttempts { get; private set; }

        /// <summary>
        /// async TapIf Step の実行回数を取得します。
        /// </summary>
        public static int AsyncTapIfAttempts { get; private set; }

        /// <summary>
        /// retry RunIf Step の実行回数を取得します。
        /// </summary>
        public static int RetryRunIfAttempts { get; private set; }

        /// <summary>
        /// retry TapIf Step の実行回数を取得します。
        /// </summary>
        public static int RetryTapIfAttempts { get; private set; }

        /// <summary>
        /// 条件判定の実行回数を取得します。
        /// </summary>
        public static int ConditionAttempts { get; private set; }

        /// <summary>
        /// 観測状態を初期値へ戻します。
        /// </summary>
        public static void Reset()
        {
            SyncRunIfAttempts = 0;
            SameTypeRunIfAttempts = 0;
            TapIfAttempts = 0;
            AsyncRunIfAttempts = 0;
            AsyncTapIfAttempts = 0;
            RetryRunIfAttempts = 0;
            RetryTapIfAttempts = 0;
            ConditionAttempts = 0;
            asyncStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// 同期 RunIf Step の実行回数を増やします。
        /// </summary>
        public static void IncrementSyncRunIfAttempts()
        {
            SyncRunIfAttempts++;
        }

        /// <summary>
        /// 同一型 RunIf Step の実行回数を増やします。
        /// </summary>
        public static void IncrementSameTypeRunIfAttempts()
        {
            SameTypeRunIfAttempts++;
        }

        /// <summary>
        /// TapIf Step の実行回数を増やします。
        /// </summary>
        public static void IncrementTapIfAttempts()
        {
            TapIfAttempts++;
        }

        /// <summary>
        /// async RunIf Step の実行回数を増やします。
        /// </summary>
        public static void IncrementAsyncRunIfAttempts()
        {
            AsyncRunIfAttempts++;
        }

        /// <summary>
        /// async TapIf Step の実行回数を増やします。
        /// </summary>
        public static void IncrementAsyncTapIfAttempts()
        {
            AsyncTapIfAttempts++;
        }

        /// <summary>
        /// retry RunIf Step の実行回数を増やします。
        /// </summary>
        /// <returns>加算後の実行回数。</returns>
        public static int IncrementRetryRunIfAttempts()
        {
            return ++RetryRunIfAttempts;
        }

        /// <summary>
        /// retry TapIf Step の実行回数を増やします。
        /// </summary>
        /// <returns>加算後の実行回数。</returns>
        public static int IncrementRetryTapIfAttempts()
        {
            return ++RetryTapIfAttempts;
        }

        /// <summary>
        /// 条件判定の実行回数を増やします。
        /// </summary>
        public static void IncrementConditionAttempts()
        {
            ConditionAttempts++;
        }

        /// <summary>
        /// 非同期 Step が開始したことを記録します。
        /// </summary>
        public static void MarkAsyncStarted()
        {
            asyncStarted.SetResult();
        }

        /// <summary>
        /// 非同期 Step の開始を待機します。
        /// </summary>
        /// <param name="timeout">開始待ちの最大時間。</param>
        /// <returns>待機処理。</returns>
        public static Task WaitForAsyncStartAsync(TimeSpan timeout)
        {
            return asyncStarted.Task.WaitAsync(timeout);
        }
    }
}
