using System.Reflection;
using System.Runtime.ExceptionServices;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// 条件付き実行全体の統合契約を検査します。
/// </summary>
public sealed class ConditionalFlowIntegrationTests
{
    /// <summary>
    /// 入れ子 CompositeStep、条件付き分岐、Config、trace、retry が同じ workflow で整合することを確認します。
    /// </summary>
    [Fact(DisplayName = "ConditionalFlow integrates nested composite config trace and retry")]
    public async Task ConditionalFlowIntegratesNestedCompositeConfigTraceAndRetry()
    {
        ConditionalFlowState.Reset();
        CompositeStep<string> step = CreateIntegratedStep();
        string directory = CreateConfigDirectory();
        string configPath = Path.Combine(directory, "appsettings.yaml");
        File.WriteAllText(
            configPath,
            """
            Flow:
              Mode: primary
            RunGate:
              Enabled: true
            Inner:
              Prefix: inner-
            Tap:
              Enabled: true
            """);
        WorkflowExecutionOptions options = CreateOptionsFromConfigFile(step, configPath, directory);
        options.Retry = new RetryOptions { MaxAttempts = 2 };

        Assert.Equal(["Flow", "RunGate", "Inner", "Tap"], step.StepConfigRegistrations.Select(registration => registration.SectionPath).ToArray());
        Assert.Equal([0, 1, 3, 5], step.StepConfigRegistrations.Select(GetStepConfigIndex).ToArray());

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.True(
            result.Succeeded,
            $"{result.ErrorCode}: {result.ErrorMessage}; events={string.Join(",", ConditionalFlowState.Snapshot())}; trace={string.Join(",", result.Trace!.Steps.Select(traceStep => $"{traceStep.StepName}:{traceStep.Status}:{traceStep.ErrorCode}:{traceStep.Attempt}"))}");
        Assert.Equal(
            ["seed:primary", "retry:1", "retry:2", "inner:inner-primary-retry-2", "tap", "then-final:inner-primary-retry-2"],
            ConditionalFlowState.Snapshot());
        Assert.Equal(
            [
                "seed",
                nameof(RetryingDecorateStep),
                nameof(RetryingDecorateStep),
                "outer-if",
                nameof(InvokeInnerCompositeStep),
                "inner-switch",
                nameof(AuditTapStep),
                "then-final",
                "after",
            ],
            result.Trace!.Steps.Select(traceStep => traceStep.StepName).ToArray());
        Assert.Equal(
            [
                (ExecutionTraceStepStatus.Succeeded, null, 1),
                (ExecutionTraceStepStatus.Failed, WorkflowErrorCodes.StepExecutionFailed, 1),
                (ExecutionTraceStepStatus.Succeeded, null, 2),
                (ExecutionTraceStepStatus.Succeeded, null, 1),
                (ExecutionTraceStepStatus.Succeeded, null, 1),
                (ExecutionTraceStepStatus.Succeeded, null, 1),
                (ExecutionTraceStepStatus.Succeeded, null, 1),
                (ExecutionTraceStepStatus.Succeeded, null, 1),
                (ExecutionTraceStepStatus.Succeeded, null, 1),
            ],
            result.Trace.Steps.Select(traceStep => (traceStep.Status, traceStep.ErrorCode, traceStep.Attempt)).ToArray());
        ExecutionTraceStep seedTrace = result.Trace.Steps.Single(traceStep => traceStep.StepName == "seed");
        Assert.Contains(seedTrace.ProducedValues, value => value.Source == ExecutionTraceValueSource.StoreAs
            && value.CaptureStatus == ExecutionTraceValueCaptureStatus.Serialized);
        ExecutionTraceStep runIfTrace = result.Trace.Steps.Single(traceStep =>
            traceStep.StepName == nameof(RetryingDecorateStep)
            && traceStep.Status == ExecutionTraceStepStatus.Succeeded);
        Assert.Contains(runIfTrace.ProducedValues, value => value.Name == "after-run-if"
            && value.SerializedValue == "\"primary-retry-2\"");
    }

    /// <summary>
    /// 未選択 branch を含む Step Config 欠落が実行前検証で止まり、Step が実行されないことを確認します。
    /// </summary>
    [Fact(DisplayName = "ConditionalFlow config validation stops before execution")]
    public void ConditionalFlowConfigValidationStopsBeforeExecution()
    {
        ConditionalFlowState.Reset();
        CompositeStep<string> step = CreateIntegratedStep();
        string directory = CreateConfigDirectory();
        string configPath = Path.Combine(directory, "appsettings.yaml");
        File.WriteAllText(
            configPath,
            """
            Flow:
              Mode: primary
            RunGate:
              Enabled: true
            Tap:
              Enabled: true
            """);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LoadStepConfigsViaReflection(configPath, directory, step.ConfigType!, step.StepConfigRegistrations));

