using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// Lambda Step の公開 API と実行契約を検査します。
/// </summary>
public sealed class LambdaStepContractTests
{
    /// <summary>
    /// top-level lambda が最初の Step として実行され、trace に指定名を残すことを確認します。
    /// </summary>
    [Fact(DisplayName = "top level lambda step runs first and records configured trace name")]
    public async Task LambdaStepTopLevelRunsFirstAndRecordsConfiguredTraceName()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("load-lambda", input =>
            {
                input.Context.Set("prefix", "top");

                return "top-value";
            });

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("load-lambda", traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, traceStep.Status);
    }

    /// <summary>
    /// チェーン途中の同期 lambda が現在値を受け取り、後続 Step へ値を渡せることを確認します。
    /// </summary>
    [Fact(DisplayName = "sync lambda step receives current value and passes next value")]
    public async Task LambdaStepSyncChainReceivesCurrentValueAndPassesNextValue()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("source-lambda", input => new LambdaSource("alpha"))
            .Run("convert-lambda", source => new LambdaNext(source.Value + "-next"))
                .Produce<LambdaNext>(output => output)
            .Run<ReadLambdaNextStep, string>();

        WorkflowResult result = await step.ExecuteWorkflowAsync();
        string output = step.Execute(new StepInput());

        Assert.True(result.Succeeded);
        Assert.Equal("alpha-next", output);
        Assert.Equal(["source-lambda", "convert-lambda", nameof(ReadLambdaNextStep)], result.Trace!.Steps.Select(traceStep => traceStep.StepName));
    }

    /// <summary>
    /// StepInput 付き lambda が StoreAs、Produce、StepContext の値を読めることを確認します。
    /// </summary>
    [Fact(DisplayName = "lambda step with StepInput reads stored produced and context values")]
    public async Task LambdaStepWithStepInputReadsStoredProducedAndContextValues()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("source-lambda", input =>
            {
                input.Context.Set("context-suffix", "ctx");

                return new LambdaSource("seed");
            })
                .StoreAs()
                .Produce<string>("named-source", output => output.Value)
            .Run("combine-lambda", (source, input) =>
            {
                LambdaSource stored = input.Get<LambdaSource>();
                string named = input.Get<string>("named-source");
                string suffix = input.Context.Get<string>("context-suffix");

                return $"{source.Value}:{stored.Value}:{named}:{suffix}";
            });

        WorkflowResult result = await step.ExecuteWorkflowAsync();
        string output = step.Execute(new StepInput());

        Assert.True(result.Succeeded);
        Assert.Equal("seed:seed:seed:ctx", output);
    }

    /// <summary>
    /// async lambda が合成済み cancellation token を受け取り、timeout で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "async lambda step receives cancellation token and returns timeout")]
    public async Task LambdaStepAsyncReceivesCancellationTokenAndReturnsTimeout()
    {
        LambdaStepState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .RunAsync("wait-lambda", async (input, cancellationToken) =>
            {
                LambdaStepState.RecordToken(cancellationToken.CanBeCanceled);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

                return "unreachable";
            })
            .Run("after-timeout-lambda", value =>
            {
                LambdaStepState.MarkFollowingStep();

                return value;
            });
        var options = new WorkflowExecutionOptions
        {
            StepTimeout = TimeSpan.FromMilliseconds(30),
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, result.ErrorCode);
        Assert.True(LambdaStepState.TokenCanBeCanceled);
        Assert.False(LambdaStepState.FollowingStepRan);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("wait-lambda", traceStep.StepName);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, traceStep.ErrorCode);
    }

    /// <summary>
    /// async lambda の開始後に外部 token を cancel すると STEP_CANCELED になり、後続 Step を実行しないことを確認します。
    /// </summary>
    [Fact(DisplayName = "async lambda step external cancellation returns step canceled")]
    public async Task LambdaStepAsyncExternalCancellationReturnsStepCanceled()
    {
        LambdaStepState.Reset();
        using var cancellationTokenSource = new CancellationTokenSource();
        var lambdaStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .RunAsync("external-cancel-lambda", async (input, cancellationToken) =>
            {
                LambdaStepState.RecordToken(cancellationToken.CanBeCanceled);
                lambdaStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

                return "unreachable";
            })
            .Run("after-cancel-lambda", value =>
            {
                LambdaStepState.MarkFollowingStep();

                return value;
            });

        Task<WorkflowResult> workflowTask = step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token);
        await lambdaStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await cancellationTokenSource.CancelAsync();
        WorkflowResult result = await workflowTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.True(LambdaStepState.TokenCanBeCanceled);
        Assert.False(LambdaStepState.FollowingStepRan);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("external-cancel-lambda", traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, traceStep.ErrorCode);
    }

    /// <summary>
    /// lambda body の通常例外が STEP_EXECUTION_FAILED になり、後続 Step を実行しないことを確認します。
    /// </summary>
    [Fact(DisplayName = "lambda body exception returns step execution failed and stops following steps")]
    public async Task LambdaStepBodyExceptionReturnsStepExecutionFailedAndStopsFollowingSteps()
    {
        LambdaStepState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<string>("failing-lambda", input => throw new InvalidOperationException("lambda failed"))
            .Run("after-failure-lambda", value =>
            {
                LambdaStepState.MarkFollowingStep();

                return value;
            });

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        Assert.False(LambdaStepState.FollowingStepRan);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("failing-lambda", traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
    }

    /// <summary>
    /// retry 設定が lambda body の通常例外に適用されることを確認します。
    /// </summary>
    [Fact(DisplayName = "lambda body exception is retried until success")]
    public async Task LambdaStepBodyExceptionIsRetriedUntilSuccess()
    {
        LambdaStepState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("retry-lambda", input =>
            {
                int attempt = LambdaStepState.IncrementAttempts();
                if (attempt == 1)
                {
                    throw new InvalidOperationException("retry lambda failed");
                }

                return $"ok-{attempt}";
            });
        var options = new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 2 },
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.True(result.Succeeded);
        Assert.Equal(2, LambdaStepState.Attempts);
        Assert.Equal(
            [
                (ExecutionTraceStepStatus.Failed, WorkflowErrorCodes.StepExecutionFailed, 1),
                (ExecutionTraceStepStatus.Succeeded, null, 2),
            ],
            result.Trace!.Steps.Select(traceStep => (traceStep.Status, traceStep.ErrorCode, traceStep.Attempt)).ToArray());
    }

    /// <summary>
    /// Lambda Step でも Produce、StoreAs、trace value capture が通常 Step と同様に動くことを確認します。
    /// </summary>
    [Fact(DisplayName = "lambda step supports produce store as and trace value capture")]
    public async Task LambdaStepSupportsProduceStoreAsAndTraceValueCapture()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("source-lambda", input => new LambdaSource("trace"))
                .StoreAs(TraceValueCapture.Serialized)
                .Produce<string>("trace-name", output => output.Value, TraceValueCapture.Serialized)
            .Run("read-lambda", (source, input) => input.Get<LambdaSource>().Value + ":" + input.Get<string>("trace-name"));

        WorkflowResult result = await step.ExecuteWorkflowAsync();
        string output = step.Execute(new StepInput());

        Assert.True(result.Succeeded);
        Assert.Equal("trace:trace", output);
        ExecutionTraceStep traceStep = result.Trace!.Steps[0];
        Assert.Equal("source-lambda", traceStep.StepName);
        Assert.Equal(2, traceStep.ProducedValues.Count);
        Assert.Contains(traceStep.ProducedValues, value => value.Source == ExecutionTraceValueSource.StoreAs
            && value.CaptureStatus == ExecutionTraceValueCaptureStatus.Serialized);
        Assert.Contains(traceStep.ProducedValues, value => value.Source == ExecutionTraceValueSource.Produce
            && value.Name == "trace-name"
            && value.SerializedValue == "\"trace\"");
    }

    /// <summary>
    /// Lambda Step API の null body と空 name が既存方針どおり失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "lambda step rejects null body and empty name")]
    public void LambdaStepRejectsNullBodyAndEmptyName()
    {
        Func<StepInput, string>? topLevelBody = null;
        Func<StepInput, CancellationToken, Task<string>>? topLevelAsyncBody = null;
        Func<string, string>? currentBody = null;
        Func<string, StepInput, string>? inputBody = null;
        Func<string, StepInput, CancellationToken, Task<string>>? asyncBody = null;

        Assert.Throws<ArgumentException>(() => CompositeStep.Define("Main").Run<string>("", input => "value"));
        Assert.Throws<ArgumentNullException>(() => CompositeStep.Define("Main").Run("null-body", topLevelBody!));
        Assert.Throws<ArgumentException>(() => CompositeStep.Define("Main").RunAsync<string>("", (input, cancellationToken) => Task.FromResult("value")));
        Assert.Throws<ArgumentNullException>(() => CompositeStep.Define("Main").RunAsync("null-body", topLevelAsyncBody!));
        Assert.Throws<ArgumentException>(() => CompositeStep.Define("Main").Run("source", input => "value").Run<string>(" ", value => value));
        Assert.Throws<ArgumentNullException>(() => CompositeStep.Define("Main").Run("source", input => "value").Run("null-body", currentBody!));
        Assert.Throws<ArgumentNullException>(() => CompositeStep.Define("Main").Run("source", input => "value").Run("null-body", inputBody!));
        Assert.Throws<ArgumentNullException>(() => CompositeStep.Define("Main").Run("source", input => "value").RunAsync("null-body", asyncBody!));
    }

    /// <summary>
    /// Lambda Step の source 値を保持します。
    /// </summary>
    private sealed class LambdaSource
    {
        /// <summary>
        /// source 値を初期化します。
        /// </summary>
        /// <param name="value">検査で使う値。</param>
        public LambdaSource(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 検査で使う文字列値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// Lambda Step の後続値を保持します。
    /// </summary>
    private sealed class LambdaNext
    {
        /// <summary>
        /// 後続値を初期化します。
        /// </summary>
        /// <param name="value">検査で使う値。</param>
        public LambdaNext(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 後続 Step へ渡す文字列値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// Produce された LambdaNext を読み取る検査用 Step です。
    /// </summary>
    private sealed class ReadLambdaNextStep : IStep<string>
    {
        /// <summary>
        /// StepInput から LambdaNext を読み取って値を返します。
        /// </summary>
        /// <param name="input">読み取り対象の StepInput。</param>
        /// <returns>読み取った文字列値。</returns>
        public string Execute(StepInput input)
        {
            return input.Get<LambdaNext>().Value;
        }
    }

    /// <summary>
    /// Lambda Step 検査の観測状態を保持します。
    /// </summary>
    private static class LambdaStepState
    {
        private static int attempts;

        /// <summary>
        /// 後続 Step が実行されたかどうかを取得します。
        /// </summary>
        public static bool FollowingStepRan { get; private set; }

        /// <summary>
        /// async lambda に渡された token がキャンセル可能だったかどうかを取得します。
        /// </summary>
        public static bool TokenCanBeCanceled { get; private set; }

        /// <summary>
        /// retry 対象 lambda の実行回数を取得します。
        /// </summary>
        public static int Attempts => attempts;

        /// <summary>
        /// 観測状態を初期値へ戻します。
        /// </summary>
        public static void Reset()
        {
            attempts = 0;
            FollowingStepRan = false;
            TokenCanBeCanceled = false;
        }

        /// <summary>
        /// retry 対象 lambda の実行回数を増やします。
        /// </summary>
        /// <returns>加算後の実行回数。</returns>
        public static int IncrementAttempts()
        {
            return Interlocked.Increment(ref attempts);
        }

        /// <summary>
        /// async lambda に渡された token の状態を記録します。
        /// </summary>
        /// <param name="tokenCanBeCanceled">token がキャンセル可能かどうか。</param>
        public static void RecordToken(bool tokenCanBeCanceled)
        {
            TokenCanBeCanceled = tokenCanBeCanceled;
        }

        /// <summary>
        /// 後続 Step が実行されたことを記録します。
        /// </summary>
        public static void MarkFollowingStep()
        {
            FollowingStepRan = true;
        }
    }
}
