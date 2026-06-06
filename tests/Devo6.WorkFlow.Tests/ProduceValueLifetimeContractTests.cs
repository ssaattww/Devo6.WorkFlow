using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// Produce で登録した値の寿命と有効範囲を利用者目線で固定します。
/// </summary>
public sealed class ProduceValueLifetimeContractTests
{
    /// <summary>
    /// 型付き Produce の値が複数の後続 Step から読めることを確認します。
    /// </summary>
    [Fact(DisplayName = "Produced typed value is visible to all following steps")]
    public void ProducedTypedValueIsVisibleToAllFollowingSteps()
    {
        LifetimeContractState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<ProduceSharedInputStep, SharedInput>()
                .Produce<SharedInput>(output => output)
            .Run<ReadSharedInputInSecondStep, string>()
                .Discard()
            .Run<ReadSharedInputInThirdStep, string>()
                .Discard();

        string result = step.Execute(new StepInput());

        Assert.Equal("third:shared", result);
        Assert.Equal(["second:shared", "third:shared"], LifetimeContractState.Snapshot());
    }

    /// <summary>
    /// 同じ CLR 型の名前付き値が複数 Step にまたがって累積することを確認します。
    /// </summary>
    [Fact(DisplayName = "Named string values from different steps remain visible together")]
    public void NamedStringValuesFromDifferentStepsRemainVisibleTogether()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<ProduceTitleStep, string>()
                .Produce<string>("title", output => output)
            .Run<ProduceBodyStep, string>()
                .Produce<string>("body", output => output)
            .Run<ReadTitleAndBodyStep, string>()
                .Discard();

        string result = step.Execute(new StepInput());

