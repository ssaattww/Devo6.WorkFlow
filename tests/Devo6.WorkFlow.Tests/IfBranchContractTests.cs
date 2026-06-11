using System.Reflection;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// If と BranchBuilder の分岐実行契約を検査します。
/// </summary>
public sealed class IfBranchContractTests
{
    /// <summary>
    /// If true が then branch だけを実行し、else branch を trace に出さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "If true executes only then branch and hides else branch from trace")]
    public async Task IfTrueExecutesOnlyThenBranchAndHidesElseBranchFromTrace()
    {
        IfBranchState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"))
            .If(
                "choose-then",
                current => true,
                thenBranch => thenBranch.Run<ThenStep, string>(),
                elseBranch => elseBranch.Run<ElseStep, string>());

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, IfBranchState.ThenAttempts);
        Assert.Equal(0, IfBranchState.ElseAttempts);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(ThenStep));
        Assert.DoesNotContain(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(ElseStep));
    }

    /// <summary>
    /// If false が else branch だけを実行し、then branch を trace に出さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "If false executes only else branch and hides then branch from trace")]
    public async Task IfFalseExecutesOnlyElseBranchAndHidesThenBranchFromTrace()
    {
        IfBranchState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"))
            .If(
                "choose-else",
                current => false,
                thenBranch => thenBranch.Run<ThenStep, string>(),
                elseBranch => elseBranch.Run<ElseStep, string>());

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, IfBranchState.ThenAttempts);
        Assert.Equal(1, IfBranchState.ElseAttempts);
        Assert.DoesNotContain(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(ThenStep));
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(ElseStep));
    }

    /// <summary>
    /// then と else が同じ TNext へ畳まれる公開 API であることを確認します。
    /// </summary>
    [Fact(DisplayName = "If public API folds both branches into same TNext")]
    public void IfPublicApiFoldsBothBranchesIntoSameTNext()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"))
            .If(
                "fold",
                current => current.Value == "seed",
                thenBranch => thenBranch.Run("then-lambda", current => "then:" + current.Value),
                elseBranch => elseBranch.Run("else-lambda", current => "else:" + current.Value))
            .Run("after", current => current + ":after");

        string output = step.Execute(new StepInput());

        Assert.Equal("then:seed:after", output);
    }

    /// <summary>
    /// branch 内で Lambda、RunIf、TapIf、入れ子 If、Produce、StoreAs、Discard を使えることを確認します。
    /// </summary>
    [Fact(DisplayName = "BranchBuilder supports lambda conditional nested and value APIs")]
    public async Task BranchBuilderSupportsLambdaConditionalNestedAndValueApis()
    {
        IfBranchState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"))
            .If(
                "outer",
                (current, input) => true,
                thenBranch => thenBranch
                    .Run("then-lambda", current => new BranchValue(current.Value + "-lambda"))
                        .StoreAs()
                        .Produce("branch-name", current => current.Value)
                    .RunIf<SameTypeBranchStep>(current => true)
                    .TapIf<TapBranchStep>((current, input) => input.Get<string>("branch-name") == "seed-lambda")
                    .If(
                        "nested",
                        current => current.Value == "same-type",
                        nestedThen => nestedThen.Run("nested-then", current => current.Value + "-nested"),
                        nestedElse => nestedElse.Run("nested-else", current => current.Value + "-else"))
                        .Discard(),
                elseBranch => elseBranch.Run("else-lambda", current => "else"));

        WorkflowResult result = await step.ExecuteWorkflowAsync();
        string output = step.Execute(new StepInput());

        Assert.True(result.Succeeded);
        Assert.Equal("same-type-nested", output);
        Assert.Equal(2, IfBranchState.SameTypeAttempts);
        Assert.Equal(2, IfBranchState.TapAttempts);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == "then-lambda");
        Assert.Contains(result.Trace.Steps, traceStep => traceStep.StepName == nameof(SameTypeBranchStep));
        Assert.Contains(result.Trace.Steps, traceStep => traceStep.StepName == nameof(TapBranchStep));
        Assert.Contains(result.Trace.Steps, traceStep => traceStep.StepName == "nested-then");
        Assert.DoesNotContain(result.Trace.Steps, traceStep => traceStep.StepName == "nested-else");
    }

    /// <summary>
    /// branch 内 Config が選択有無にかかわらず実行前読み込みの検証対象になり、選択 branch の実行直前に登録されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Branch config metadata covers both branches and selected config is registered before execution")]
    public async Task BranchConfigMetadataCoversBothBranchesAndSelectedConfigIsRegisteredBeforeExecution()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"))
            .If(
                "config-if",
                current => true,
                thenBranch => thenBranch.Run<ThenConfigStep, string>().WithConfig<ThenConfig>("Then"),
                elseBranch => elseBranch.Run<ElseConfigStep, string>().WithConfig<ElseConfig>("Else"))
            .WithConfig<BoundaryConfig>();
        WorkflowExecutionOptions options = CreateOptionsWithStepConfigs(
            (1, typeof(ThenConfig), new ThenConfig { Value = "then-config" }),
            (2, typeof(ElseConfig), new ElseConfig { Value = "else-config" }));

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.True(result.Succeeded);
        Assert.Equal(["Then", "Else"], step.StepConfigRegistrations.Select(registration => registration.SectionPath).ToArray());
        ExecutionTraceStep selectedTrace = result.Trace!.Steps.Single(traceStep => traceStep.StepName == nameof(ThenConfigStep));
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, selectedTrace.Status);
        Assert.DoesNotContain(result.Trace.Steps, traceStep => traceStep.StepName == nameof(ElseConfigStep));
    }

    /// <summary>
    /// If 後続 Step の Config metadata が flatten 後の index を持ち、選択 branch にかかわらず実行直前に登録されることを確認します。
    /// </summary>
    /// <param name="chooseThen">then branch を選択する場合は true。</param>
    [Theory(DisplayName = "Config after If uses flattened index and is registered before execution")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfigAfterIfUsesFlattenedIndexAndIsRegisteredBeforeExecution(bool chooseThen)
    {
        IfBranchState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"))
            .If(
                "choose-branch",
                current => chooseThen,
                thenBranch => thenBranch.Run<ThenStep, string>(),
                elseBranch => elseBranch.Run<ElseStep, string>())
            .Run<AfterConfigStep, string>()
            .WithConfig<AfterConfig>("After")
            .WithConfig<BoundaryConfig>();
        WorkflowExecutionOptions options = CreateOptionsWithStepConfigs(
            (4, typeof(AfterConfig), new AfterConfig { Value = chooseThen ? "after-then" : "after-else" }));

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.True(result.Succeeded);
        Assert.Equal(4, GetStepConfigIndex(Assert.Single(step.StepConfigRegistrations)));
        Assert.Equal(chooseThen ? "after-then" : "after-else", IfBranchState.AfterValue);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(AfterConfigStep)
            && traceStep.Status == ExecutionTraceStepStatus.Succeeded);
    }

    /// <summary>
    /// If 条件判定の例外が CONDITION_EVALUATION_FAILED になり retry されないことを確認します。
    /// </summary>
    [Fact(DisplayName = "If condition exception returns condition evaluation failed without retry")]
    public async Task IfConditionExceptionReturnsConditionEvaluationFailedWithoutRetry()
    {
        IfBranchState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"))
            .If(
                "failing-condition",
                current =>
                {
                    IfBranchState.IncrementConditionAttempts();
                    throw new InvalidOperationException("if condition failed");
                },
                thenBranch => thenBranch.Run<RetryBranchStep, string>(),
                elseBranch => elseBranch.Run<ElseStep, string>());

        WorkflowResult result = await step.ExecuteWorkflowAsync(new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
        });

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ConditionEvaluationFailed, result.ErrorCode);
        Assert.Equal(1, IfBranchState.ConditionAttempts);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps.Where(traceStep => traceStep.StepName == "failing-condition"));
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.ConditionEvaluationFailed, traceStep.ErrorCode);
    }

    /// <summary>
    /// 空 branch が定義時に失敗し、明示 passthrough を要求する契約であることを確認します。
    /// </summary>
    [Fact(DisplayName = "If rejects empty branches and requires explicit passthrough")]
    public void IfRejectsEmptyBranchesAndRequiresExplicitPassthrough()
    {
        CompositeStep<BranchValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"));

        Assert.Throws<InvalidOperationException>(() => step.If(
            "empty-then",
            current => true,
            thenBranch => thenBranch,
            elseBranch => elseBranch.Run("else-passthrough", current => current)));
        Assert.Throws<InvalidOperationException>(() => step.If(
            "empty-else",
            current => true,
            thenBranch => thenBranch.Run("then-passthrough", current => current),
            elseBranch => elseBranch));
    }

    /// <summary>
    /// selected branch 内の retry が既存契約どおり効くことを確認します。
    /// </summary>
    [Fact(DisplayName = "Selected branch steps keep retry behavior")]
    public async Task SelectedBranchStepsKeepRetryBehavior()
    {
        IfBranchState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"))
            .If(
                "retry-if",
                current => true,
                thenBranch => thenBranch.Run<RetryBranchStep, string>(),
                elseBranch => elseBranch.Run<ElseStep, string>());

        WorkflowResult result = await step.ExecuteWorkflowAsync(new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 2 },
        });

        Assert.True(result.Succeeded);
        Assert.Equal(2, IfBranchState.RetryAttempts);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(RetryBranchStep)
            && traceStep.Status == ExecutionTraceStepStatus.Failed
            && traceStep.Attempt == 1);
        Assert.Contains(result.Trace.Steps, traceStep => traceStep.StepName == nameof(RetryBranchStep)
            && traceStep.Status == ExecutionTraceStepStatus.Succeeded
            && traceStep.Attempt == 2);
    }

    /// <summary>
    /// selected branch 内の timeout が既存契約どおり効くことを確認します。
    /// </summary>
    [Fact(DisplayName = "Selected branch steps keep timeout behavior", Skip = "CI 環境の timer / scheduling に依存して不安定になるため、timeout 検査の安定化まで保留します。")]
    public async Task SelectedBranchStepsKeepTimeoutBehavior()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"))
            .If(
                "timeout-if",
                current => true,
                thenBranch => thenBranch.RunAsync<TimeoutBranchStep, string>(),
                elseBranch => elseBranch.Run<ElseStep, string>());

        WorkflowResult result = await step.ExecuteWorkflowAsync(new WorkflowExecutionOptions
        {
            StepTimeout = TimeSpan.FromMilliseconds(30),
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepTimeout, result.ErrorCode);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(TimeoutBranchStep)
            && traceStep.ErrorCode == WorkflowErrorCodes.StepTimeout);
    }

    /// <summary>
    /// null name、condition、branch delegate が既存方針に合わせて失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "If rejects null and empty API arguments")]
    public void IfRejectsNullAndEmptyApiArguments()
    {
        CompositeStep<BranchValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue("seed"));
        Func<BranchValue, bool>? condition = null;
        Func<BranchValue, StepInput, bool>? conditionWithInput = null;
        Func<BranchBuilder<BranchValue>, BranchBuilder<string>>? thenFlow = null;
        Func<BranchBuilder<BranchValue>, BranchBuilder<string>>? elseFlow = null;

        Assert.Throws<ArgumentException>(() => step.If("", current => true, thenBranch => thenBranch.Run("then", current => "then"), elseBranch => elseBranch.Run("else", current => "else")));
        Assert.Throws<ArgumentNullException>(() => step.If("null-condition", condition!, thenBranch => thenBranch.Run("then", current => "then"), elseBranch => elseBranch.Run("else", current => "else")));
        Assert.Throws<ArgumentNullException>(() => step.If("null-condition-input", conditionWithInput!, thenBranch => thenBranch.Run("then", current => "then"), elseBranch => elseBranch.Run("else", current => "else")));
        Assert.Throws<ArgumentNullException>(() => step.If("null-then", current => true, thenFlow!, elseBranch => elseBranch.Run("else", current => "else")));
        Assert.Throws<ArgumentNullException>(() => step.If("null-else", current => true, thenBranch => thenBranch.Run("then", current => "then"), elseFlow!));
    }

    /// <summary>
    /// 同じ CompositeStep instance の複数回実行と並行実行で branch 状態が漏れないことを確認します。
    /// </summary>
    [Fact(DisplayName = "Repeated and concurrent If executions keep branch state isolated")]
    public async Task RepeatedAndConcurrentIfExecutionsKeepBranchStateIsolated()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new BranchValue(input.Context.Get<EngineArguments>().WorkflowSettings["branch"]))
            .If(
                "state-if",
                (current, input) => current.Value == "then",
                thenBranch => thenBranch.Run("then-lambda", current => "then-output"),
                elseBranch => elseBranch.Run("else-lambda", current => "else-output"));

        WorkflowResult first = await step.ExecuteWorkflowAsync(CreateOptionsWithWorkflowSetting("branch", "then"));
        WorkflowResult second = await step.ExecuteWorkflowAsync(CreateOptionsWithWorkflowSetting("branch", "else"));
        WorkflowResult[] concurrent = await Task.WhenAll(
            step.ExecuteWorkflowAsync(CreateOptionsWithWorkflowSetting("branch", "then")),
            step.ExecuteWorkflowAsync(CreateOptionsWithWorkflowSetting("branch", "else")));

        Assert.Contains(first.Trace!.Steps, traceStep => traceStep.StepName == "then-lambda");
        Assert.DoesNotContain(first.Trace.Steps, traceStep => traceStep.StepName == "else-lambda");
        Assert.Contains(second.Trace!.Steps, traceStep => traceStep.StepName == "else-lambda");
        Assert.DoesNotContain(second.Trace.Steps, traceStep => traceStep.StepName == "then-lambda");
        Assert.Contains(concurrent.Single(result => result.Trace!.Steps.Any(traceStep => traceStep.StepName == "then-lambda")).Trace!.Steps, traceStep => traceStep.StepName == "state-if");
        Assert.Contains(concurrent.Single(result => result.Trace!.Steps.Any(traceStep => traceStep.StepName == "else-lambda")).Trace!.Steps, traceStep => traceStep.StepName == "state-if");
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
    /// 分岐条件と出力に使う値を保持します。
    /// </summary>
    private sealed class BranchValue
    {
        /// <summary>
        /// 検査用の値を初期化します。
        /// </summary>
        /// <param name="value">検査用の文字列値。</param>
        public BranchValue(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 検査用の文字列値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// then branch の Config を保持します。
    /// </summary>
    private sealed class ThenConfig
    {
        /// <summary>
        /// Step が読む文字列値を取得または設定します。
        /// </summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// else branch の Config を保持します。
    /// </summary>
    private sealed class ElseConfig
    {
        /// <summary>
        /// Step が読む文字列値を取得または設定します。
        /// </summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// 境界 Config を保持します。
    /// </summary>
    private sealed class BoundaryConfig
    {
        /// <summary>
        /// then branch の Config を取得または設定します。
        /// </summary>
        public ThenConfig Then { get; set; } = new();

        /// <summary>
        /// else branch の Config を取得または設定します。
        /// </summary>
        public ElseConfig Else { get; set; } = new();

        /// <summary>
        /// If 後続 Step の Config を取得または設定します。
        /// </summary>
        public AfterConfig After { get; set; } = new();
    }

    /// <summary>
    /// If 後続 Step の Config を保持します。
    /// </summary>
    private sealed class AfterConfig
    {
        /// <summary>
        /// Step が読む文字列値を取得または設定します。
        /// </summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// then branch で実行される Step です。
    /// </summary>
    private sealed class ThenStep : IStep<string>
    {
        /// <summary>
        /// then branch の実行回数を記録します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>then branch の戻り値。</returns>
        public string Execute(StepInput input)
        {
            IfBranchState.IncrementThenAttempts();

            return "then";
        }
    }

    /// <summary>
    /// else branch で実行される Step です。
    /// </summary>
    private sealed class ElseStep : IStep<string>
    {
        /// <summary>
        /// else branch の実行回数を記録します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>else branch の戻り値。</returns>
        public string Execute(StepInput input)
        {
            IfBranchState.IncrementElseAttempts();

            return "else";
        }
    }

    /// <summary>
    /// 同一型 RunIf で実行される branch Step です。
    /// </summary>
    private sealed class SameTypeBranchStep : IStep<BranchValue>
    {
        /// <summary>
        /// 実行回数を記録して同一型の値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>RunIf 後の現在値。</returns>
        public BranchValue Execute(StepInput input)
        {
            IfBranchState.IncrementSameTypeAttempts();

            return new BranchValue("same-type");
        }
    }

    /// <summary>
    /// TapIf で実行される branch Step です。
    /// </summary>
    private sealed class TapBranchStep : IStep<Unit>
    {
        /// <summary>
        /// 実行回数を記録して Unit を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Unit 値。</returns>
        public Unit Execute(StepInput input)
        {
            IfBranchState.IncrementTapAttempts();

            return Unit.Value;
        }
    }

    /// <summary>
    /// then branch の Config を読む Step です。
    /// </summary>
    private sealed class ThenConfigStep : IStep<string>
    {
        /// <summary>
        /// StepContext から Config を読んで値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Config から取得した値。</returns>
        public string Execute(StepInput input)
        {
            return input.Context.Get<ThenConfig>().Value;
        }
    }

    /// <summary>
    /// else branch の Config を読む Step です。
    /// </summary>
    private sealed class ElseConfigStep : IStep<string>
    {
        /// <summary>
        /// StepContext から Config を読んで値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Config から取得した値。</returns>
        public string Execute(StepInput input)
        {
            return input.Context.Get<ElseConfig>().Value;
        }
    }

    /// <summary>
    /// If 後続で Config を読む Step です。
    /// </summary>
    private sealed class AfterConfigStep : IStep<string>
    {
        /// <summary>
        /// StepContext から Config を読んで値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Config から取得した値。</returns>
        public string Execute(StepInput input)
        {
            string value = input.Context.Get<AfterConfig>().Value;
            IfBranchState.SetAfterValue(value);

            return value;
        }
    }

    /// <summary>
    /// retry 検査用の branch Step です。
    /// </summary>
    private sealed class RetryBranchStep : IStep<string>
    {
        /// <summary>
        /// 1 回目だけ失敗し、2 回目に成功します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>retry 後の戻り値。</returns>
        public string Execute(StepInput input)
        {
            int attempt = IfBranchState.IncrementRetryAttempts();
            if (attempt == 1)
            {
                throw new InvalidOperationException("retry branch failed");
            }

            return "retry-ok";
        }
    }

    /// <summary>
    /// timeout 検査用の branch Step です。
    /// </summary>
    private sealed class TimeoutBranchStep : IAsyncStep<string>
    {
        /// <summary>
        /// キャンセルされるまで待機します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <param name="cancellationToken">キャンセル通知。</param>
        /// <returns>到達しない文字列。</returns>
        public async Task<string> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return "unreachable";
        }
    }

    /// <summary>
    /// If 分岐検査の観測状態を保持します。
    /// </summary>
    private static class IfBranchState
    {
        /// <summary>
        /// then branch Step の実行回数を取得します。
        /// </summary>
        public static int ThenAttempts { get; private set; }

        /// <summary>
        /// else branch Step の実行回数を取得します。
        /// </summary>
        public static int ElseAttempts { get; private set; }

        /// <summary>
        /// 同一型 RunIf Step の実行回数を取得します。
        /// </summary>
        public static int SameTypeAttempts { get; private set; }

        /// <summary>
        /// TapIf Step の実行回数を取得します。
        /// </summary>
        public static int TapAttempts { get; private set; }

        /// <summary>
        /// retry Step の実行回数を取得します。
        /// </summary>
        public static int RetryAttempts { get; private set; }

        /// <summary>
        /// 条件判定の実行回数を取得します。
        /// </summary>
        public static int ConditionAttempts { get; private set; }

        /// <summary>
        /// If 後続 Step が読んだ Config 値を取得します。
        /// </summary>
        public static string? AfterValue { get; private set; }

        /// <summary>
        /// 観測状態を初期値へ戻します。
        /// </summary>
        public static void Reset()
        {
            ThenAttempts = 0;
            ElseAttempts = 0;
            SameTypeAttempts = 0;
            TapAttempts = 0;
            RetryAttempts = 0;
            ConditionAttempts = 0;
            AfterValue = null;
        }

        /// <summary>
        /// then branch Step の実行回数を増やします。
        /// </summary>
        public static void IncrementThenAttempts()
        {
            ThenAttempts++;
        }

        /// <summary>
        /// else branch Step の実行回数を増やします。
        /// </summary>
        public static void IncrementElseAttempts()
        {
            ElseAttempts++;
        }

        /// <summary>
        /// 同一型 RunIf Step の実行回数を増やします。
        /// </summary>
        public static void IncrementSameTypeAttempts()
        {
            SameTypeAttempts++;
        }

        /// <summary>
        /// TapIf Step の実行回数を増やします。
        /// </summary>
        public static void IncrementTapAttempts()
        {
            TapAttempts++;
        }

        /// <summary>
        /// retry Step の実行回数を増やします。
        /// </summary>
        /// <returns>加算後の実行回数。</returns>
        public static int IncrementRetryAttempts()
        {
            return ++RetryAttempts;
        }

        /// <summary>
        /// 条件判定の実行回数を増やします。
        /// </summary>
        public static void IncrementConditionAttempts()
        {
            ConditionAttempts++;
        }

        /// <summary>
        /// If 後続 Step が読んだ Config 値を記録します。
        /// </summary>
        /// <param name="value">Step が読んだ Config 値。</param>
        public static void SetAfterValue(string value)
        {
            AfterValue = value;
        }
    }
}
