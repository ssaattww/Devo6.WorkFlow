using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Cli;
using Devo6.WorkFlow.Engine;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Threading;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// 標準出力を差し替える階層ログ検査を直列実行します。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HierarchicalLoggingConsoleCollection
{
    /// <summary>階層ログ検査用 collection 名。</summary>
    public const string Name = "HierarchicalLoggingConsole";
}

/// <summary>
/// Step 名、CompositeStep、分岐、retry を含むログ階層契約を検証します。
/// </summary>
[Collection(HierarchicalLoggingConsoleCollection.Name)]
public sealed class HierarchicalLoggingContractTests
{
    private static readonly object ConsoleSync = new();

    /// <summary>
    /// Text ログが scope chain から実行パスと試行番号を表示することを検証します。
    /// </summary>
    [Fact(DisplayName = "Text ログは Entry Step Attempt を実行コンテキストとして表示する")]
    public void TextLogIncludesExecutionPathAndAttempt()
    {
        string output = CaptureConsole(() =>
        {
            using var provider = CreateProvider(EngineLoggingFormat.Text);
            ILogger logger = provider.CreateLogger("Test.Category");
            using IDisposable? entryScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["EntryName"] = "Main",
            });
            using IDisposable? stepScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["StepName"] = "LoadTextStep",
                ["Attempt"] = 2,
            });

            logger.LogInformation("loading-text");
        });

        string line = FindLine(output, "loading-text");
        Assert.Contains("[Main > LoadTextStep]", line, StringComparison.Ordinal);
        Assert.Contains("[attempt=2]", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// JSON ログが既存フィールドを維持しつつ構造化された scope 情報を追加することを検証します。
    /// </summary>
    [Fact(DisplayName = "JSON ログは EntryName StepName BranchName Attempt ExecutionPath を保持する")]
    public void JsonLogIncludesStructuredScopeFields()
    {
        string output = CaptureConsole(() =>
        {
            using var provider = CreateProvider(EngineLoggingFormat.Json);
            ILogger logger = provider.CreateLogger("Test.Category");
            using IDisposable? entryScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["EntryName"] = "Main",
            });
            using IDisposable? stepScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["StepName"] = "Decision",
                ["Attempt"] = 1,
            });
            using IDisposable? branchScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["BranchName"] = "then",
            });
            using IDisposable? childScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["StepName"] = "ThenStep",
                ["Attempt"] = 1,
            });

            logger.LogInformation("json-scope-log");
        });

        string line = FindLine(output, "json-scope-log");
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        Assert.Equal("Test.Category", root.GetProperty("Category").GetString());
        Assert.Equal("json-scope-log", root.GetProperty("Message").GetString());
        Assert.Equal("Main", root.GetProperty("EntryName").GetString());
        Assert.Equal("ThenStep", root.GetProperty("StepName").GetString());
        Assert.Equal("then", root.GetProperty("BranchName").GetString());
        Assert.Equal(1, root.GetProperty("Attempt").GetInt32());
        Assert.Equal(
            ["Main", "Decision", "then", "ThenStep"],
            root.GetProperty("ExecutionPath")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray());
    }

    /// <summary>
    /// Step 本体が出したログへ root Entry と実行中 Step の scope が適用されることを検証します。
    /// </summary>
    [Fact(DisplayName = "StepContext.Logger のログは root Entry と実行中 Step 名を表示する")]
    public void StepBodyLogIncludesCurrentStepPath()
    {
        string output = CaptureConsole(() =>
        {
            using var loggerFactory = CreateLoggerFactory(EngineLoggingFormat.Text);
            CompositeStep<string> workflow = CompositeStep
                .Define("Main")
                .Run<BodyLoggingStep, string>()
                    .StoreAs();

            WorkflowResult result = workflow.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

            Assert.True(result.Succeeded);
        });

        string line = FindLine(output, "step-body-log");
        Assert.Contains("[Main > BodyLoggingStep]", line, StringComparison.Ordinal);
        Assert.Contains("[attempt=1]", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// 外側 Step が実行する nested CompositeStep のログへ全階層が適用されることを検証します。
    /// </summary>
    [Fact(DisplayName = "nested CompositeStep の子 Step ログは外側から内側までの実行パスを表示する")]
    public void NestedCompositeLogIncludesFullExecutionPath()
    {
        string output = CaptureConsole(() =>
        {
            using var loggerFactory = CreateLoggerFactory(EngineLoggingFormat.Text);
            CompositeStep<string> workflow = CompositeStep
                .Define("Main")
                .Run<RunNestedCompositeStep, string>()
                    .StoreAs();

            WorkflowResult result = workflow.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

            Assert.True(result.Succeeded);
        });

        string line = FindLine(output, "nested-step-body-log");
        Assert.Contains(
            "[Main > RunNestedCompositeStep > Inner > NestedBodyLoggingStep]",
            line,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// If と Switch が選択された branch だけを実行パスへ追加することを検証します。
    /// </summary>
    [Fact(DisplayName = "If と Switch は選択 branch 名だけを実行パスへ表示する")]
    public void ConditionalBranchesIncludeSelectedBranchNames()
    {
        string output = CaptureConsole(() =>
        {
            using var loggerFactory = CreateLoggerFactory(EngineLoggingFormat.Text);
            CompositeStep<string> workflow = CompositeStep
                .Define("Main")
                .Run("Seed", input => "value")
                .If(
                    "Decision",
                    current => true,
                    thenFlow => thenFlow.Run("ThenStep", (current, input) =>
                    {
                        input.Context.Logger.LogInformation("if-branch-log");
                        return current;
                    }),
                    elseFlow => elseFlow.Run("ElseStep", (current, input) =>
                    {
                        input.Context.Logger.LogInformation("unselected-else-log");
                        return current;
                    }))
                .Switch<Route, string>(
                    "Route",
                    current => Route.Guide,
                    cases => cases
                        .Case(Route.Guide, branch => branch.Run("GuideStep", (current, input) =>
                        {
                            input.Context.Logger.LogInformation("switch-branch-log");
                            return current;
                        }))
                        .Default(branch => branch.Run("DefaultStep", (current, input) =>
                        {
                            input.Context.Logger.LogInformation("unselected-default-log");
                            return current;
                        })));

            WorkflowResult result = workflow.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

            Assert.True(result.Succeeded);
        });

        string ifLine = FindLine(output, "if-branch-log");
        string switchLine = FindLine(output, "switch-branch-log");
        Assert.Contains("[Main > Decision > then > ThenStep]", ifLine, StringComparison.Ordinal);
        Assert.Contains("[Main > Route > case=Guide > GuideStep]", switchLine, StringComparison.Ordinal);
        Assert.DoesNotContain("unselected-else-log", output, StringComparison.Ordinal);
        Assert.DoesNotContain("unselected-default-log", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// retry の各試行で同じ Step 名と異なる試行番号が表示されることを検証します。
    /// </summary>
    [Fact(DisplayName = "retry 中の Step 本体ログは各試行の Attempt を表示する")]
    public void RetryLogsIncludeCurrentAttempt()
    {
        RetryBodyLoggingStep.Reset();
        string output = CaptureConsole(() =>
        {
            using var loggerFactory = CreateLoggerFactory(EngineLoggingFormat.Text);
            CompositeStep<string> workflow = CompositeStep
                .Define("Main")
                .Run<RetryBodyLoggingStep, string>()
                    .StoreAs();
            var options = new WorkflowExecutionOptions(loggerFactory)
            {
                Retry = new RetryOptions
                {
                    MaxAttempts = 2,
                },
            };

            WorkflowResult result = workflow.ExecuteWorkflow(options);

            Assert.True(result.Succeeded);
        });

        string firstAttempt = FindLine(output, "retry-body-log-1");
        string secondAttempt = FindLine(output, "retry-body-log-2");
        Assert.Contains("[Main > RetryBodyLoggingStep]", firstAttempt, StringComparison.Ordinal);
        Assert.Contains("[attempt=1]", firstAttempt, StringComparison.Ordinal);
        Assert.Contains("[Main > RetryBodyLoggingStep]", secondAttempt, StringComparison.Ordinal);
        Assert.Contains("[attempt=2]", secondAttempt, StringComparison.Ordinal);
    }

    /// <summary>
    /// 指定形式の CLI logger provider を作成します。
    /// </summary>
    /// <param name="format">コンソールログ形式。</param>
    /// <returns>検査対象 provider。</returns>
    private static EngineLoggingProvider CreateProvider(EngineLoggingFormat format)
    {
        return new EngineLoggingProvider(
            new EngineLoggingOptions
            {
                ConsoleEnabled = true,
                ConsoleFormat = format,
                FileEnabled = false,
            },
            null);
    }

    /// <summary>
    /// 指定形式の CLI logger factory を作成します。
    /// </summary>
    /// <param name="format">コンソールログ形式。</param>
    /// <returns>検査対象 logger factory。</returns>
    private static EngineLoggerFactory CreateLoggerFactory(EngineLoggingFormat format)
    {
        return new EngineLoggerFactory(
            new EngineLoggingOptions
            {
                ConsoleEnabled = true,
                ConsoleFormat = format,
                FileEnabled = false,
            },
            null);
    }

    /// <summary>
    /// 標準出力を一時 writer へ差し替えて処理結果を返します。
    /// </summary>
    /// <param name="action">標準出力を収集する処理。</param>
    /// <returns>収集した標準出力。</returns>
    private static string CaptureConsole(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (ConsoleSync)
        {
            TextWriter original = Console.Out;
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            try
            {
                Console.SetOut(writer);
                action();
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(original);
            }
        }
    }

    /// <summary>
    /// 指定文字列を含む唯一のログ行を取得します。
    /// </summary>
    /// <param name="output">複数行のログ出力。</param>
    /// <param name="marker">検索する識別文字列。</param>
    /// <returns>識別文字列を含むログ行。</returns>
    private static string FindLine(string output, string marker)
    {
        return Assert.Single(
            output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(marker, StringComparison.Ordinal)));
    }

    /// <summary>
    /// StepContext.Logger へ識別ログを書き込む Step です。
    /// </summary>
    private sealed class BodyLoggingStep : IStep<string>
    {
        /// <summary>
        /// 識別ログを書き込み、固定値を返します。
        /// </summary>
        /// <param name="input">logger を含む Step 入力。</param>
        /// <returns>固定値。</returns>
        public string Execute(StepInput input)
        {
            input.Context.Logger.LogInformation("step-body-log");
            return "ok";
        }
    }

    /// <summary>
    /// 内側 CompositeStep を同じ StepInput で実行する Step です。
    /// </summary>
    private sealed class RunNestedCompositeStep : IStep<string>
    {
        /// <summary>
        /// Inner CompositeStep を実行します。
        /// </summary>
        /// <param name="input">外側 workflow の Step 入力。</param>
        /// <returns>内側 Step の戻り値。</returns>
        public string Execute(StepInput input)
        {
            CompositeStep<string> inner = CompositeStep
                .Define("Inner")
                .Run<NestedBodyLoggingStep, string>();

            return inner.Execute(input);
        }
    }

    /// <summary>
    /// nested CompositeStep 内で識別ログを書き込む Step です。
    /// </summary>
    private sealed class NestedBodyLoggingStep : IStep<string>
    {
        /// <summary>
        /// nested Step の識別ログを書き込みます。
        /// </summary>
        /// <param name="input">logger を含む Step 入力。</param>
        /// <returns>固定値。</returns>
        public string Execute(StepInput input)
        {
            input.Context.Logger.LogInformation("nested-step-body-log");
            return "inner";
        }
    }

    /// <summary>
    /// retry の最初の試行だけ失敗する Step です。
    /// </summary>
    private sealed class RetryBodyLoggingStep : IStep<string>
    {
        private static int attempts;

        /// <summary>
        /// 試行番号を記録し、最初の試行だけ失敗します。
        /// </summary>
        /// <param name="input">logger を含む Step 入力。</param>
        /// <returns>2 回目以降の固定値。</returns>
        public string Execute(StepInput input)
        {
            int attempt = Interlocked.Increment(ref attempts);
            input.Context.Logger.LogInformation("retry-body-log-{Attempt}", attempt);
            if (attempt == 1)
            {
                throw new InvalidOperationException("retry once");
            }

            return "ok";
        }

        /// <summary>
        /// 試行回数を初期化します。
        /// </summary>
        public static void Reset()
        {
            Interlocked.Exchange(ref attempts, 0);
        }
    }

    /// <summary>
    /// Switch 検査で選択する経路を表します。
    /// </summary>
    private enum Route
    {
        /// <summary>guide 経路。</summary>
        Guide,
    }
}