        Assert.Equal("title:body", result);
    }

    /// <summary>
    /// 型キーと名前付きキーが同じ CLR 型でも別キーとして共存することを確認します。
    /// </summary>
    [Fact(DisplayName = "Typed string and named string coexist as separate keys")]
    public void TypedStringAndNamedStringCoexistAsSeparateKeys()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<ProduceTypedStringStep, string>()
                .Produce<string>(output => output)
                .Produce<string>("title", output => $"{output}-named")
            .Run<ReadTypedAndNamedStringStep, string>()
                .Discard();

        string result = step.Execute(new StepInput());

        Assert.Equal("typed|typed-named", result);
    }

    /// <summary>
    /// 同じ型キーの再登録が post-processing で失敗し、後続 Step が開始しないことを確認します。
    /// </summary>
    [Fact(DisplayName = "Duplicate typed Produce fails after second step and stops following step")]
    public async Task DuplicateTypedProduceFailsAfterSecondStepAndStopsFollowingStep()
    {
        LifetimeContractState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<ProduceSameInputFirstStep, SameInput>()
                .Produce<SameInput>(output => output)
            .Run<ProduceSameInputSecondStep, SameInput>()
                .Produce<SameInput>(output => output)
            .Run<ShouldNotStartStep, string>()
                .Discard();

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        Assert.Contains("already registered", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(["same-first", "same-second"], LifetimeContractState.Snapshot());
        Assert.DoesNotContain(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(ShouldNotStartStep));
    }

    /// <summary>
    /// 同じ型と名前の再登録が失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Duplicate named Produce fails for same type and name")]
    public async Task DuplicateNamedProduceFailsForSameTypeAndName()
    {
        LifetimeContractState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<ProduceFirstNamedStringStep, string>()
                .Produce<string>("same", output => output)
            .Run<ProduceSecondNamedStringStep, string>()
                .Produce<string>("same", output => output)
            .Run<ShouldNotStartStep, string>()
                .Discard();

        WorkflowResult result = await step.ExecuteWorkflowAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepExecutionFailed, result.ErrorCode);
        Assert.Contains("already registered", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(["named-first", "named-second"], LifetimeContractState.Snapshot());
        Assert.DoesNotContain(result.Trace!.Steps, traceStep => traceStep.StepName == nameof(ShouldNotStartStep));
    }

    /// <summary>
    /// Discard が現在 Step の戻り値を登録せず、既存値は削除しないことを確認します。
    /// </summary>
    [Fact(DisplayName = "Discard skips current output without removing existing values")]
    public void DiscardSkipsCurrentOutputWithoutRemovingExistingValues()
    {
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<ProduceSharedInputStep, SharedInput>()
                .Produce<SharedInput>(output => output)
            .Run<DiscardedOutputStep, DiscardedOutput>()
                .Discard()
            .Run<ReadSharedAfterDiscardStep, string>()
                .Discard();

        string result = step.Execute(new StepInput());

        Assert.Equal("shared:discarded-output-missing", result);
    }

    /// <summary>
    /// retry では失敗試行の値が残らず、成功試行の値だけが後続 Step から読めることを確認します。
    /// </summary>
    [Fact(DisplayName = "Retry exposes only the successful attempt value to following steps")]
    public async Task RetryExposesOnlyTheSuccessfulAttemptValueToFollowingSteps()
    {
        LifetimeContractState.Reset();
        CompositeStep<string> step = CompositeStep
            .Define("Main")
            .Run<SecondAttemptProducesStep, AttemptOutput>()
                .Produce<AttemptInput>(output => new AttemptInput(output.Value))
            .Run<ReadAttemptInputStep, string>()
                .Discard();
        var options = new WorkflowExecutionOptions
        {
            Retry = new RetryOptions { MaxAttempts = 2 },
        };

        WorkflowResult result = await step.ExecuteWorkflowAsync(options);

        Assert.True(result.Succeeded);
        Assert.Equal(["attempt:1", "attempt:2", "read:success-attempt"], LifetimeContractState.Snapshot());
        ExecutionTraceStep[] attemptTrace = result.Trace!.Steps
            .Where(traceStep => traceStep.StepName == nameof(SecondAttemptProducesStep))
            .ToArray();
        Assert.Equal(
            [
                (ExecutionTraceStepStatus.Failed, WorkflowErrorCodes.StepExecutionFailed, 1),
                (ExecutionTraceStepStatus.Succeeded, null, 2),
            ],
            attemptTrace.Select(traceStep => (traceStep.Status, traceStep.ErrorCode, traceStep.Attempt)).ToArray());
    }

    /// <summary>
    /// 登録前の Step から後続 Produce 値を読めないことを確認します。
    /// </summary>
    [Fact(DisplayName = "Previous step cannot read a later produced value before registration")]
    public void PreviousStepCannotReadLaterProducedValueBeforeRegistration()
    {
        LifetimeContractState.Reset();
        CompositeStep<FutureInput> step = CompositeStep
            .Define("Main")
            .Run<TryReadFutureInputBeforeProduceStep, string>()
                .Discard()
            .Run<ProduceFutureInputStep, FutureInput>()
                .Produce<FutureInput>(output => output);

        FutureInput result = step.Execute(new StepInput());

        Assert.Equal("future", result.Value);
        Assert.Equal(["before:missing"], LifetimeContractState.Snapshot());
    }

    /// <summary>
    /// 複数 Step から共有される入力値を表します。
    /// </summary>
    private sealed class SharedInput
    {
        /// <summary>
        /// 共有入力値を初期化します。
        /// </summary>
        public SharedInput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 後続 Step が読む文字列値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// 重複登録の検査に使う型付き入力値を表します。
    /// </summary>
    private sealed class SameInput
    {
        /// <summary>
        /// 重複登録対象の入力値を初期化します。
        /// </summary>
        public SameInput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 登録元 Step を識別する値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// Discard 対象の戻り値を表します。
    /// </summary>
    private sealed class DiscardedOutput
    {
        /// <summary>
        /// Discard 対象の戻り値を初期化します。
        /// </summary>
        public DiscardedOutput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 登録されないはずの値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// retry 対象 Step の戻り値を表します。
    /// </summary>
    private sealed class AttemptOutput
    {
        /// <summary>
        /// retry 対象 Step の戻り値を初期化します。
        /// </summary>
        public AttemptOutput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 成功試行で生成された値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// retry 成功後に後続 Step へ渡す入力値を表します。
    /// </summary>
    private sealed class AttemptInput
    {
        /// <summary>
        /// retry 成功後の入力値を初期化します。
        /// </summary>
        public AttemptInput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 後続 Step が読む成功試行の値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// 後続 Step が Produce する入力値を表します。
    /// </summary>
    private sealed class FutureInput
    {
        /// <summary>
        /// 後続 Step で登録される入力値を初期化します。
        /// </summary>
        public FutureInput(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 登録後に読める文字列値を取得します。
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// 共有入力を生成する Step です。
    /// </summary>
    private sealed class ProduceSharedInputStep : IStep<SharedInput>
    {
        /// <summary>
        /// 後続 Step に共有する入力値を返します。
        /// </summary>
        public SharedInput Execute(StepInput input)
        {
            return new SharedInput("shared");
        }
    }

    /// <summary>
    /// 2 番目の Step から共有入力を読む Step です。
    /// </summary>
    private sealed class ReadSharedInputInSecondStep : IStep<string>
    {
        /// <summary>
        /// 共有入力を読み、観測状態へ記録します。
        /// </summary>
        public string Execute(StepInput input)
        {
            string value = input.Get<SharedInput>().Value;
            LifetimeContractState.Add($"second:{value}");

            return $"second:{value}";
        }
    }

    /// <summary>
    /// 3 番目の Step から共有入力を読む Step です。
    /// </summary>
    private sealed class ReadSharedInputInThirdStep : IStep<string>
    {
        /// <summary>
        /// 共有入力を読み、観測状態へ記録します。
        /// </summary>
        public string Execute(StepInput input)
        {
            string value = input.Get<SharedInput>().Value;
            LifetimeContractState.Add($"third:{value}");

            return $"third:{value}";
        }
    }

    /// <summary>
    /// title として登録する文字列を返す Step です。
    /// </summary>
    private sealed class ProduceTitleStep : IStep<string>
    {
        /// <summary>
        /// title 用の文字列を返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            return "title";
        }
    }

    /// <summary>
    /// body として登録する文字列を返す Step です。
    /// </summary>
    private sealed class ProduceBodyStep : IStep<string>
    {
        /// <summary>
        /// body 用の文字列を返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            return "body";
        }
    }

    /// <summary>
    /// title と body の名前付き値を読む Step です。
    /// </summary>
    private sealed class ReadTitleAndBodyStep : IStep<string>
    {
        /// <summary>
        /// 2 つの名前付き文字列を結合して返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            return $"{input.Get<string>("title")}:{input.Get<string>("body")}";
        }
    }

    /// <summary>
    /// 型キーと名前付きキーに分ける文字列を返す Step です。
    /// </summary>
    private sealed class ProduceTypedStringStep : IStep<string>
    {
        /// <summary>
        /// 型キーにも名前付きキーにも登録する元値を返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            return "typed";
        }
    }

    /// <summary>
    /// 型付き文字列と名前付き文字列を読む Step です。
    /// </summary>
    private sealed class ReadTypedAndNamedStringStep : IStep<string>
    {
        /// <summary>
        /// 別キーとして登録された 2 つの文字列を結合して返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            return $"{input.Get<string>()}|{input.Get<string>("title")}";
        }
    }

    /// <summary>
    /// 同じ型キーへ最初に登録する値を返す Step です。
    /// </summary>
    private sealed class ProduceSameInputFirstStep : IStep<SameInput>
    {
        /// <summary>
        /// 最初の型付き入力値を返します。
        /// </summary>
        public SameInput Execute(StepInput input)
        {
            LifetimeContractState.Add("same-first");

            return new SameInput("first");
        }
    }

    /// <summary>
    /// 同じ型キーへ再登録する値を返す Step です。
    /// </summary>
    private sealed class ProduceSameInputSecondStep : IStep<SameInput>
    {
        /// <summary>
        /// 重複登録になる 2 番目の型付き入力値を返します。
        /// </summary>
        public SameInput Execute(StepInput input)
        {
            LifetimeContractState.Add("same-second");

            return new SameInput("second");
        }
    }

    /// <summary>
    /// 同じ名前付きキーへ最初に登録する文字列を返す Step です。
    /// </summary>
    private sealed class ProduceFirstNamedStringStep : IStep<string>
    {
        /// <summary>
        /// 最初の名前付き文字列を返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            LifetimeContractState.Add("named-first");

            return "first";
        }
    }

    /// <summary>
    /// 同じ名前付きキーへ再登録する文字列を返す Step です。
    /// </summary>
    private sealed class ProduceSecondNamedStringStep : IStep<string>
    {
        /// <summary>
        /// 重複登録になる 2 番目の名前付き文字列を返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            LifetimeContractState.Add("named-second");

            return "second";
        }
    }

    /// <summary>
    /// 失敗時に開始されてはならない Step です。
    /// </summary>
    private sealed class ShouldNotStartStep : IStep<string>
    {
        /// <summary>
        /// 開始された場合に観測状態へ記録します。
        /// </summary>
        public string Execute(StepInput input)
        {
            LifetimeContractState.Add("should-not-start");

            return "unexpected";
        }
    }

    /// <summary>
    /// Discard される戻り値を返す Step です。
    /// </summary>
    private sealed class DiscardedOutputStep : IStep<DiscardedOutput>
    {
        /// <summary>
        /// 後続 Step へ登録しない戻り値を返します。
        /// </summary>
        public DiscardedOutput Execute(StepInput input)
        {
            return new DiscardedOutput("discarded");
        }
    }

    /// <summary>
    /// Discard 後に既存値が残り、現在 Step の戻り値は未登録であることを読む Step です。
    /// </summary>
    private sealed class ReadSharedAfterDiscardStep : IStep<string>
    {
        /// <summary>
        /// 既存の共有入力と Discard 対象値の未登録状態を返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            string shared = input.Get<SharedInput>().Value;
            string discardedState = input.TryGet<DiscardedOutput>(out _)
                ? "discarded-output-present"
                : "discarded-output-missing";

            return $"{shared}:{discardedState}";
        }
    }

    /// <summary>
    /// 1 回目に失敗し、2 回目に Produce 対象値を返す Step です。
    /// </summary>
    private sealed class SecondAttemptProducesStep : IStep<AttemptOutput>
    {
        /// <summary>
        /// 失敗試行では例外を投げ、成功試行では値を返します。
        /// </summary>
        public AttemptOutput Execute(StepInput input)
        {
            int attempt = LifetimeContractState.IncrementAttempt(nameof(SecondAttemptProducesStep));
            LifetimeContractState.Add($"attempt:{attempt}");

            if (attempt == 1)
            {
                throw new InvalidOperationException("first attempt failed");
            }

            return new AttemptOutput("success-attempt");
        }
    }

    /// <summary>
    /// retry 成功後の入力値を読む Step です。
    /// </summary>
    private sealed class ReadAttemptInputStep : IStep<string>
    {
        /// <summary>
        /// 成功試行で登録された入力値を観測状態へ記録して返します。
        /// </summary>
        public string Execute(StepInput input)
        {
            string value = input.Get<AttemptInput>().Value;
            LifetimeContractState.Add($"read:{value}");

            return value;
        }
    }

    /// <summary>
    /// 後続 Produce 値を登録前に読めないことを確認する Step です。
    /// </summary>
    private sealed class TryReadFutureInputBeforeProduceStep : IStep<string>
    {
        /// <summary>
        /// 未登録の後続 Produce 値が見えない状態を観測状態へ記録します。
        /// </summary>
        public string Execute(StepInput input)
        {
            string state = input.TryGet<FutureInput>(out _)
                ? "present"
                : "missing";
            LifetimeContractState.Add($"before:{state}");

            return state;
        }
    }

    /// <summary>
    /// 後続 Produce 値を返す Step です。
    /// </summary>
    private sealed class ProduceFutureInputStep : IStep<FutureInput>
    {
        /// <summary>
        /// 登録前 Step からは読めない Produce 対象値を返します。
        /// </summary>
        public FutureInput Execute(StepInput input)
        {
            return new FutureInput("future");
        }
    }

    /// <summary>
    /// テスト用 Step の実行状態を記録します。
    /// </summary>
    private static class LifetimeContractState
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, int> Attempts = new();
        private static List<string> entries = new();

        /// <summary>
        /// テスト間で共有する観測状態を初期化します。
        /// </summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Attempts.Clear();
                entries = new List<string>();
            }
        }

        /// <summary>
        /// 指定 Step の試行回数を進め、現在の試行番号を返します。
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
        /// 現在までに記録された実行イベントを返します。
        /// </summary>
        public static string[] Snapshot()
        {
            lock (Gate)
            {
                return entries.ToArray();
            }
        }
    }
}
