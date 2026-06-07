using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// T26 の trace value capture 契約を利用者目線で固定します。
/// </summary>
public sealed class TraceValueContractTests
{
    /// <summary>
    /// 既存 Produce と StoreAs が既定では trace value を残さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "default trace does not capture produced values")]
    public async Task DefaultTraceDoesNotCaptureProducedValues()
    {
        CompositeStep<StoredTraceOutput> step = CompositeStep
            .Define("Main")
            .Run<TypedTracePayloadStep, TypedTracePayload>()
                .Produce<TypedTracePayload>(output => output)
            .Run<StoredTraceOutputStep, StoredTraceOutput>()
                .StoreAs();

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        Assert.All(result.Trace!.Steps, traceStep => Assert.Empty(traceStep.ProducedValues));
    }

    /// <summary>
    /// 明示 serialized capture の型付き Produce が JSON 値と型名を trace に残すことを確認します。
    /// </summary>
    [Fact(DisplayName = "explicit trace capture records typed produced value")]
    public async Task ExplicitTraceCaptureRecordsTypedProducedValue()
    {
        CompositeStep<TypedTracePayload> step = CompositeStep
            .Define("Main")
            .Run<TypedTracePayloadStep, TypedTracePayload>()
                .Produce<TypedTracePayload>(output => output, TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        var traceValue = Assert.Single(traceStep.ProducedValues);
        Assert.Equal(typeof(TypedTracePayload).FullName, traceValue.TypeName);
        Assert.Null(traceValue.Name);
        Assert.Equal("Produce", traceValue.Source.ToString());
        Assert.Equal("Serialized", traceValue.CaptureStatus.ToString());
        Assert.Equal("""{"Value":"typed-json"}""", traceValue.SerializedValue);
        Assert.Null(traceValue.SerializationFailureReason);
    }

    /// <summary>
    /// 名前付き Produce が名前と JSON 値を trace に残すことを確認します。
    /// </summary>
    [Fact(DisplayName = "explicit trace capture records named produced value")]
    public async Task ExplicitTraceCaptureRecordsNamedProducedValue()
    {
        CompositeStep<TypedTracePayload> step = CompositeStep
            .Define("Main")
            .Run<TypedTracePayloadStep, TypedTracePayload>()
                .Produce<string>("trace-name", output => output.Value, TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        var traceValue = Assert.Single(traceStep.ProducedValues);
        Assert.Equal(typeof(string).FullName, traceValue.TypeName);
        Assert.Equal("trace-name", traceValue.Name);
        Assert.Equal("Produce", traceValue.Source.ToString());
        Assert.Equal("Serialized", traceValue.CaptureStatus.ToString());
        Assert.Equal("\"typed-json\"", traceValue.SerializedValue);
        Assert.Null(traceValue.SerializationFailureReason);
    }

    /// <summary>
    /// StoreAs の capture が source と保存値を trace に残すことを確認します。
    /// </summary>
    [Fact(DisplayName = "StoreAs trace capture records stored output")]
    public async Task StoreAsTraceCaptureRecordsStoredOutput()
    {
        CompositeStep<StoredTraceOutput> step = CompositeStep
            .Define("Main")
            .Run<StoredTraceOutputStep, StoredTraceOutput>()
                .StoreAs(TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        var traceValue = Assert.Single(traceStep.ProducedValues);
        Assert.Equal(typeof(StoredTraceOutput).FullName, traceValue.TypeName);
        Assert.Null(traceValue.Name);
        Assert.Equal("StoreAs", traceValue.Source.ToString());
        Assert.Equal("Serialized", traceValue.CaptureStatus.ToString());
        Assert.Equal("""{"Value":"stored-json"}""", traceValue.SerializedValue);
        Assert.Null(traceValue.SerializationFailureReason);
    }

    /// <summary>
    /// redacted capture が metadata だけを残し、値本文を残さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "redacted produced value does not expose serialized value")]
    public async Task RedactedProducedValueDoesNotExposeSerializedValue()
    {
        CompositeStep<TypedTracePayload> step = CompositeStep
            .Define("Main")
            .Run<TypedTracePayloadStep, TypedTracePayload>()
                .Produce<TypedTracePayload>(output => output, TraceValueCapture.Redacted);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        var traceValue = Assert.Single(traceStep.ProducedValues);
        Assert.Equal(typeof(TypedTracePayload).FullName, traceValue.TypeName);
        Assert.Equal("Produce", traceValue.Source.ToString());
        Assert.Equal("Redacted", traceValue.CaptureStatus.ToString());
        Assert.Null(traceValue.SerializedValue);
        Assert.Null(traceValue.SerializationFailureReason);
    }

    /// <summary>
    /// 直列化できない値が workflow を失敗させず trace value に記録されることを確認します。
    /// </summary>
    [Fact(DisplayName = "non serializable produced value is marked without failing workflow")]
    public async Task NonSerializableProducedValueIsMarkedWithoutFailingWorkflow()
    {
        CompositeStep<NonSerializableTraceValue> step = CompositeStep
            .Define("Main")
            .Run<NonSerializableTraceValueStep, NonSerializableTraceValue>()
                .Produce<NonSerializableTraceValue>(output => output, TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        var traceValue = Assert.Single(traceStep.ProducedValues);
        Assert.Equal(typeof(NonSerializableTraceValue).FullName, traceValue.TypeName);
        Assert.Equal("Produce", traceValue.Source.ToString());
        Assert.Equal("NotSerializable", traceValue.CaptureStatus.ToString());
        Assert.Null(traceValue.SerializedValue);
        Assert.Equal(
            "Trace value serialization failed: InvalidOperationException.",
            traceValue.SerializationFailureReason);
        Assert.DoesNotContain(
            NonSerializableTraceValue.SecretValue,
            traceValue.SerializationFailureReason,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// retry 途中の失敗試行は値を残さず、成功試行だけが値を残すことを確認します。
    /// </summary>
    [Fact(DisplayName = "failed attempt does not capture produced values")]
    public async Task FailedAttemptDoesNotCaptureProducedValues()
    {
        TraceValueContractState.Reset();
        CompositeStep<AttemptTraceOutput> step = CompositeStep
            .Define("Main")
            .Run<SecondAttemptTraceStep, AttemptTraceOutput>()
                .Produce<AttemptTraceOutput>(output => output, TraceValueCapture.Serialized);
        var options = new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 2 },
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.True(result.Succeeded);
        ExecutionTraceStep[] attempts = result.Trace!.Steps
            .Where(traceStep => traceStep.StepName == nameof(SecondAttemptTraceStep))
            .ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal(ExecutionTraceStepStatus.Failed, attempts[0].Status);
        Assert.Empty(attempts[0].ProducedValues);
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, attempts[1].Status);
        var traceValue = Assert.Single(attempts[1].ProducedValues);
        Assert.Equal("""{"Value":"attempt-2"}""", traceValue.SerializedValue);
    }

    /// <summary>
    /// producer 失敗時は失敗 trace に値を残さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "produce failure does not capture produced values")]
    public async Task ProduceFailureDoesNotCaptureProducedValues()
    {
        CompositeStep<TypedTracePayload> step = CompositeStep
            .Define("Main")
            .Run<TypedTracePayloadStep, TypedTracePayload>()
                .Produce<TypedTracePayload>(
                    _ => throw new InvalidOperationException("produce failed"),
                    TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Empty(traceStep.ProducedValues);
    }

    /// <summary>
    /// timeout した Step の failed trace が trace value を残さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "timeout failure does not capture produced values")]
    public async Task TimeoutFailureDoesNotCaptureProducedValues()
    {
        CompositeStep<TraceFailureOutput> step = CompositeStep
            .Define("Main")
            .RunAsync<TimeoutTraceValueStep, TraceFailureOutput>()
                .Produce<TraceFailureOutput>(output => output, TraceValueCapture.Serialized);
        var options = new WorkflowExecutionOptions
        {
            StepTimeout = TimeSpan.FromMilliseconds(50),
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, result.ErrorCode);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(TimeoutTraceValueStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Empty(traceStep.ProducedValues);
    }

    /// <summary>
    /// 外部 cancel した Step の failed trace が trace value を残さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "external cancellation failure does not capture produced values")]
    public async Task ExternalCancellationFailureDoesNotCaptureProducedValues()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        CompositeStep<TraceFailureOutput> step = CompositeStep
            .Define("Main")
            .RunAsync<CanceledTraceValueStep, TraceFailureOutput>()
                .Produce<TraceFailureOutput>(output => output, TraceValueCapture.Serialized);

        Task<WorkflowResult> workflowTask = step.ExecuteWorkflowAsync(cancellationToken: cancellationTokenSource.Token);
        await cancellationTokenSource.CancelAsync();
        WorkflowResult result = await workflowTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(CanceledTraceValueStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Empty(traceStep.ProducedValues);
    }

    /// <summary>
    /// 重複登録で失敗した Step の failed trace が trace value を残さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "duplicate registration failure does not capture produced values")]
    public async Task DuplicateRegistrationFailureDoesNotCaptureProducedValues()
    {
        CompositeStep<TraceFailureOutput> step = CompositeStep
            .Define("Main")
            .Run<NamedTraceValueStep, TraceFailureOutput>()
                .Produce<string>("same", output => output.Value)
            .Run<DuplicateTraceValueStep, TraceFailureOutput>()
                .Produce<string>("same", output => output.Value, TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        Assert.Equal(2, result.Trace!.Steps.Count);
        ExecutionTraceStep traceStep = result.Trace.Steps[1];
        Assert.Equal(nameof(DuplicateTraceValueStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Empty(traceStep.ProducedValues);
    }

    /// <summary>
    /// 複数 producer の途中失敗で failed trace が部分的な trace value を残さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "partial producer failure does not capture produced values")]
    public async Task PartialProducerFailureDoesNotCaptureProducedValues()
    {
        CompositeStep<TraceFailureOutput> step = CompositeStep
            .Define("Main")
            .Run<NamedTraceValueStep, TraceFailureOutput>()
                .Produce<string>("partial", output => output.Value, TraceValueCapture.Serialized)
                .Produce<string>("partial", output => output.Value, TraceValueCapture.Serialized);

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(NamedTraceValueStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Empty(traceStep.ProducedValues);
    }

    /// <summary>
    /// 型付き trace capture に使う値を表します。
    /// </summary>
    private sealed class TypedTracePayload
    {
        /// <summary>
        /// trace に保存される文字列値を初期化します。
        /// </summary>
        public TypedTracePayload(string value)
        {
            Value = value;
        }

        /// <summary>
        /// JSON へ保存される値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// StoreAs の trace capture に使う値を表します。
    /// </summary>
    private sealed class StoredTraceOutput
    {
        /// <summary>
        /// StoreAs で保存される文字列値を初期化します。
        /// </summary>
        public StoredTraceOutput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// JSON へ保存される値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// retry の trace capture に使う値を表します。
    /// </summary>
    private sealed class AttemptTraceOutput
    {
        /// <summary>
        /// 試行番号を含む文字列値を初期化します。
        /// </summary>
        public AttemptTraceOutput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 成功試行で保存される値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// 直列化時に例外を投げる値を表します。
    /// </summary>
    private sealed class NonSerializableTraceValue
    {
        /// <summary>
        /// trace に漏れてはいけない例外 message 内の値を取得します。
        /// </summary>
        public const string SecretValue = "secret-token-for-trace-value";

        /// <summary>
        /// 直列化失敗を起こす getter を取得します。
        /// </summary>
        public string ThrowingValue => throw new InvalidOperationException($"serialization failed: {SecretValue}");
    }

    /// <summary>
    /// failed trace の trace value 検査に使う値を表します。
    /// </summary>
    private sealed class TraceFailureOutput
    {
        /// <summary>
        /// failed trace 検査用の文字列値を初期化します。
        /// </summary>
        public TraceFailureOutput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// producer が保存しようとする値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// 型付き trace capture の値を返す Step です。
    /// </summary>
    private sealed class TypedTracePayloadStep : IStep<TypedTracePayload>
    {
        /// <summary>
        /// 型付き Produce へ渡す値を返します。
        /// </summary>
        public TypedTracePayload Execute(StepInput input)
        {
            return new TypedTracePayload("typed-json");
        }
    }

    /// <summary>
    /// StoreAs 用の値を返す Step です。
    /// </summary>
    private sealed class StoredTraceOutputStep : IStep<StoredTraceOutput>
    {
        /// <summary>
        /// StoreAs へ渡す値を返します。
        /// </summary>
        public StoredTraceOutput Execute(StepInput input)
        {
            return new StoredTraceOutput("stored-json");
        }
    }

    /// <summary>
    /// 直列化できない値を返す Step です。
    /// </summary>
    private sealed class NonSerializableTraceValueStep : IStep<NonSerializableTraceValue>
    {
        /// <summary>
        /// trace 直列化で失敗する値を返します。
        /// </summary>
        public NonSerializableTraceValue Execute(StepInput input)
        {
            return new NonSerializableTraceValue();
        }
    }

    /// <summary>
    /// 2 回目だけ成功する retry 用 Step です。
    /// </summary>
    private sealed class SecondAttemptTraceStep : IStep<AttemptTraceOutput>
    {
        /// <summary>
        /// 1 回目は例外を投げ、2 回目は値を返します。
        /// </summary>
        public AttemptTraceOutput Execute(StepInput input)
        {
            int attempt = TraceValueContractState.NextAttempt();
            if (attempt == 1)
            {
                throw new InvalidOperationException("first attempt failed");
            }

            return new AttemptTraceOutput($"attempt-{attempt}");
        }
    }

    /// <summary>
    /// timeout で producer 実行前に失敗する非同期 Step です。
    /// </summary>
    private sealed class TimeoutTraceValueStep : IAsyncStep<TraceFailureOutput>
    {
        /// <summary>
        /// timeout 用 token が cancel されるまで待機します。
        /// </summary>
        public async Task<TraceFailureOutput> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            return new TraceFailureOutput("timeout");
        }
    }

    /// <summary>
    /// 外部 cancel で producer 実行前に失敗する非同期 Step です。
    /// </summary>
    private sealed class CanceledTraceValueStep : IAsyncStep<TraceFailureOutput>
    {
        /// <summary>
        /// 外部 cancel 用 token が cancel されるまで待機します。
        /// </summary>
        public async Task<TraceFailureOutput> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            return new TraceFailureOutput("canceled");
        }
    }

    /// <summary>
    /// 名前付き値を返す trace value 検査用 Step です。
    /// </summary>
    private sealed class NamedTraceValueStep : IStep<TraceFailureOutput>
    {
        /// <summary>
        /// 名前付き producer へ渡す値を返します。
        /// </summary>
        public TraceFailureOutput Execute(StepInput input)
        {
            return new TraceFailureOutput("named");
        }
    }

    /// <summary>
    /// 重複登録を発生させる trace value 検査用 Step です。
    /// </summary>
    private sealed class DuplicateTraceValueStep : IStep<TraceFailureOutput>
    {
        /// <summary>
        /// 既存の名前付き値と同じ key へ登録する値を返します。
        /// </summary>
        public TraceFailureOutput Execute(StepInput input)
        {
            return new TraceFailureOutput("duplicate");
        }
    }

    /// <summary>
    /// retry 試行数を検査間で分離して記録します。
    /// </summary>
    private static class TraceValueContractState
    {
        private static readonly object Gate = new();
        private static int attempts;

        /// <summary>
        /// 記録済み試行数を初期化します。
        /// </summary>
        public static void Reset()
        {
            lock (Gate)
            {
                attempts = 0;
            }
        }

        /// <summary>
        /// 試行数を進め、現在の試行番号を返します。
        /// </summary>
        public static int NextAttempt()
        {
            lock (Gate)
            {
                attempts++;

                return attempts;
            }
        }
    }
}
