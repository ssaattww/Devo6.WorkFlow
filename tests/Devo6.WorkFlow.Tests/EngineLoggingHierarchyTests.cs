using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Cli;
using Devo6.WorkFlow.Engine;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// Engine と CLI logger provider の実行階層ログを検証します。
/// </summary>
public sealed class EngineLoggingHierarchyTests
{
    /// <summary>
    /// workflow の Engine ログと Step 本体ログに Entry、Step、試行番号が表示されることを検証します。
    /// </summary>
    [Fact(DisplayName = "workflow の Engine と Step ログに実行中 Step 名と試行番号が表示される")]
    public void WorkflowLogsIncludeEntryStepAndAttempt()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string content;
            {
                var loggingOptions = new EngineLoggingOptions
                {
                    ConsoleEnabled = false,
                    FileEnabled = true,
                    FileDirectory = directory,
                    FileNameFormat = "{RootStepName}.log",
                    FileFormat = EngineLoggingFormat.Text,
                };
                using var loggerFactory = new EngineLoggerFactory(loggingOptions, null);
                CompositeStep<string> workflow = CompositeStep
                    .Define("Main")
                    .Run<LoggingStep, string>()
                        .StoreAs();

                WorkflowResult result = workflow.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

                Assert.True(result.Succeeded);
            }

