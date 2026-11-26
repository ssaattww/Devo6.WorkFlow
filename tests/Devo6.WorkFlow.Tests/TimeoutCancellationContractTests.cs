using System.Reflection;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// timeout とキャンセルの公開契約を検査します。
/// </summary>
public sealed class TimeoutCancellationContractTests
{
    /// <summary>
    /// WorkflowExecutionOptions が Step 単位の nullable timeout を公開することを確認します。
    /// </summary>
    [Fact(DisplayName = "WorkflowExecutionOptions exposes nullable per-step timeout")]
    public void WorkflowExecutionOptionsExposesNullablePerStepTimeout()
    {
        PropertyInfo stepTimeout = RequireStepTimeoutProperty();

        Assert.Equal(typeof(TimeSpan?), stepTimeout.PropertyType);
        Assert.True(stepTimeout.CanRead);
        Assert.NotNull(stepTimeout.SetMethod);
        Assert.True(stepTimeout.SetMethod!.IsPublic);
    }

    /// <summary>
    /// async Step の timeout が STEP_TIMEOUT になり、Produce と後続 Step を止めることを確認します。
    /// </summary>
    [Fact(DisplayName = "async step timeout returns STEP_TIMEOUT and stops produce and following steps")]
    public async Task AsyncStepTimeoutReturnsStepTimeoutAndStopsProduceAndFollowingSteps()
    {
        ContractTestState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .RunAsync<TimeoutObservingAsyncStep, TimedOutput>()
                .Produce<NextInput>(output =>
                {
                    ContractTestState.Add("timeout-produce");

                    return new NextInput(output.Value);
                })
            .Run<ShouldNotRunStep, string>()
                .StoreAs();
        WorkflowExecutionOptions options = CreateOptionsWithStepTimeout(TimeSpan.FromMilliseconds(50));

        WorkflowResult result = await step.ExecuteWorkflowAsync(options).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, result.ErrorCode);
        Assert.True(ContractTestState.ReadObservedTokenCanBeCanceled());
        Assert.Equal(["async-timeout:start"], ContractTestState.Snapshot());
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(TimeoutObservingAsyncStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, traceStep.ErrorCode);
    }

    /// <summary>
    /// 外部 CancellationToken による cancel が STEP_CANCELED になり、後続 Step を止めることを確認します。
    /// </summary>
    [Fact(DisplayName = "external cancellation returns STEP_CANCELED and stops following steps")]
    public async Task ExternalCancellationReturnsStepCanceledAndStopsFollowingSteps()
    {
        ContractTestState.Reset();
        using var cancellationTokenSource = new CancellationTokenSource();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .RunAsync<ExternallyCanceledAsyncStep, TimedOutput>()
                .Produce<NextInput>(output =>
                {
                    ContractTestState.Add("external-cancel-produce");

                    return new NextInput(output.Value);
                })
            .Run<ShouldNotRunStep, string>()
                .StoreAs();

        Task<WorkflowResult> workflowTask = step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token);
        await ContractTestState.WaitForAsyncStepStartAsync(TimeSpan.FromSeconds(1));
        await cancellationTokenSource.CancelAsync();
        WorkflowResult result = await workflowTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.True(ContractTestState.ReadObservedTokenCanBeCanceled());
        Assert.Equal(["async-external-cancel:start"], ContractTestState.Snapshot());
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(ExternallyCanceledAsyncStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, traceStep.ErrorCode);
    }

    /// <summary>
    /// Step 初期化後、Step 本体の呼び出し直前に外部取消された場合は本体を開始しません。
    /// </summary>
    [Fact(DisplayName = "cancellation during step initialization stops before synchronous step body")]
    public async Task CancellationDuringStepInitializationStopsBeforeSynchronousStepBody()
    {
        ContractTestState.Reset();
        using var cancellationTokenSource = new CancellationTokenSource();
        CompositeStep<int> step = CompositeStep
            .Define("Main")
            .Run<int>("sync", _ =>
            {
                ContractTestState.Add("sync-body");

                return 1;
            });
        var options = new WorkflowExecutionOptions
        {
            StepTimeout = TimeSpan.FromMinutes(1),
            StepTimeoutTokenObserver = _ => cancellationTokenSource.Cancel(),
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options, cancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.Empty(ContractTestState.Snapshot());
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, traceStep.ErrorCode);
    }

    /// <summary>
    /// RunIf と TapIf の条件評価中に外部取消された場合は Step を生成しません。
    /// </summary>
    [Theory]
    [InlineData("RunIf")]
    [InlineData("TapIf")]
    public async Task ConditionalPredicateCancellationStopsBeforeStepConstruction(string registration)
    {
        ContractTestState.Reset();
        using var cancellationTokenSource = new CancellationTokenSource();
        CompositeStep<int> step = CompositeStep.Define("Main").Run<int>("seed", _ => 0);
        if (registration == "RunIf")
        {
            step = step.RunIf<PredicateCancellationStep, int>(
                (_, _) => CancelDuringCondition(cancellationTokenSource),
                (current, _) => current);
        }
        else
        {
            step = step.TapIf<PredicateCancellationTapStep>(
                (_, _) => CancelDuringCondition(cancellationTokenSource));
        }

        WorkflowResult result = await step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.Equal(["condition"], ContractTestState.Snapshot());
    }

    /// <summary>
    /// 型付き Step のコンストラクター中に外部取消された場合は Execute を呼びません。
    /// </summary>
    [Fact(DisplayName = "external cancellation during typed step construction stops before execution")]
    public async Task TypedStepConstructionCancellationStopsBeforeExecution()
    {
        ContractTestState.Reset();
        using var cancellationTokenSource = new CancellationTokenSource();
        ContractTestState.CancelWhenStepIsConstructed(cancellationTokenSource);
        CompositeStep<int> step = CompositeStep.Define("Main").Run<CancelOnConstructionStep, int>();

        WorkflowResult result = await step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.Equal(["constructor"], ContractTestState.Snapshot());
    }

    /// <summary>
    /// RunIf 条件の評価中に外部取消を通知し、その評価結果を返します。
    /// </summary>
    /// <param name="cancellationTokenSource">取り消す workflow の token source。</param>
    /// <returns>条件評価を成功扱いにしないための true。</returns>
    private static bool CancelDuringCondition(CancellationTokenSource cancellationTokenSource)
    {
        ContractTestState.Add("condition");
        cancellationTokenSource.Cancel();

        return true;
    }

    /// <summary>
    /// sync Step 実行中の外部 cancel が完了後に STEP_CANCELED になり、Produce と後続 Step を止めることを確認します。
    /// </summary>
    [Fact(DisplayName = "external cancellation during sync step returns STEP_CANCELED after completion")]
    public async Task ExternalCancellationDuringSyncStepReturnsStepCanceledAfterCompletion()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ContractTestState.Reset(testTimeout.Token);
        using var cancellationTokenSource = new CancellationTokenSource();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<SlowExternallyCanceledSynchronousStep, TimedOutput>()
                .Produce<NextInput>(output =>
                {
                    ContractTestState.Add("sync-external-cancel-produce");

                    return new NextInput(output.Value);
                })
            .Run<ShouldNotRunStep, string>()
                .StoreAs();

        Task<WorkflowResult> workflowTask = Task.Run(
            () => step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token));
        try
        {
            await ContractTestState.WaitForSyncStepStartAsync();
            await cancellationTokenSource.CancelAsync().WaitAsync(testTimeout.Token);
        }
        finally
        {
            ContractTestState.ReleaseSyncStep();
            await workflowTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        WorkflowResult result = await workflowTask;

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.Equal(["sync-external-cancel:start", "sync-external-cancel:end"], ContractTestState.Snapshot());
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(SlowExternallyCanceledSynchronousStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, traceStep.ErrorCode);
    }

    /// <summary>
    /// sync Step 完了後に timeout と外部 cancel を両方観測した場合、外部 cancel を優先することを確認します。
    /// </summary>
    [Fact(DisplayName = "external cancellation wins when sync step observes timeout and cancellation")]
    public async Task ExternalCancellationWinsWhenSyncStepObservesTimeoutAndCancellation()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ContractTestState.Reset(testTimeout.Token);
        using var cancellationTokenSource = new CancellationTokenSource();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<SlowTimeoutAndExternallyCanceledSynchronousStep, TimedOutput>()
                .Produce<NextInput>(output =>
                {
                    ContractTestState.Add("sync-timeout-external-cancel-produce");

                    return new NextInput(output.Value);
                })
            .Run<ShouldNotRunStep, string>()
                .StoreAs();
        WorkflowExecutionOptions options = CreateOptionsWithStepTimeout(TimeSpan.FromMilliseconds(30));
        var timeoutRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration timeoutRegistration = default;
        options.StepTimeoutTokenObserver = token =>
            timeoutRegistration = token.Register(() => timeoutRequested.TrySetResult());
        Task<WorkflowResult> workflowTask = Task.Run(
            () => step.ExecuteWorkflowAsync(options, cancellationTokenSource.Token));
        try
        {
            try
            {
                await ContractTestState.WaitForSyncStepStartAsync();
                await timeoutRequested.Task.WaitAsync(testTimeout.Token);
                await cancellationTokenSource.CancelAsync().WaitAsync(testTimeout.Token);
            }
            finally
            {
                ContractTestState.ReleaseSyncStep();
                await workflowTask.WaitAsync(TimeSpan.FromSeconds(2));
            }

            WorkflowResult result = await workflowTask;

            Assert.False(result.Succeeded);
            Assert.Equal("Main", result.EntryName);
            Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
            Assert.Equal(["sync-timeout-external-cancel:start", "sync-timeout-external-cancel:end"], ContractTestState.Snapshot());
            ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
            Assert.Equal(nameof(SlowTimeoutAndExternallyCanceledSynchronousStep), traceStep.StepName);
            Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
            Assert.Equal(WorkflowErrorCodes.StepCanceled, traceStep.ErrorCode);
        }
        finally
        {
            timeoutRegistration.Dispose();
        }
    }

    /// <summary>
    /// sync Step は timeout で強制中断せず、完了後に後続 Step を開始しないことを確認します。
    /// </summary>
    [Fact(DisplayName = "sync step timeout waits for completion and stops before following steps")]
    public async Task SyncStepTimeoutWaitsForCompletionAndStopsBeforeFollowingSteps()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ContractTestState.Reset(testTimeout.Token);
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<SlowSynchronousStep, TimedOutput>()
                .Produce<NextInput>(output =>
                {
                    ContractTestState.Add("sync-timeout-produce");

                    return new NextInput(output.Value);
                })
            .Run<ShouldNotRunStep, string>()
                .StoreAs();
        WorkflowExecutionOptions options = CreateOptionsWithStepTimeout(TimeSpan.FromMilliseconds(30));
        var timeoutRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration timeoutRegistration = default;
        options.StepTimeoutTokenObserver = token =>
            timeoutRegistration = token.Register(() => timeoutRequested.TrySetResult());
        Task<WorkflowResult> workflowTask = Task.Run(() => step.ExecuteWorkflowAsync(options));
        try
        {
            try
            {
                await ContractTestState.WaitForSyncStepStartAsync();
                await timeoutRequested.Task.WaitAsync(testTimeout.Token);
            }
            finally
            {
                ContractTestState.ReleaseSyncStep();
                await workflowTask.WaitAsync(TimeSpan.FromSeconds(2));
            }

            WorkflowResult result = await workflowTask;

            Assert.False(result.Succeeded);
            Assert.Equal("Main", result.EntryName);
            Assert.Equal(WorkflowErrorCodes.StepTimeout, result.ErrorCode);
            Assert.Equal(["sync-timeout:start", "sync-timeout:end"], ContractTestState.Snapshot());
            ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
            Assert.Equal(nameof(SlowSynchronousStep), traceStep.StepName);
            Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
            Assert.Equal(WorkflowErrorCodes.StepTimeout, traceStep.ErrorCode);
        }
        finally
        {
            timeoutRegistration.Dispose();
        }
    }

    /// <summary>
    /// 前の実行から遅れて戻った worker が次の実行状態を変更しないことを確認します。
    /// </summary>
    [Fact(DisplayName = "late worker keeps its own timeout test state after reset")]
    public async Task LateWorkerKeepsItsOwnTestStateAfterReset()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ContractTestState.Reset(testTimeout.Token);
        var workerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWorker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            workerStarted.TrySetResult();
            await releaseWorker.Task.WaitAsync(testTimeout.Token);
            ContractTestState.Add("late-worker");
        });

        try
        {
            await workerStarted.Task.WaitAsync(testTimeout.Token);
            ContractTestState.Reset(testTimeout.Token);
            releaseWorker.TrySetResult();
            await worker.WaitAsync(testTimeout.Token);

            Assert.Empty(ContractTestState.Snapshot());
        }
        finally
        {
            releaseWorker.TrySetResult();
            await worker.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// WorkflowExecutionOptions.StepTimeout の PropertyInfo を取得します。
    /// </summary>
    private static PropertyInfo RequireStepTimeoutProperty()
    {
        PropertyInfo? stepTimeout = typeof(WorkflowExecutionOptions).GetProperty(
            "StepTimeout",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(stepTimeout);

        return stepTimeout!;
    }

    /// <summary>
    /// StepTimeout を設定した WorkflowExecutionOptions を作成します。
    /// </summary>
    private static WorkflowExecutionOptions CreateOptionsWithStepTimeout(TimeSpan timeout)
    {
        PropertyInfo stepTimeout = RequireStepTimeoutProperty();
        var options = new WorkflowExecutionOptions();

        stepTimeout.SetValue(options, timeout);

        Assert.Equal(timeout, stepTimeout.GetValue(options));

        return options;
    }

    /// <summary>
    /// timeout/cancel 検査用 Step の出力値を保持します。
    /// </summary>
    private sealed class TimedOutput
    {
        /// <summary>
        /// テスト用 Step の出力値を初期化します。
        /// </summary>
        public TimedOutput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 後続 Step へ渡す観測用の値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// Produce から後続 Step へ渡す入力値を保持します。
    /// </summary>
    private sealed class NextInput
    {
        /// <summary>
        /// 後続 Step の入力値を初期化します。
        /// </summary>
        public NextInput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 後続 Step が受け取る観測用の値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// timeout 用 token の受け渡しと協調キャンセルを観測する async Step です。
    /// </summary>
    private sealed class TimeoutObservingAsyncStep : IAsyncStep<TimedOutput>
    {
        /// <summary>
        /// timeout 用 token が渡され、timeout 時に協調キャンセルされるまで待機します。
        /// </summary>
        public async Task<TimedOutput> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            ContractTestState.Add("async-timeout:start");
            ContractTestState.ObserveCancellationToken(cancellationToken);

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

            ContractTestState.Add("async-timeout:end");

            return new TimedOutput("timeout");
        }
    }

    /// <summary>
    /// 外部 CancellationToken による async Step のキャンセルを観測します。
    /// </summary>
    private sealed class ExternallyCanceledAsyncStep : IAsyncStep<TimedOutput>
    {
        /// <summary>
        /// 外部 CancellationToken が cancel されるまで待機します。
        /// </summary>
        public async Task<TimedOutput> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            ContractTestState.Add("async-external-cancel:start");
            ContractTestState.ObserveCancellationToken(cancellationToken);
            ContractTestState.MarkAsyncStepStarted();

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            ContractTestState.Add("async-external-cancel:end");

            return new TimedOutput("external-cancel");
        }
    }

    /// <summary>
    /// timeout より長く実行される同期 Step です。
    /// </summary>
    private sealed class SlowSynchronousStep : IStep<TimedOutput>
    {
        /// <summary>
        /// timeout より長く同期的に実行され、強制中断されないことを観測します。
        /// </summary>
        public TimedOutput Execute(StepInput input)
        {
            ContractTestState.Add("sync-timeout:start");
            ContractTestState.WaitAtSyncStepGate();
            ContractTestState.Add("sync-timeout:end");

            return new TimedOutput("sync-timeout");
        }
    }

    /// <summary>
    /// 外部 cancel より長く実行される同期 Step です。
    /// </summary>
    private sealed class SlowExternallyCanceledSynchronousStep : IStep<TimedOutput>
    {
        /// <summary>
        /// 外部 cancel より長く同期的に実行され、完了後に cancel として扱われることを観測します。
        /// </summary>
        public TimedOutput Execute(StepInput input)
        {
            ContractTestState.Add("sync-external-cancel:start");
            ContractTestState.WaitAtSyncStepGate();
            ContractTestState.Add("sync-external-cancel:end");

            return new TimedOutput("sync-external-cancel");
        }
    }

    /// <summary>
    /// timeout と外部 cancel の優先順位を観測する同期 Step です。
    /// </summary>
    private sealed class SlowTimeoutAndExternallyCanceledSynchronousStep : IStep<TimedOutput>
    {
        /// <summary>
        /// timeout と外部 cancel の両方より長く同期的に実行され、完了後の優先順位を観測します。
        /// </summary>
        public TimedOutput Execute(StepInput input)
        {
            ContractTestState.Add("sync-timeout-external-cancel:start");
            ContractTestState.WaitAtSyncStepGate();
            ContractTestState.Add("sync-timeout-external-cancel:end");

            return new TimedOutput("sync-timeout-external-cancel");
        }
    }

    /// <summary>
    /// timeout または cancel 後に後続 Step が走らないことを検出する Step です。
    /// </summary>
    private sealed class ShouldNotRunStep : IStep<string>
    {
        /// <summary>
        /// timeout または cancel 後に実行されないことを確認するための Step です。
        /// </summary>
        public string Execute(StepInput input)
        {
            ContractTestState.Add($"should-not-run:{input.Get<NextInput>().Value}");

            return "unexpected";
        }
    }

    /// <summary>
    /// RunIf の条件取消後に生成・実行されてはならない同期 Step です。
    /// </summary>
    private sealed class PredicateCancellationStep : IStep<int>
    {
        /// <summary>Step 型の生成を実行順へ記録します。</summary>
        public PredicateCancellationStep() => ContractTestState.Add("constructor");

        /// <summary>条件取消後に呼ばれてはならない本体です。</summary>
        public int Execute(StepInput input)
        {
            ContractTestState.Add("execute");

            return 1;
        }
    }

    /// <summary>
    /// TapIf の条件取消後に生成・実行されてはならない同期 Step です。
    /// </summary>
    private sealed class PredicateCancellationTapStep : IStep<Unit>
    {
        /// <summary>Step 型の生成を実行順へ記録します。</summary>
        public PredicateCancellationTapStep() => ContractTestState.Add("constructor");

        /// <summary>条件取消後に呼ばれてはならない本体です。</summary>
        public Unit Execute(StepInput input)
        {
            ContractTestState.Add("execute");

            return Unit.Value;
        }
    }

    /// <summary>
    /// 生成時の取消を観測し、本体呼出しを抑止する同期 Step です。
    /// </summary>
    private sealed class CancelOnConstructionStep : IStep<int>
    {
        /// <summary>生成時に外部 token を取り消します。</summary>
        public CancelOnConstructionStep()
        {
            ContractTestState.Add("constructor");
            ContractTestState.CancelDuringStepConstruction();
        }

        /// <summary>コンストラクターでの取消後は呼ばれてはならない本体です。</summary>
        public int Execute(StepInput input)
        {
            ContractTestState.Add("execute");

            return 1;
        }
    }

    /// <summary>
    /// timeout/cancel 検査で共有する実行順序と token 観測結果を保持します。
    /// </summary>
    private static class ContractTestState
    {
        private static readonly AsyncLocal<ExecutionState?> Current = new();

        /// <summary>
        /// 現在のテスト実行専用の観測状態を作成します。
        /// </summary>
        public static void Reset(CancellationToken timeoutToken = default)
        {
            Current.Value = new ExecutionState(timeoutToken);
        }

        /// <summary>
        /// Step コンストラクター中に外部取消する通知を現在の実行状態へ登録します。
        /// </summary>
        /// <param name="cancellationTokenSource">コンストラクターから取り消す token source。</param>
        public static void CancelWhenStepIsConstructed(CancellationTokenSource cancellationTokenSource)
        {
            State.StepConstructionCancellation = cancellationTokenSource.Cancel;
        }

        /// <summary>
        /// 現在の実行用に登録された Step 生成時の取消を通知します。
        /// </summary>
        public static void CancelDuringStepConstruction()
        {
            State.StepConstructionCancellation?.Invoke();
        }

        /// <summary>
        /// Step の実行順序を記録します。
        /// </summary>
        public static void Add(string value)
        {
            State.Add(value);
        }

        /// <summary>
        /// Step に渡された CancellationToken の状態を記録します。
        /// </summary>
        public static void ObserveCancellationToken(CancellationToken cancellationToken)
        {
            State.ObserveCancellationToken(cancellationToken);
        }

        /// <summary>
        /// async Step が待機状態へ入ったことを通知します。
        /// </summary>
        public static void MarkAsyncStepStarted()
        {
            State.AsyncStepStarted.TrySetResult();
        }

        /// <summary>
        /// async Step が開始するまで待機します。
        /// </summary>
        public static async Task WaitForAsyncStepStartAsync(TimeSpan timeout)
        {
            await State.AsyncStepStarted.Task.WaitAsync(timeout);
        }

        /// <summary>
        /// sync Step が進入するまで待機します。
        /// </summary>
        public static async Task WaitForSyncStepStartAsync()
        {
            ExecutionState state = State;
            await state.SyncStepStarted.Task.WaitAsync(state.TimeoutToken);
        }

        /// <summary>
        /// sync Step が返却できるように gate を解除します。
        /// </summary>
        public static void ReleaseSyncStep() => State.SyncStepRelease.TrySetResult();

        /// <summary>
        /// sync Step の進入を通知し、テストが解除するまで実行中に留まります。
        /// </summary>
        public static void WaitAtSyncStepGate()
        {
            ExecutionState state = State;
            state.SyncStepStarted.TrySetResult();
            state.SyncStepRelease.Task.WaitAsync(state.TimeoutToken).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 記録済みの実行順序を取得します。
        /// </summary>
        public static IReadOnlyList<string> Snapshot()
        {
            return State.Snapshot();
        }

        /// <summary>
        /// Step に渡された token が cancel 可能だったかを取得します。
        /// </summary>
        public static bool? ReadObservedTokenCanBeCanceled()
        {
            return State.ObservedTokenCanBeCanceled;
        }

        /// <summary>
        /// 現在のテスト実行に対応する状態を取得します。
        /// </summary>
        private static ExecutionState State => Current.Value
            ?? throw new InvalidOperationException("Test execution state was not initialized.");

        /// <summary>一回のテスト実行に固有の観測状態を作成します。</summary>
        /// <param name="timeoutToken">テスト全体の期限を制御する token。</param>
        private sealed class ExecutionState(CancellationToken timeoutToken)
        {
            private readonly object gate = new();
            private readonly List<string> entries = new();
            private bool? observedTokenCanBeCanceled;

            /// <summary>Step 生成時に実行する外部取消 callback を取得または設定します。</summary>
            public Action? StepConstructionCancellation { get; set; }

            /// <summary>このテスト実行の全体期限を取得します。</summary>
            public CancellationToken TimeoutToken { get; } = timeoutToken;

            /// <summary>async Step の開始通知を取得します。</summary>
            public TaskCompletionSource AsyncStepStarted { get; } = NewSource();

            /// <summary>sync Step の開始通知を取得します。</summary>
            public TaskCompletionSource SyncStepStarted { get; } = NewSource();

            /// <summary>sync Step を解放する通知を取得します。</summary>
            public TaskCompletionSource SyncStepRelease { get; } = NewSource();

            /// <summary>Step に渡した token の取消可能状態を取得します。</summary>
            public bool? ObservedTokenCanBeCanceled
            {
                get
                {
                    lock (gate)
                    {
                        return observedTokenCanBeCanceled;
                    }
                }
            }

            /// <summary>Step の実行順序を記録します。</summary>
            /// <param name="value">記録する実行イベント。</param>
            public void Add(string value)
            {
                lock (gate)
                {
                    entries.Add(value);
                }
            }

            /// <summary>Step に渡された token の取消可能状態を記録します。</summary>
            /// <param name="cancellationToken">観測する Step token。</param>
            public void ObserveCancellationToken(CancellationToken cancellationToken)
            {
                lock (gate)
                {
                    observedTokenCanBeCanceled = cancellationToken.CanBeCanceled;
                }
            }

            /// <summary>記録済みの実行順序を複製して返します。</summary>
            /// <returns>このテスト実行で記録したイベント。</returns>
            public IReadOnlyList<string> Snapshot()
            {
                lock (gate)
                {
                    return entries.ToArray();
                }
            }

            /// <summary>非同期通知用の継続非同期な TCS を作成します。</summary>
            /// <returns>新しい通知用 TCS。</returns>
            private static TaskCompletionSource NewSource() =>
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
