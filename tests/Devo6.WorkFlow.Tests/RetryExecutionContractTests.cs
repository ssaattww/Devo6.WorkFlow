using System.Collections.Immutable;
using System.Reflection;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using Microsoft.Extensions.Logging;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// retry 実行の公開契約を検査します。
/// </summary>
public sealed class RetryExecutionContractTests
{
    /// <summary>
    /// WorkflowExecutionOptions が retry 設定を公開し、MaxAttempts が初回を含む最大試行回数であることを確認します。
    /// </summary>
    [Fact(DisplayName = "WorkflowExecutionOptions exposes RetryOptions with MaxAttempts")]
    public void WorkflowExecutionOptionsExposesRetryOptionsWithMaxAttempts()
    {
        PropertyInfo? retryProperty = typeof(WorkflowExecutionOptions).GetProperty(
            "Retry",
            BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo? maxAttemptsProperty = typeof(RetryOptions).GetProperty(
            "MaxAttempts",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(retryProperty);
        Assert.Equal(typeof(RetryOptions), retryProperty!.PropertyType);
        Assert.True(retryProperty.CanRead);
        Assert.NotNull(retryProperty.SetMethod);
        Assert.True(retryProperty.SetMethod!.IsPublic);
        Assert.NotNull(maxAttemptsProperty);
        Assert.Equal(typeof(int), maxAttemptsProperty!.PropertyType);
        Assert.True(maxAttemptsProperty.CanRead);
        Assert.NotNull(maxAttemptsProperty.SetMethod);
        Assert.True(maxAttemptsProperty.SetMethod!.IsPublic);

        var options = new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
        };

        Assert.Equal(3, options.Retry!.MaxAttempts);
    }

    /// <summary>
    /// ExecutionTraceStep が 1 始まりの試行番号を公開することを確認します。
    /// </summary>
    [Fact(DisplayName = "ExecutionTraceStep exposes attempt number")]
    public void ExecutionTraceStepExposesAttemptNumber()
    {
        var step = new ExecutionTraceStep(
            "RetryStep",
            ExecutionTraceStepStatus.Failed,
            TimeSpan.FromMilliseconds(10),
            WorkflowErrorCodes.StepExecutionFailed,
            2);

        Assert.Equal(2, step.Attempt);
    }

    /// <summary>
    /// 通常例外の Step 本体が 3 回目で成功し、各試行が trace と log scope に残ることを確認します。
    /// </summary>
    [Fact(DisplayName = "step body exception retries until success and records attempts")]
    public async Task StepBodyExceptionRetriesUntilSuccessAndRecordsAttempts()
    {
        RetryContractState.Reset();
        var loggerFactory = new RecordingLoggerFactory();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<ThirdAttemptSucceedsStep, RetryOutput>()
                .Produce<NextInput>(output =>
                {
                    RetryContractState.Add($"produce:{output.Value}");

                    return new NextInput(output.Value);
                })
            .Run<FollowingStep, string>()
                .StoreAs();
        var options = new WorkflowExecutionOptions(loggerFactory)
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ["retry-step:1", "retry-step:2", "retry-step:3", "produce:ok-3", "following:ok-3"],
            RetryContractState.Snapshot());
        ExecutionTraceStep[] retryTrace = result.Trace!.Steps
            .Where(traceStep => traceStep.StepName == nameof(ThirdAttemptSucceedsStep))
            .ToArray();
        Assert.Equal(
            [
                (ExecutionTraceStepStatus.Failed, WorkflowErrorCodes.StepExecutionFailed, 1),
                (ExecutionTraceStepStatus.Failed, WorkflowErrorCodes.StepExecutionFailed, 2),
                (ExecutionTraceStepStatus.Succeeded, null, 3),
            ],
            retryTrace.Select(traceStep => (traceStep.Status, traceStep.ErrorCode, traceStep.Attempt)).ToArray());
        ExecutionTraceStep followingTrace = Assert.Single(
            result.Trace.Steps.Where(traceStep => traceStep.StepName == nameof(FollowingStep)));
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, followingTrace.Status);
        Assert.Equal(1, followingTrace.Attempt);
        Assert.Contains(loggerFactory.Entries, entry => entry.Message.Contains("Step started", StringComparison.Ordinal)
            && entry.GetScopeValue<int>("Attempt") == 1);
        Assert.Contains(loggerFactory.Entries, entry => entry.Message.Contains("Step started", StringComparison.Ordinal)
            && entry.GetScopeValue<int>("Attempt") == 2);
        Assert.Contains(loggerFactory.Entries, entry => entry.Message.Contains("Step started", StringComparison.Ordinal)
            && entry.GetScopeValue<int>("Attempt") == 3);
    }

    /// <summary>
    /// 通常例外の Step 本体が全試行で失敗し、後続 Step と Produce が実行されないことを確認します。
    /// </summary>
    [Fact(DisplayName = "step body exception stops after max attempts")]
    public async Task StepBodyExceptionStopsAfterMaxAttempts()
    {
        RetryContractState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<AlwaysFailsStep, RetryOutput>()
                .Produce<NextInput>(output =>
                {
                    RetryContractState.Add($"produce:{output.Value}");

                    return new NextInput(output.Value);
                })
            .Run<FollowingStep, string>()
                .StoreAs();
        var options = new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        Assert.Equal(["always-fails:1", "always-fails:2", "always-fails:3"], RetryContractState.Snapshot());
        ExecutionTraceStep[] traceSteps = result.Trace!.Steps.ToArray();
        Assert.Equal(3, traceSteps.Length);
        Assert.All(traceSteps, traceStep =>
        {
            Assert.Equal(nameof(AlwaysFailsStep), traceStep.StepName);
            Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
            Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, traceStep.ErrorCode);
        });
        Assert.Equal([1, 2, 3], traceSteps.Select(traceStep => traceStep.Attempt).ToArray());
    }

    /// <summary>
    /// timeout は retry 対象外であり、1 回の失敗で Produce と後続 Step を止めることを確認します。
    /// </summary>
    [Fact(DisplayName = "step timeout is not retried")]
    public async Task StepTimeoutIsNotRetried()
    {
        RetryContractState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .RunAsync<TimeoutStep, RetryOutput>()
                .Produce<NextInput>(output =>
                {
                    RetryContractState.Add($"timeout-produce:{output.Value}");

                    return new NextInput(output.Value);
                })
            .Run<FollowingStep, string>()
                .StoreAs();
        var options = new WorkflowExecutionOptions
        {
            StepTimeout = TimeSpan.FromMilliseconds(30),
            Retry = new RetryOptions { MaxAttempts = 3 },
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, result.ErrorCode);
        Assert.Equal(["timeout-step:1"], RetryContractState.Snapshot());
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(TimeoutStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, traceStep.ErrorCode);
        Assert.Equal(1, traceStep.Attempt);
    }

    /// <summary>
    /// 外部キャンセルは retry 対象外であり、1 回の失敗で Produce と後続 Step を止めることを確認します。
    /// </summary>
    [Fact(DisplayName = "external cancellation is not retried")]
    public async Task ExternalCancellationIsNotRetried()
    {
        RetryContractState.Reset();
        using var cancellationTokenSource = new CancellationTokenSource();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .RunAsync<ExternallyCanceledStep, RetryOutput>()
                .Produce<NextInput>(output =>
                {
                    RetryContractState.Add($"canceled-produce:{output.Value}");

                    return new NextInput(output.Value);
                })
            .Run<FollowingStep, string>()
                .StoreAs();
        var options = new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
        };

        Task<WorkflowResult> workflowTask = step.ExecuteWorkflowAsync(options, cancellationTokenSource.Token);
        await RetryContractState.WaitForAsyncStepStartAsync(TimeSpan.FromSeconds(1));
        await cancellationTokenSource.CancelAsync();
        WorkflowResult result = await workflowTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.Equal(["external-cancel-step:1"], RetryContractState.Snapshot());
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(ExternallyCanceledStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, traceStep.ErrorCode);
        Assert.Equal(1, traceStep.Attempt);
    }

    /// <summary>
    /// Produce 失敗は Step 本体を retry せず、既存の Step 失敗として扱うことを確認します。
    /// </summary>
    [Fact(DisplayName = "produce failure does not retry step body")]
    public async Task ProduceFailureDoesNotRetryStepBody()
    {
        RetryContractState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<ProduceFailureSourceStep, RetryOutput>()
                .Produce<NextInput>(output =>
                {
                    RetryContractState.Add($"produce-failure:{output.Value}");
                    throw new InvalidOperationException("produce failed");
                })
            .Run<FollowingStep, string>()
                .StoreAs();
        var options = new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        Assert.Contains("produce failed", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(["produce-source:1", "produce-failure:source-ok"], RetryContractState.Snapshot());
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal(nameof(ProduceFailureSourceStep), traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, traceStep.ErrorCode);
        Assert.Equal(1, traceStep.Attempt);
    }

    /// <summary>
    /// retry 対象 Step が返す観測用の値を保持します。
    /// </summary>
    private sealed record RetryOutput(string Value);

    /// <summary>
    /// Produce から後続 Step へ渡す観測用の値を保持します。
    /// </summary>
    private sealed record NextInput(string Value);

    /// <summary>
    /// 3 回目の実行だけ成功する retry 検査用 Step です。
    /// </summary>
    private sealed class ThirdAttemptSucceedsStep : IStep<RetryOutput>
    {
        /// <summary>
        /// 1 回目と 2 回目は通常例外を投げ、3 回目に成功します。
        /// </summary>
        public RetryOutput Execute(StepInput input)
        {
            int attempt = RetryContractState.IncrementAttempt(nameof(ThirdAttemptSucceedsStep));
            RetryContractState.Add($"retry-step:{attempt}");

            if (attempt < 3)
            {
                throw new InvalidOperationException($"boom-{attempt}");
            }

            return new RetryOutput($"ok-{attempt}");
        }
    }

    /// <summary>
    /// すべての試行で通常例外を投げる retry 検査用 Step です。
    /// </summary>
    private sealed class AlwaysFailsStep : IStep<RetryOutput>
    {
        /// <summary>
        /// 全試行で通常例外を投げます。
        /// </summary>
        public RetryOutput Execute(StepInput input)
        {
            int attempt = RetryContractState.IncrementAttempt(nameof(AlwaysFailsStep));
            RetryContractState.Add($"always-fails:{attempt}");

            throw new InvalidOperationException($"boom-{attempt}");
        }
    }

    /// <summary>
    /// timeout が retry 対象にならないことを観測する async Step です。
    /// </summary>
    private sealed class TimeoutStep : IAsyncStep<RetryOutput>
    {
        /// <summary>
        /// timeout 用 token が cancel されるまで待機します。
        /// </summary>
        public async Task<RetryOutput> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            int attempt = RetryContractState.IncrementAttempt(nameof(TimeoutStep));
            RetryContractState.Add($"timeout-step:{attempt}");

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            return new RetryOutput("timeout");
        }
    }

    /// <summary>
    /// 外部キャンセルが retry 対象にならないことを観測する async Step です。
    /// </summary>
    private sealed class ExternallyCanceledStep : IAsyncStep<RetryOutput>
    {
        /// <summary>
        /// 外部 cancel が発火するまで待機します。
        /// </summary>
        public async Task<RetryOutput> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            int attempt = RetryContractState.IncrementAttempt(nameof(ExternallyCanceledStep));
            RetryContractState.Add($"external-cancel-step:{attempt}");
            RetryContractState.MarkAsyncStepStarted();

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            return new RetryOutput("canceled");
        }
    }

    /// <summary>
    /// Produce 失敗時の Step 本体再実行有無を観測する Step です。
    /// </summary>
    private sealed class ProduceFailureSourceStep : IStep<RetryOutput>
    {
        /// <summary>
        /// Produce 失敗時に Step 本体が再実行されないことを観測する値を返します。
        /// </summary>
        public RetryOutput Execute(StepInput input)
        {
            int attempt = RetryContractState.IncrementAttempt(nameof(ProduceFailureSourceStep));
            RetryContractState.Add($"produce-source:{attempt}");

            return new RetryOutput("source-ok");
        }
    }

    /// <summary>
    /// retry 対象 Step の後続実行を観測する Step です。
    /// </summary>
    private sealed class FollowingStep : IStep<string>
    {
        /// <summary>
        /// retry 対象 Step の成功後だけ 1 回実行されることを観測します。
        /// </summary>
        public string Execute(StepInput input)
        {
            string value = input.Get<NextInput>().Value;
            RetryContractState.Add($"following:{value}");

            return value;
        }
    }

    /// <summary>
    /// retry 検査で共有する試行回数と実行イベントを保持します。
    /// </summary>
    private static class RetryContractState
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, int> Attempts = new();
        private static List<string> entries = new();
        private static TaskCompletionSource asyncStepStarted = NewAsyncStepStartedSource();

        /// <summary>
        /// テスト間で共有する観測状態を初期化します。
        /// </summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Attempts.Clear();
                entries = new List<string>();
                asyncStepStarted = NewAsyncStepStartedSource();
            }
        }

        /// <summary>
        /// Step 名ごとの試行回数を進め、現在の試行番号を返します。
        /// </summary>
        public static int IncrementAttempt(string stepName)
        {
            lock (Gate)
            {
                Attempts.TryGetValue(stepName, out int current);
                int next = current + 1;
                Attempts[stepName] = next;

                return next;
            }
        }

        /// <summary>
        /// 観測した実行イベントを記録します。
        /// </summary>
        public static void Add(string value)
        {
            lock (Gate)
            {
                entries.Add(value);
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
        /// 記録済みの実行イベントを取得します。
        /// </summary>
        public static IReadOnlyList<string> Snapshot()
        {
            lock (Gate)
            {
                return entries.ToArray();
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

    /// <summary>
    /// workflow 実行中の logger entry を記録する factory です。
    /// </summary>
    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly RecordingLogger logger;

        /// <summary>
        /// 記録用 logger factory を初期化します。
        /// </summary>
        public RecordingLoggerFactory()
        {
            logger = new RecordingLogger(Entries);
        }

        /// <summary>
        /// 記録済み log entry を取得します。
        /// </summary>
        public List<LogEntry> Entries { get; } = new();

        /// <summary>
        /// 追加 provider はこの検査では使用しません。
        /// </summary>
        public void AddProvider(ILoggerProvider provider)
        {
        }

        /// <summary>
        /// category に依存しない記録用 logger を返します。
        /// </summary>
        public ILogger CreateLogger(string categoryName)
        {
            return logger;
        }

        /// <summary>
        /// 保持する unmanaged resource はありません。
        /// </summary>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 現在の scope と message を entries に保存する logger です。
    /// </summary>
    private sealed class RecordingLogger(List<LogEntry> entries) : ILogger
    {
        private readonly AsyncLocal<ImmutableStack<IReadOnlyDictionary<string, object?>>?> scopes = new();

        /// <summary>
        /// 構造化 scope を記録対象の現在 scope に追加します。
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            IReadOnlyDictionary<string, object?> scope = ToScopeDictionary(state);
            scopes.Value = (scopes.Value ?? ImmutableStack<IReadOnlyDictionary<string, object?>>.Empty).Push(scope);

            return new ScopePopper(scopes);
        }

        /// <summary>
        /// すべての log level を有効として扱います。
        /// </summary>
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <summary>
        /// 現在 scope と message を log entry として保存します。
        /// </summary>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception,
                SnapshotScopes()));
        }

        /// <summary>
        /// logger scope を辞書として扱える形に変換します。
        /// </summary>
        private static IReadOnlyDictionary<string, object?> ToScopeDictionary<TState>(TState state)
            where TState : notnull
        {
            if (state is IReadOnlyDictionary<string, object?> dictionary)
            {
                return dictionary;
            }

            return new Dictionary<string, object?> { ["State"] = state };
        }

        /// <summary>
        /// 現在の scope を外側から内側の順序で取得します。
        /// </summary>
        private IReadOnlyList<IReadOnlyDictionary<string, object?>> SnapshotScopes()
        {
            return (scopes.Value ?? ImmutableStack<IReadOnlyDictionary<string, object?>>.Empty)
                .Reverse()
                .ToArray();
        }
    }

    /// <summary>
    /// BeginScope で追加した scope を破棄時に戻します。
    /// </summary>
    private sealed class ScopePopper : IDisposable
    {
        private readonly AsyncLocal<ImmutableStack<IReadOnlyDictionary<string, object?>>?> scopes;

        /// <summary>
        /// 対象 logger の現在 scope stack を保持します。
        /// </summary>
        public ScopePopper(AsyncLocal<ImmutableStack<IReadOnlyDictionary<string, object?>>?> scopes)
        {
            this.scopes = scopes;
        }

        /// <summary>
        /// BeginScope で追加した scope を取り除きます。
        /// </summary>
        public void Dispose()
        {
            scopes.Value = scopes.Value?.Pop();
        }
    }

    /// <summary>
    /// logger に記録された message と scope の snapshot を保持します。
    /// </summary>
    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Scopes)
    {
        /// <summary>
        /// 記録された scope から指定した値を取得します。
        /// </summary>
        public T? GetScopeValue<T>(string key)
        {
            foreach (IReadOnlyDictionary<string, object?> scope in Scopes.Reverse())
            {
                if (scope.TryGetValue(key, out object? value) && value is T typedValue)
                {
                    return typedValue;
                }
            }

            return default;
        }
    }
}