        Assert.Equal("Config section was not found: Inner", exception.Message);
        Assert.Empty(ConditionalFlowState.Snapshot());
    }

    /// <summary>
    /// 条件付き branch 内の timeout が retry されず後続 Step を止めることを確認します。
    /// </summary>
    [Fact(DisplayName = "ConditionalFlow timeout in selected branch is not retried")]
    public async Task ConditionalFlowTimeoutInSelectedBranchIsNotRetried()
    {
        ConditionalFlowState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new FlowValue("primary"))
            .If(
                "outer-if",
                current => true,
                thenBranch => thenBranch
                    .RunIfAsync<NeverCompletesFlowStep, FlowValue>(current => true, current => new FlowValue("fallback"))
                    .Run("after-timeout", current =>
                    {
                        ConditionalFlowState.Add("after-timeout");

                        return current.Text;
                    }),
                elseBranch => elseBranch.Run("else", current => current.Text))
            .Run("after", current =>
            {
                ConditionalFlowState.Add("after");

                return current;
            });
        var options = new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
            StepTimeout = TimeSpan.FromMilliseconds(30),
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, result.ErrorCode);
        Assert.Equal(["timeout-start"], ConditionalFlowState.Snapshot());
        Assert.Equal(["seed", nameof(NeverCompletesFlowStep)], result.Trace!.Steps.Select(traceStep => traceStep.StepName).ToArray());
        Assert.Equal(
            [
                (ExecutionTraceStepStatus.Succeeded, null, 1),
                (ExecutionTraceStepStatus.Failed, WorkflowErrorCodes.StepTimeout, 1),
            ],
            result.Trace.Steps.Select(traceStep => (traceStep.Status, traceStep.ErrorCode, traceStep.Attempt)).ToArray());
    }

    /// <summary>
    /// T59 統合検査用 workflow を作成します。
    /// </summary>
    /// <returns>条件付き実行を横断する workflow。</returns>
    private static CompositeStep<string> CreateIntegratedStep()
    {
        return CompositeStep
            .Define("Main")
            .Run("seed", input =>
            {
                FlowConfig config = input.Context.Get<FlowConfig>();
                ConditionalFlowState.Add($"seed:{config.Mode}");
                input.Context.Set("current-flow", new FlowValue(config.Mode));

                return new FlowValue(config.Mode);
            })
                .WithConfig<BoundaryConfig>()
                .WithConfig<FlowConfig>("Flow")
                .StoreAs(TraceValueCapture.Serialized)
            .RunIf<RetryingDecorateStep, FlowValue>(
                (current, input) => input.Context.Get<RunGateConfig>().Enabled,
                (current, input) => new FlowValue(current.Text + "-skipped"))
                .WithConfig<RunGateConfig>("RunGate")
                .Produce("after-run-if", current => current.Text, TraceValueCapture.Serialized)
            .If(
                "outer-if",
                (current, input) => current.Kind == FlowKind.Primary,
                thenBranch => thenBranch
                    .Run<InvokeInnerCompositeStep, FlowValue>()
                        .WithConfig<InnerConfig>("Inner")
                    .Switch<FlowKind, string>(
                        "inner-switch",
                        current => current.Kind,
                        cases => cases
                            .Case(FlowKind.Primary, primary => primary
                                .TapIf<AuditTapStep>((current, input) => input.Context.Get<TapConfig>().Enabled)
                                    .WithConfig<TapConfig>("Tap")
                                .Run("then-final", current =>
                                {
                                    ConditionalFlowState.Add($"then-final:{current.Text}");

                                    return current.Text + ":then";
                                }))
                            .Default(defaultBranch => defaultBranch.Run("default-final", current => current.Text + ":default"))),
                elseBranch => elseBranch.Run("else-final", current => current.Text + ":else"))
            .Run("after", current => current + ":after");
    }

    /// <summary>
    /// 一時 Config directory を作成します。
    /// </summary>
    /// <returns>作成した directory path。</returns>
    private static string CreateConfigDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-workflow-t59", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return directory;
    }

    /// <summary>
    /// 標準 Config loader で Step Config を読み、実行 option に設定します。
    /// </summary>
    /// <param name="step">Config metadata を持つ workflow。</param>
    /// <param name="configPath">root Config YAML path。</param>
    /// <param name="entryDirectory">Entry directory として使う path。</param>
    /// <returns>Step Config を持つ実行 option。</returns>
    private static WorkflowExecutionOptions CreateOptionsFromConfigFile(
        CompositeStep<string> step,
        string configPath,
        string entryDirectory)
    {
        object stepConfigs = LoadStepConfigsViaReflection(configPath, entryDirectory, step.ConfigType!, step.StepConfigRegistrations);
        MethodInfo method = typeof(WorkflowExecutionOptions).GetMethod(
            "WithStepConfigs",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WithStepConfigs method was not found.");

        return (WorkflowExecutionOptions)method.Invoke(new WorkflowExecutionOptions(), [stepConfigs])!;
    }

    /// <summary>
    /// internal の StandardConfigLoader.LoadStepConfigs を呼び出します。
    /// </summary>
    /// <param name="configPath">root Config YAML path。</param>
    /// <param name="entryDirectory">Entry directory として使う path。</param>
    /// <param name="boundaryConfigType">境界 Config 型。</param>
    /// <param name="registrations">Step Config metadata。</param>
    /// <returns>読み込んだ Step Config 値の一覧。</returns>
    private static object LoadStepConfigsViaReflection(
        string configPath,
        string entryDirectory,
        Type boundaryConfigType,
        IReadOnlyList<StepConfigRegistration> registrations)
    {
        Type loaderType = typeof(CompositeStep).Assembly.GetType("Devo6.WorkFlow.Engine.StandardConfigLoader", throwOnError: true)
            ?? throw new InvalidOperationException("StandardConfigLoader type was not found.");
        MethodInfo method = loaderType.GetMethod(
            "LoadStepConfigs",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LoadStepConfigs method was not found.");

        try
        {
            return method.Invoke(null, [configPath, entryDirectory, boundaryConfigType, registrations, null])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>
    /// 公開 API を増やさずに Step Config metadata の StepIndex を取得します。
    /// </summary>
    /// <param name="registration">Step Config metadata。</param>
    /// <returns>metadata が保持する Step index。</returns>
    private static int GetStepConfigIndex(StepConfigRegistration registration)
    {
        PropertyInfo property = typeof(StepConfigRegistration).GetProperty(
            "StepIndex",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("StepConfigRegistration.StepIndex property was not found.");

        return (int)property.GetValue(registration)!;
    }

    /// <summary>
    /// 条件付き実行の現在値を保持します。
    /// </summary>
    private sealed class FlowValue
    {
        /// <summary>
        /// 検査用の文字列値を初期化します。
        /// </summary>
        /// <param name="text">検査用の文字列値。</param>
        public FlowValue(string text)
        {
            Text = text;
        }

        /// <summary>
        /// 検査用の文字列値を取得します。
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// 文字列値から Switch の分岐キーを取得します。
        /// </summary>
        public FlowKind Kind => Text.StartsWith("primary", StringComparison.Ordinal)
            || Text.StartsWith("inner-primary", StringComparison.Ordinal)
                ? FlowKind.Primary
                : FlowKind.Other;
    }

    /// <summary>
    /// Switch の分岐キーを表します。
    /// </summary>
    private enum FlowKind
    {
        /// <summary>
        /// 主経路を表します。
        /// </summary>
        Primary,

        /// <summary>
        /// その他の経路を表します。
        /// </summary>
        Other,
    }

    /// <summary>
    /// workflow 全体の境界 Config です。
    /// </summary>
    private sealed class BoundaryConfig
    {
        /// <summary>
        /// seed Step の Config を取得または設定します。
        /// </summary>
        public FlowConfig Flow { get; set; } = new();

        /// <summary>
        /// RunIf 条件用 Config を取得または設定します。
        /// </summary>
        public RunGateConfig RunGate { get; set; } = new();

        /// <summary>
        /// 内側 CompositeStep 用 Config を取得または設定します。
        /// </summary>
        public InnerConfig Inner { get; set; } = new();

        /// <summary>
        /// TapIf 条件用 Config を取得または設定します。
        /// </summary>
        public TapConfig Tap { get; set; } = new();
    }

    /// <summary>
    /// seed Step の Config です。
    /// </summary>
    private sealed class FlowConfig
    {
        /// <summary>
        /// 初期 mode を取得または設定します。
        /// </summary>
        public string Mode { get; set; } = "";
    }

    /// <summary>
    /// RunIf 条件用 Config です。
    /// </summary>
    private sealed class RunGateConfig
    {
        /// <summary>
        /// RunIf Step を実行するかどうかを取得または設定します。
        /// </summary>
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 内側 CompositeStep 用 Config です。
    /// </summary>
    private sealed class InnerConfig
    {
        /// <summary>
        /// 内側の戻り値に付ける prefix を取得または設定します。
        /// </summary>
        public string Prefix { get; set; } = "";
    }

    /// <summary>
    /// TapIf 条件用 Config です。
    /// </summary>
    private sealed class TapConfig
    {
        /// <summary>
        /// TapIf Step を実行するかどうかを取得または設定します。
        /// </summary>
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 1 回失敗して retry 後に現在値を更新する Step です。
    /// </summary>
    private sealed class RetryingDecorateStep : IStep<FlowValue>
    {
        /// <summary>
        /// retry 回数を記録し、2 回目に成功します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>retry 後の現在値。</returns>
        public FlowValue Execute(StepInput input)
        {
            int attempt = ConditionalFlowState.IncrementRetryAttempts();
            ConditionalFlowState.Add($"retry:{attempt}");
            if (attempt == 1)
            {
                throw new InvalidOperationException("retry first attempt failed");
            }

            var value = new FlowValue(input.Get<FlowValue>().Text + $"-retry-{attempt}");
            input.Context.Set("current-flow", value);

            return value;
        }
    }

    /// <summary>
    /// 内側 CompositeStep を同じ StepInput で実行する Step です。
    /// </summary>
    private sealed class InvokeInnerCompositeStep : IStep<FlowValue>
    {
        /// <summary>
        /// 外側で登録済みの Config と値を内側 CompositeStep から参照します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>内側 CompositeStep の戻り値。</returns>
        public FlowValue Execute(StepInput input)
        {
            CompositeStep<FlowValue> inner = CompositeStep
                .Define("Inner")
                .Run("inner-transform", innerInput =>
                {
                    InnerConfig config = innerInput.Context.Get<InnerConfig>();
                    FlowValue current = innerInput.Context.Get<FlowValue>("current-flow");
                    var value = new FlowValue(config.Prefix + current.Text);
                    ConditionalFlowState.Add($"inner:{value.Text}");

                    return value;
                });

            return inner.Execute(input);
        }
    }

    /// <summary>
    /// TapIf から実行される監査 Step です。
    /// </summary>
    private sealed class AuditTapStep : IStep<Unit>
    {
        /// <summary>
        /// TapIf の実行を記録します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Unit 値。</returns>
        public Unit Execute(StepInput input)
        {
            ConditionalFlowState.Add("tap");

            return Unit.Value;
        }
    }

    /// <summary>
    /// timeout 検査用に完了しない非同期 Step です。
    /// </summary>
    private sealed class NeverCompletesFlowStep : IAsyncStep<FlowValue>
    {
        /// <summary>
        /// cancellation token が cancel されるまで待機します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <param name="cancellationToken">キャンセル通知。</param>
        /// <returns>到達しない戻り値。</returns>
        public async Task<FlowValue> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            ConditionalFlowState.Add("timeout-start");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return new FlowValue("unreachable");
        }
    }

    /// <summary>
    /// 統合検査の観測状態を保持します。
    /// </summary>
    private static class ConditionalFlowState
    {
        private static readonly object Gate = new();
        private static readonly List<string> Events = [];

        /// <summary>
        /// retry Step の実行回数を取得します。
        /// </summary>
        public static int RetryAttempts { get; private set; }

        /// <summary>
        /// 観測状態を初期値へ戻します。
        /// </summary>
        public static void Reset()
        {
            lock (Gate)
            {
                RetryAttempts = 0;
                Events.Clear();
            }
        }

        /// <summary>
        /// retry Step の実行回数を増やします。
        /// </summary>
        /// <returns>増加後の実行回数。</returns>
        public static int IncrementRetryAttempts()
        {
            lock (Gate)
            {
                RetryAttempts++;

                return RetryAttempts;
            }
        }

        /// <summary>
        /// 観測 event を追加します。
        /// </summary>
        /// <param name="value">追加する event。</param>
        public static void Add(string value)
        {
            lock (Gate)
            {
                Events.Add(value);
            }
        }

        /// <summary>
        /// 観測 event の snapshot を取得します。
        /// </summary>
        /// <returns>観測 event の配列。</returns>
        public static string[] Snapshot()
        {
            lock (Gate)
            {
                return Events.ToArray();
            }
        }
    }
}