            content = File.ReadAllText(Path.Combine(directory, "Main.log"));
            Assert.Contains(
                "Devo6.WorkFlow.Engine [Main > LoggingStep] [attempt=1] Step started for attempt 1",
                content,
                StringComparison.Ordinal);
            Assert.Contains(
                "Devo6.WorkFlow.Step [Main > LoggingStep] [attempt=1] step-body",
                content,
                StringComparison.Ordinal);
            Assert.Contains(
                "Devo6.WorkFlow.Engine [Main > LoggingStep] [attempt=1] Step succeeded on attempt 1",
                content,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 選択された If branch の子 Step が親制御 Step を含む実行 path を持つことを検証します。
    /// </summary>
    [Fact(DisplayName = "If branch のログは親制御 Step と選択された子 Step の階層を表示する")]
    public void IfBranchLogsIncludeControlAndSelectedStep()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string content;
            {
                var loggingOptions = new EngineLoggingOptions
                {
                    ConsoleEnabled = false,
                    FileEnabled = true,
                    FileDirectory = directory,
                    FileNameFormat = "workflow.log",
                    FileFormat = EngineLoggingFormat.Text,
                };
                using var loggerFactory = new EngineLoggerFactory(loggingOptions, null);
                CompositeStep<string> workflow = CompositeStep
                    .Define("Main")
                    .Run("Seed", _ => "seed")
                    .If(
                        "Choice",
                        _ => true,
                        thenFlow => thenFlow.Run("ThenStep", current => current),
                        elseFlow => elseFlow.Run("ElseStep", current => current));

                WorkflowResult result = workflow.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

                Assert.True(result.Succeeded);
            }

            content = File.ReadAllText(Path.Combine(directory, "workflow.log"));
            Assert.Contains("[Main > Choice > then > ThenStep] [attempt=1]", content, StringComparison.Ordinal);
            Assert.DoesNotContain("ElseStep", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// retry の各試行に同じ Step 名と対応する試行番号が表示されることを検証します。
    /// </summary>
    [Fact(DisplayName = "retry ログは実行中 Step 名と各試行番号を表示する")]
    public void RetryLogsIncludeAttemptForCurrentStep()
    {
        string directory = CreateTemporaryDirectory();
        RetryLoggingStep.Reset();
        try
        {
            string content;
            {
                var loggingOptions = new EngineLoggingOptions
                {
                    ConsoleEnabled = false,
                    FileEnabled = true,
                    FileDirectory = directory,
                    FileNameFormat = "workflow.log",
                    FileFormat = EngineLoggingFormat.Text,
                };
                using var loggerFactory = new EngineLoggerFactory(loggingOptions, null);
                CompositeStep<string> workflow = CompositeStep
                    .Define("Main")
                    .Run<RetryLoggingStep, string>()
                        .StoreAs();
                var executionOptions = new WorkflowExecutionOptions(loggerFactory)
                {
                    Retry = new RetryOptions
                    {
                        MaxAttempts = 2,
                    },
                };

                WorkflowResult result = workflow.ExecuteWorkflow(executionOptions);

                Assert.True(result.Succeeded);
            }

            content = File.ReadAllText(Path.Combine(directory, "workflow.log"));
            Assert.Contains(
                "[Main > RetryLoggingStep] [attempt=1] Step attempt 1 failed",
                content,
                StringComparison.Ordinal);
            Assert.Contains(
                "[Main > RetryLoggingStep] [attempt=2] Step started for attempt 2",
                content,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// JSON ログが実行 path を配列として保持し、scope 破棄後に子情報を漏らさないことを検証します。
    /// </summary>
    [Fact(DisplayName = "JSON ログは構造化実行 path を保持し scope 破棄後に子情報を漏らさない")]
    public void JsonLogsContainStructuredPathWithoutScopeLeak()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string[] lines;
            {
                var loggingOptions = new EngineLoggingOptions
                {
                    ConsoleEnabled = false,
                    FileEnabled = true,
                    FileDirectory = directory,
                    FileNameFormat = "scope.jsonl",
                    FileFormat = EngineLoggingFormat.Json,
                };
                using var provider = new EngineLoggingProvider(
                    loggingOptions,
                    null,
                    () => new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));
                ILogger logger = provider.CreateLogger("Test.Category");

                using (logger.BeginScope(new Dictionary<string, object?>
                {
                    ["EntryName"] = "Main",
                }))
                {
                    using (logger.BeginScope(new Dictionary<string, object?>
                    {
                        ["StepName"] = "Choice",
                        ["Attempt"] = 1,
                    }))
                    using (logger.BeginScope(new Dictionary<string, object?>
                    {
                        ["BranchName"] = "then",
                    }))
                    using (logger.BeginScope(new Dictionary<string, object?>
                    {
                        ["StepName"] = "ThenStep",
                        ["Attempt"] = 2,
                    }))
                    {
                        logger.LogInformation("branch-body");
                    }

                    logger.LogInformation("entry-body");
                }

            }

            lines = File.ReadAllLines(Path.Combine(directory, "scope.jsonl"));
            Assert.Equal(2, lines.Length);

            using JsonDocument branchDocument = JsonDocument.Parse(lines[0]);
            JsonElement branchRoot = branchDocument.RootElement;
            Assert.Equal("Main", branchRoot.GetProperty("EntryName").GetString());
            Assert.Equal("ThenStep", branchRoot.GetProperty("StepName").GetString());
            Assert.Equal("then", branchRoot.GetProperty("BranchName").GetString());
            Assert.Equal(2, branchRoot.GetProperty("Attempt").GetInt32());
            Assert.Equal(
                new[] { "Main", "Choice", "then", "ThenStep" },
                branchRoot.GetProperty("ExecutionPath").EnumerateArray().Select(element => element.GetString()!).ToArray());

            using JsonDocument entryDocument = JsonDocument.Parse(lines[1]);
            JsonElement entryRoot = entryDocument.RootElement;
            Assert.Equal("Main", entryRoot.GetProperty("EntryName").GetString());
            Assert.Equal(JsonValueKind.Null, entryRoot.GetProperty("StepName").ValueKind);
            Assert.Equal(JsonValueKind.Null, entryRoot.GetProperty("BranchName").ValueKind);
            Assert.Equal(JsonValueKind.Null, entryRoot.GetProperty("Attempt").ValueKind);
            Assert.Equal(
                new[] { "Main" },
                entryRoot.GetProperty("ExecutionPath").EnumerateArray().Select(element => element.GetString()!).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 検査ごとに独立した一時ディレクトリを作成します。
    /// </summary>
    /// <returns>作成した一時ディレクトリの絶対 path。</returns>
    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-logging-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Step 本体ログを出力する検査用 Step です。
    /// </summary>
    private sealed class LoggingStep : IStep<string>
    {
        /// <summary>
        /// Step 本体ログを出力して固定値を返します。
        /// </summary>
        /// <param name="input">logger を含む Step 入力。</param>
        /// <returns>固定文字列。</returns>
        public string Execute(StepInput input)
        {
            input.Context.Logger.LogInformation("step-body");
            return "ok";
        }
    }

    /// <summary>
    /// 最初の試行だけ失敗する検査用 Step です。
    /// </summary>
    private sealed class RetryLoggingStep : IStep<string>
    {
        private static int attempts;

        /// <summary>
        /// 試行回数を初期化します。
        /// </summary>
        public static void Reset()
        {
            Interlocked.Exchange(ref attempts, 0);
        }

        /// <summary>
        /// 最初の試行だけ例外を送出し、2 回目は固定値を返します。
        /// </summary>
        /// <param name="input">未使用の Step 入力。</param>
        /// <returns>成功時の固定文字列。</returns>
        public string Execute(StepInput input)
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("retry once");
            }

            return "ok";
        }
    }
}
