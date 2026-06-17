using System.Reflection;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// Switch と SwitchCaseBuilder の分岐実行契約を検査します。
/// </summary>
public sealed class SwitchBranchContractTests
{
    /// <summary>
    /// 一致した case branch だけを実行し、default と未選択 case を trace に出さないことを確認します。
    /// </summary>
    [Fact(DisplayName = "Switch executes only matching case branch and hides unselected branches from trace")]
    public async Task SwitchExecutesOnlyMatchingCaseBranchAndHidesUnselectedBranchesFromTrace()
    {
        SwitchBranchState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new SwitchValue("beta"))
            .Switch<SwitchKind, string>(
                "choose-case",
                current => current.Kind,
                cases => cases
                    .Case(SwitchKind.Alpha, branch => branch.Run<AlphaStep, string>())
                    .Case(SwitchKind.Beta, branch => branch.Run<BetaStep, string>())
                    .Default(branch => branch.Run<DefaultStep, string>()));

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, SwitchBranchState.AlphaAttempts);
        Assert.Equal(1, SwitchBranchState.BetaAttempts);
        Assert.Equal(0, SwitchBranchState.DefaultAttempts);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == "choose-case");
        Assert.Contains(result.Trace.Steps, traceStep => traceStep.StepName == nameof(BetaStep));
        Assert.DoesNotContain(result.Trace.Steps, traceStep => traceStep.StepName == nameof(AlphaStep));
        Assert.DoesNotContain(result.Trace.Steps, traceStep => traceStep.StepName == nameof(DefaultStep));
    }

    /// <summary>
    /// 一致する case がない場合に default branch だけを実行することを確認します。
    /// </summary>
    [Fact(DisplayName = "Switch executes default branch when no case matches")]
    public async Task SwitchExecutesDefaultBranchWhenNoCaseMatches()
    {
        SwitchBranchState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new SwitchValue("gamma"))
            .Switch<SwitchKind, string>(
                "choose-default",
                current => current.Kind,
                cases => cases
                    .Case(SwitchKind.Alpha, branch => branch.Run<AlphaStep, string>())
                    .Default(branch => branch.Run<DefaultStep, string>()));

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, SwitchBranchState.AlphaAttempts);
        Assert.Equal(0, SwitchBranchState.BetaAttempts);
        Assert.Equal(1, SwitchBranchState.DefaultAttempts);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(DefaultStep));
        Assert.DoesNotContain(result.Trace.Steps, traceStep => traceStep.StepName == nameof(AlphaStep));
    }

    /// <summary>
    /// 重複 case が定義時に失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Switch rejects duplicate cases at definition time")]
    public void SwitchRejectsDuplicateCasesAtDefinitionTime()
    {
        CompositeStep<SwitchValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new SwitchValue("alpha"));

        Assert.Throws<InvalidOperationException>(() => step.Switch<SwitchKind, string>(
            "duplicate-case",
            current => current.Kind,
            cases => cases
                .Case(SwitchKind.Alpha, branch => branch.Run<AlphaStep, string>())
                .Case(SwitchKind.Alpha, branch => branch.Run<BetaStep, string>())
                .Default(branch => branch.Run<DefaultStep, string>())));
    }

    /// <summary>
    /// default 未定義が定義時に失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Switch requires default branch at definition time")]
    public void SwitchRequiresDefaultBranchAtDefinitionTime()
    {
        CompositeStep<SwitchValue> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new SwitchValue("alpha"));

        Assert.Throws<InvalidOperationException>(() => step.Switch<SwitchKind, string>(
            "missing-default",
            current => current.Kind,
            cases => cases.Case(SwitchKind.Alpha, branch => branch.Run<AlphaStep, string>())));
    }

    /// <summary>
    /// branch 内 Config が case/default すべて実行前読み込み対象になり、選択 branch の実行直前に登録されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Switch branch config metadata covers all branches and selected config is registered before execution")]
    public async Task SwitchBranchConfigMetadataCoversAllBranchesAndSelectedConfigIsRegisteredBeforeExecution()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new SwitchValue("beta"))
            .Switch<SwitchKind, string>(
                "config-switch",
                current => current.Kind,
                cases => cases
                    .Case(SwitchKind.Alpha, branch => branch.Run<AlphaConfigStep, string>().WithConfig<AlphaConfig>("Alpha"))
                    .Case(SwitchKind.Beta, branch => branch.Run<BetaConfigStep, string>().WithConfig<BetaConfig>("Beta"))
                    .Default(branch => branch.Run<DefaultConfigStep, string>().WithConfig<DefaultConfig>("Default")))
            .Run<AfterConfigStep, string>()
            .WithConfig<AfterConfig>("After")
            .WithConfig<BoundaryConfig>();
        WorkflowExecutionOptions options = CreateOptionsWithStepConfigs(
            (2, typeof(AlphaConfig), new AlphaConfig { Value = "alpha-config" }),
            (3, typeof(BetaConfig), new BetaConfig { Value = "beta-config" }),
            (4, typeof(DefaultConfig), new DefaultConfig { Value = "default-config" }),
            (5, typeof(AfterConfig), new AfterConfig { Value = "after-config" }));

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.True(result.Succeeded);
        Assert.Equal(["Alpha", "Beta", "Default", "After"], step.StepConfigRegistrations.Select(registration => registration.SectionPath).ToArray());
        Assert.Equal([2, 3, 4, 5], step.StepConfigRegistrations.Select(GetStepConfigIndex).ToArray());
        Assert.Equal("beta-config", SwitchBranchState.BetaConfigValue);
        Assert.Equal("after-config", SwitchBranchState.AfterConfigValue);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(BetaConfigStep));
        Assert.DoesNotContain(result.Trace.Steps, traceStep => traceStep.StepName == nameof(AlphaConfigStep));
        Assert.DoesNotContain(result.Trace.Steps, traceStep => traceStep.StepName == nameof(DefaultConfigStep));
    }

    /// <summary>
    /// selector 例外が SWITCH_SELECTOR_FAILED になり retry されないことを確認します。
    /// </summary>
    [Fact(DisplayName = "Switch selector exception returns switch selector failed without retry")]
    public async Task SwitchSelectorExceptionReturnsSwitchSelectorFailedWithoutRetry()
    {
        SwitchBranchState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new SwitchValue("alpha"))
            .Switch<SwitchKind, string>(
                "failing-selector",
                current =>
                {
                    SwitchBranchState.IncrementSelectorAttempts();
                    throw new InvalidOperationException("switch selector failed");
                },
                cases => cases
                    .Case(SwitchKind.Alpha, branch => branch.Run<AlphaStep, string>())
                    .Default(branch => branch.Run<DefaultStep, string>()));

        WorkflowResult result = await step.ExecuteWorkflowAsync(new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 3 },
        });

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.SwitchSelectorFailed, result.ErrorCode);
        Assert.Equal(1, SwitchBranchState.SelectorAttempts);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps.Where(traceStep => traceStep.StepName == "failing-selector"));
        Assert.Equal(ExecutionTraceStepStatus.Failed, traceStep.Status);
        Assert.Equal(WorkflowErrorCodes.SwitchSelectorFailed, traceStep.ErrorCode);
    }

    /// <summary>
    /// branch 内で入れ子 Switch と値 API が使えることを確認します。
    /// </summary>
    [Fact(DisplayName = "BranchBuilder supports nested switch and value APIs")]
    public async Task BranchBuilderSupportsNestedSwitchAndValueApis()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run("seed", input => new SwitchValue("alpha"))
            .Switch<SwitchKind, string>(
                "outer-switch",
                (current, input) => current.Kind,
                cases => cases
                    .Case(SwitchKind.Alpha, branch => branch
                        .Run("alpha-lambda", current => new SwitchValue("beta"))
                            .StoreAs()
                            .Produce("switch-name", current => current.Raw)
                        .Switch<SwitchKind, string>(
                            "nested-switch",
                            current => current.Kind,
                            nestedCases => nestedCases
                                .Case(SwitchKind.Beta, nestedBranch => nestedBranch.Run("nested-beta", current => current.Raw + ":" + current.Kind))
                                .Default(nestedBranch => nestedBranch.Run("nested-default", current => current.Raw)))
                            .Discard())
                    .Default(branch => branch.Run("outer-default", current => "default")));

        WorkflowResult result = await step.ExecuteWorkflowAsync();
        string output = step.Execute(new StepInput());

        Assert.True(result.Succeeded);
        Assert.Equal("beta:Beta", output);
        Assert.Contains(result.Trace!.Steps, traceStep => traceStep.StepName == "nested-switch");
        Assert.Contains(result.Trace.Steps, traceStep => traceStep.StepName == "nested-beta");
        Assert.DoesNotContain(result.Trace.Steps, traceStep => traceStep.StepName == "nested-default");
        Assert.DoesNotContain(result.Trace.Steps, traceStep => traceStep.StepName == "outer-default");
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
    /// Switch の selector と分岐で使う値を保持します。
    /// </summary>
    private sealed class SwitchValue
    {
        /// <summary>
        /// 検査用の値を初期化します。
        /// </summary>
        /// <param name="raw">検査用の文字列値。</param>
        public SwitchValue(string raw)
        {
            Raw = raw;
        }

        /// <summary>
        /// 検査用の文字列値を取得します。
        /// </summary>
        public string Raw { get; }

        /// <summary>
        /// 文字列値から分岐キーを取得します。
        /// </summary>
        public SwitchKind Kind => Raw switch
        {
            "alpha" => SwitchKind.Alpha,
            "beta" => SwitchKind.Beta,
            _ => SwitchKind.Unknown,
        };
    }

    /// <summary>
    /// Switch の分岐キーを表します。
    /// </summary>
    private enum SwitchKind
    {
        /// <summary>
        /// 一致なしを表します。
        /// </summary>
        Unknown,

        /// <summary>
        /// Alpha case を表します。
        /// </summary>
        Alpha,

        /// <summary>
        /// Beta case を表します。
        /// </summary>
        Beta,
    }

    /// <summary>
    /// alpha branch の Config を保持します。
    /// </summary>
    private sealed class AlphaConfig
    {
        /// <summary>
        /// Step が読む文字列値を取得または設定します。
        /// </summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// beta branch の Config を保持します。
    /// </summary>
    private sealed class BetaConfig
    {
        /// <summary>
        /// Step が読む文字列値を取得または設定します。
        /// </summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// default branch の Config を保持します。
    /// </summary>
    private sealed class DefaultConfig
    {
        /// <summary>
        /// Step が読む文字列値を取得または設定します。
        /// </summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// Switch 後続 Step の Config を保持します。
    /// </summary>
    private sealed class AfterConfig
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
        /// alpha branch の Config を取得または設定します。
        /// </summary>
        public AlphaConfig Alpha { get; set; } = new();

        /// <summary>
        /// beta branch の Config を取得または設定します。
        /// </summary>
        public BetaConfig Beta { get; set; } = new();

        /// <summary>
        /// default branch の Config を取得または設定します。
        /// </summary>
        public DefaultConfig Default { get; set; } = new();

        /// <summary>
        /// Switch 後続 Step の Config を取得または設定します。
        /// </summary>
        public AfterConfig After { get; set; } = new();
    }

    /// <summary>
    /// alpha branch で実行される Step です。
    /// </summary>
    private sealed class AlphaStep : IStep<string>
    {
        /// <summary>
        /// alpha branch の実行回数を記録します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>alpha branch の戻り値。</returns>
        public string Execute(StepInput input)
        {
            SwitchBranchState.IncrementAlphaAttempts();

            return "alpha";
        }
    }

    /// <summary>
    /// beta branch で実行される Step です。
    /// </summary>
    private sealed class BetaStep : IStep<string>
    {
        /// <summary>
        /// beta branch の実行回数を記録します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>beta branch の戻り値。</returns>
        public string Execute(StepInput input)
        {
            SwitchBranchState.IncrementBetaAttempts();

            return "beta";
        }
    }

    /// <summary>
    /// default branch で実行される Step です。
    /// </summary>
    private sealed class DefaultStep : IStep<string>
    {
        /// <summary>
        /// default branch の実行回数を記録します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>default branch の戻り値。</returns>
        public string Execute(StepInput input)
        {
            SwitchBranchState.IncrementDefaultAttempts();

            return "default";
        }
    }

    /// <summary>
    /// alpha branch の Config を読む Step です。
    /// </summary>
    private sealed class AlphaConfigStep : IStep<string>
    {
        /// <summary>
        /// StepContext から Config を読んで値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Config から取得した値。</returns>
        public string Execute(StepInput input)
        {
            return input.Context.Get<AlphaConfig>().Value;
        }
    }

    /// <summary>
    /// beta branch の Config を読む Step です。
    /// </summary>
    private sealed class BetaConfigStep : IStep<string>
    {
        /// <summary>
        /// StepContext から Config を読んで値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Config から取得した値。</returns>
        public string Execute(StepInput input)
        {
            string value = input.Context.Get<BetaConfig>().Value;
            SwitchBranchState.SetBetaConfigValue(value);

            return value;
        }
    }

    /// <summary>
    /// default branch の Config を読む Step です。
    /// </summary>
    private sealed class DefaultConfigStep : IStep<string>
    {
        /// <summary>
        /// StepContext から Config を読んで値を返します。
        /// </summary>
        /// <param name="input">Step 入力。</param>
        /// <returns>Config から取得した値。</returns>
        public string Execute(StepInput input)
        {
            return input.Context.Get<DefaultConfig>().Value;
        }
    }

    /// <summary>
    /// Switch 後続で Config を読む Step です。
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
            SwitchBranchState.SetAfterConfigValue(value);

            return value;
        }
    }

    /// <summary>
    /// Switch 分岐検査の観測状態を保持します。
    /// </summary>
    private static class SwitchBranchState
    {
        /// <summary>
        /// alpha branch Step の実行回数を取得します。
        /// </summary>
        public static int AlphaAttempts { get; private set; }

        /// <summary>
        /// beta branch Step の実行回数を取得します。
        /// </summary>
        public static int BetaAttempts { get; private set; }

        /// <summary>
        /// default branch Step の実行回数を取得します。
        /// </summary>
        public static int DefaultAttempts { get; private set; }

        /// <summary>
        /// selector の実行回数を取得します。
        /// </summary>
        public static int SelectorAttempts { get; private set; }

        /// <summary>
        /// beta branch Step が読んだ Config 値を取得します。
        /// </summary>
        public static string? BetaConfigValue { get; private set; }

        /// <summary>
        /// Switch 後続 Step が読んだ Config 値を取得します。
        /// </summary>
        public static string? AfterConfigValue { get; private set; }

        /// <summary>
        /// 観測状態を初期値へ戻します。
        /// </summary>
        public static void Reset()
        {
            AlphaAttempts = 0;
            BetaAttempts = 0;
            DefaultAttempts = 0;
            SelectorAttempts = 0;
            BetaConfigValue = null;
            AfterConfigValue = null;
        }

        /// <summary>
        /// alpha branch Step の実行回数を増やします。
        /// </summary>
        public static void IncrementAlphaAttempts()
        {
            AlphaAttempts++;
        }

        /// <summary>
        /// beta branch Step の実行回数を増やします。
        /// </summary>
        public static void IncrementBetaAttempts()
        {
            BetaAttempts++;
        }

        /// <summary>
        /// default branch Step の実行回数を増やします。
        /// </summary>
        public static void IncrementDefaultAttempts()
        {
            DefaultAttempts++;
        }

        /// <summary>
        /// selector の実行回数を増やします。
        /// </summary>
        public static void IncrementSelectorAttempts()
        {
            SelectorAttempts++;
        }

        /// <summary>
        /// beta branch Step が読んだ Config 値を記録します。
        /// </summary>
        /// <param name="value">Step が読んだ Config 値。</param>
        public static void SetBetaConfigValue(string value)
        {
            BetaConfigValue = value;
        }

        /// <summary>
        /// Switch 後続 Step が読んだ Config 値を記録します。
        /// </summary>
        /// <param name="value">Step が読んだ Config 値。</param>
        public static void SetAfterConfigValue(string value)
        {
            AfterConfigValue = value;
        }
    }
}
