using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Cli;
using Devo6.WorkFlow.Engine;
using Microsoft.Extensions.Logging;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// Switch branch 名をログへ安全に表示する契約を検証します。
/// </summary>
public sealed class SwitchBranchLoggingSafetyTests
{
    /// <summary>
    /// case 値の制御文字を空白へ置換し、表示を 128 文字へ制限することを検証します。
    /// </summary>
    [Fact(DisplayName = "Switch case ログ名は制御文字を除去して 128 文字へ制限する")]
    public void SwitchCaseLogNameSanitizesControlCharactersAndLength()
    {
        string directory = CreateTemporaryDirectory();
        string caseValue = new string('A', 130) + "\nsecret";
        try
        {
            using EngineLoggerFactory loggerFactory = CreateLoggerFactory(directory);
            CompositeStep<string> workflow = CompositeStep
                .Define("Main")
                .Run("Seed", input => "seed")
                .Switch<string, string>(
                    "Route",
                    current => caseValue,
                    cases => cases
                        .Case(caseValue, branch => branch.Run("Selected", (current, input) =>
                        {
                            input.Context.Logger.LogInformation("sanitized-case-log");
                            return current;
                        }))
                        .Default(branch => branch.Run("DefaultStep", current => current)));

            WorkflowResult result = workflow.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

            Assert.True(result.Succeeded);
            string content = File.ReadAllText(Path.Combine(directory, "workflow.log"));
            Assert.Contains(
                $"[Main > Route > case={new string('A', 128)} > Selected]",
                content,
                StringComparison.Ordinal);
            Assert.DoesNotContain("secret", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// case 値の文字列化に失敗しても workflow を失敗させず fallback 名を使うことを検証します。
    /// </summary>
    [Fact(DisplayName = "Switch case の ToString 失敗は unavailable 表示へ fallback する")]
    public void SwitchCaseLogNameFallsBackWhenToStringFails()
    {
        string directory = CreateTemporaryDirectory();
        var selectedCase = new ThrowingCaseToken();
        try
        {
            using EngineLoggerFactory loggerFactory = CreateLoggerFactory(directory);
            CompositeStep<string> workflow = CompositeStep
                .Define("Main")
                .Run("Seed", input => "seed")
                .Switch<ThrowingCaseToken, string>(
                    "Route",
                    current => selectedCase,
                    cases => cases
                        .Case(selectedCase, branch => branch.Run("Selected", (current, input) =>
                        {
                            input.Context.Logger.LogInformation("unavailable-case-log");
                            return current;
                        }))
                        .Default(branch => branch.Run("DefaultStep", current => current)));

            WorkflowResult result = workflow.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

            Assert.True(result.Succeeded);
            string content = File.ReadAllText(Path.Combine(directory, "workflow.log"));
            Assert.Contains(
                "[Main > Route > case=<unavailable> > Selected]",
                content,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 一致 case がない場合に default branch 名を表示することを検証します。
    /// </summary>
    [Fact(DisplayName = "Switch 不一致時のログ path は default branch を表示する")]
    public void SwitchDefaultLogNameUsesDefaultBranch()
    {
        string directory = CreateTemporaryDirectory();
        var registeredCase = new CaseToken("registered");
        var selectedCase = new CaseToken("selected");
        try
        {
            using EngineLoggerFactory loggerFactory = CreateLoggerFactory(directory);
            CompositeStep<string> workflow = CompositeStep
                .Define("Main")
                .Run("Seed", input => "seed")
                .Switch<CaseToken, string>(
                    "Route",
                    current => selectedCase,
                    cases => cases
                        .Case(registeredCase, branch => branch.Run("RegisteredStep", current => current))
                        .Default(branch => branch.Run("DefaultStep", (current, input) =>
                        {
                            input.Context.Logger.LogInformation("default-branch-log");
                            return current;
                        })));

            WorkflowResult result = workflow.ExecuteWorkflow(new WorkflowExecutionOptions(loggerFactory));

            Assert.True(result.Succeeded);
            string content = File.ReadAllText(Path.Combine(directory, "workflow.log"));
            Assert.Contains(
                "[Main > Route > default > DefaultStep]",
                content,
                StringComparison.Ordinal);
            Assert.DoesNotContain("RegisteredStep", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// ファイル出力だけを有効にした logger factory を作成します。
    /// </summary>
    /// <param name="directory">ログ出力先ディレクトリ。</param>
    /// <returns>検査対象 logger factory。</returns>
    private static EngineLoggerFactory CreateLoggerFactory(string directory)
    {
        return new EngineLoggerFactory(
            new EngineLoggingOptions
            {
                ConsoleEnabled = false,
                FileEnabled = true,
                FileDirectory = directory,
                FileNameFormat = "workflow.log",
                FileFormat = EngineLoggingFormat.Text,
            },
            null);
    }

    /// <summary>
    /// 検査ごとに独立した一時ディレクトリを作成します。
    /// </summary>
    /// <returns>作成したディレクトリ。</returns>
    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-switch-log-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// 文字列化に失敗する case 値です。
    /// </summary>
    private sealed class ThrowingCaseToken
    {
        /// <summary>
        /// fallback 契約を検証するため例外を送出します。
        /// </summary>
        /// <returns>戻りません。</returns>
        public override string ToString()
        {
            throw new InvalidOperationException("case value cannot be formatted");
        }
    }

    /// <summary>
    /// 参照同一性で比較される case 値です。
    /// </summary>
    private sealed class CaseToken
    {
        /// <summary>
        /// 表示名を指定して case 値を作成します。
        /// </summary>
        /// <param name="name">表示名。</param>
        public CaseToken(string name)
        {
            Name = name;
        }

        /// <summary>
        /// 表示名を取得します。
        /// </summary>
        private string Name { get; }

        /// <summary>
        /// 表示名を返します。
        /// </summary>
        /// <returns>case 値の表示名。</returns>
        public override string ToString()
        {
            return Name;
        }
    }
}
