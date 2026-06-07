using System.Diagnostics;
using System.Reflection;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// timeout とキャンセルの公開契約を検査します。
/// </summary>
public sealed class TimeoutCancellationContractTests
{
    private static readonly TimeSpan SyncStepDelay = TimeSpan.FromMilliseconds(150);

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
    /// sync Step 実行中の外部 cancel が完了後に STEP_CANCELED になり、Produce と後続 Step を止めることを確認します。
    /// </summary>
    [Fact(DisplayName = "external cancellation during sync step returns STEP_CANCELED after completion")]
    public async Task ExternalCancellationDuringSyncStepReturnsStepCanceledAfterCompletion()
    {
        ContractTestState.Reset();
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

        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(30));
        var stopwatch = Stopwatch.StartNew();
        WorkflowResult result = await step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token);
        stopwatch.Stop();

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.True(stopwatch.Elapsed >= SyncStepDelay);
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
        ContractTestState.Reset();
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

        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(60));
        var stopwatch = Stopwatch.StartNew();
        WorkflowResult result = await step.ExecuteWorkflowAsync(options, cancellationTokenSource.Token);
        stopwatch.Stop();

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.True(stopwatch.Elapsed >= SyncStepDelay);
        Assert.Equal(["sync-timeout-external-cancel:start", "sync-timeout-external-cancel:end"], ContractTestState.Snapshot());
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(SlowTimeoutAndExternallyCanceledSynchronousStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, traceStep.ErrorCode);
    }

    /// <summary>
    /// sync Step は timeout で強制中断せず、完了後に後続 Step を開始しないことを確認します。
    /// </summary>
    [Fact(DisplayName = "sync step timeout waits for completion and stops before following steps")]
    public async Task SyncStepTimeoutWaitsForCompletionAndStopsBeforeFollowingSteps()
    {
        ContractTestState.Reset();
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

        var stopwatch = Stopwatch.StartNew();
        WorkflowResult result = await step.ExecuteWorkflowAsync(options);
        stopwatch.Stop();

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, result.ErrorCode);
        Assert.True(stopwatch.Elapsed >= SyncStepDelay);
        Assert.Equal(["sync-timeout:start", "sync-timeout:end"], ContractTestState.Snapshot());
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(SlowSynchronousStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, traceStep.ErrorCode);
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

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

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
            Thread.Sleep(SyncStepDelay);
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
            Thread.Sleep(SyncStepDelay);
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
            Thread.Sleep(SyncStepDelay);
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
    /// timeout/cancel 検査で共有する実行順序と token 観測結果を保持します。
    /// </summary>
    private static class ContractTestState
    {
        private static readonly object Gate = new();
        private static List<string> entries = new();
        private static TaskCompletionSource asyncStepStarted = NewAsyncStepStartedSource();
        private static bool? observedTokenCanBeCanceled;

        /// <summary>
        /// テスト間で共有する観測状態を初期化します。
        /// </summary>
        public static void Reset()
        {
            lock (Gate)
            {
                entries = new List<string>();
                asyncStepStarted = NewAsyncStepStartedSource();
                observedTokenCanBeCanceled = null;
            }
        }

        /// <summary>
        /// Step の実行順序を記録します。
        /// </summary>
        public static void Add(string value)
        {
            lock (Gate)
            {
                entries.Add(value);
            }
        }

        /// <summary>
        /// Step に渡された CancellationToken の状態を記録します。
        /// </summary>
        public static void ObserveCancellationToken(CancellationToken cancellationToken)
        {
            lock (Gate)
            {
                observedTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            }
        }

        /// <summary>
        /// async Step が待機状態へ入ったことを通知します。
        /// </summary>
        public static void MarkAsyncStepStarted()
        {
            TaskCompletionSource source;
            lock (Gate)
            {
                source = asyncStepStarted;
            }

            source.TrySetResult();
        }

        /// <summary>
        /// async Step が開始するまで待機します。
        /// </summary>
        public static async Task WaitForAsyncStepStartAsync(TimeSpan timeout)
        {
            TaskCompletionSource source;
            lock (Gate)
            {
                source = asyncStepStarted;
            }

            await source.Task.WaitAsync(timeout);
        }

        /// <summary>
        /// 記録済みの実行順序を取得します。
        /// </summary>
        public static IReadOnlyList<string> Snapshot()
        {
            lock (Gate)
            {
                return entries.ToArray();
            }
        }

        /// <summary>
        /// Step に渡された token が cancel 可能だったかを取得します。
        /// </summary>
        public static bool? ReadObservedTokenCanBeCanceled()
        {
            lock (Gate)
            {
                return observedTokenCanBeCanceled;
            }
        }

        /// <summary>
        /// async Step 開始通知用の TaskCompletionSource を作成します。
        /// </summary>
        private static TaskCompletionSource NewAsyncStepStartedSource()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
