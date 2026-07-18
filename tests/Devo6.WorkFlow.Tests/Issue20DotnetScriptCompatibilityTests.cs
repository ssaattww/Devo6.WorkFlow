using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// 課題 #20 の dotnet-script 互換診断契約を検査します。
/// </summary>
public sealed class Issue20DotnetScriptCompatibilityTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "devo6-workflow-issue-20-tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 課題 #20 用の一時 directory を作成します。
    /// </summary>
    public Issue20DotnetScriptCompatibilityTests()
    {
        Directory.CreateDirectory(temporaryDirectory);
    }

    /// <summary>
    /// nullable context 外の nullable annotation を Execute がコンパイル失敗として返すことを検査します。
    /// </summary>
    [Fact(DisplayName = "Execute は nullable context 外の annotation を SCRIPT_COMPILE_FAILED にする")]
    public void ExecuteRejectsNullableAnnotationWithoutNullableDirective()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class NullableStep : IStep<string?>
            {
                public string? Execute(StepInput input) => "value";
            }

            var Main = CompositeStep.Define("Main")
                .Run<NullableStep, string?>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptCompileFailed, result.ErrorCode);
        Assert.Contains("CS8632", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// nullable context 外の nullable annotation を Validate もコンパイル失敗として返すことを検査します。
    /// </summary>
    [Fact(DisplayName = "Validate は nullable context 外の annotation を SCRIPT_COMPILE_FAILED にする")]
    public void ValidateRejectsNullableAnnotationWithoutNullableDirective()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class NullableStep : IStep<string?>
            {
                public string? Execute(StepInput input) => "value";
            }

            var Main = CompositeStep.Define("Main")
                .Run<NullableStep, string?>()
                    .StoreAs();
            """);

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.Equal(WorkflowErrorCodes.ScriptCompileFailed, error.Code);
        Assert.Contains("CS8632", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// nullable context を明示した同等 script は実行できることを検査します。
    /// </summary>
    [Fact(DisplayName = "#nullable enable を付けた nullable annotation は実行できる")]
    public void ExecuteAllowsNullableAnnotationWithNullableDirective()
    {
        string scriptPath = CreateScript(
            """
            #nullable enable
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class NullableStep : IStep<string?>
            {
                public string? Execute(StepInput input) => "value";
            }

            var Main = CompositeStep.Define("Main")
                .Run<NullableStep, string?>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// nullable 範囲外の通常 warning だけでは実行を失敗させないことを検査します。
    /// </summary>
    [Fact(DisplayName = "nullable 範囲外の通常 warning はコンパイル失敗にしない")]
    public void ExecuteKeepsNonNullableWarningNonFatal()
    {
        string scriptPath = CreateScript(
            """
            #warning issue-20-non-nullable-warning
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class WarningStep : IStep<string>
            {
                public string Execute(StepInput input) => "value";
            }

            var Main = CompositeStep.Define("Main")
                .Run<WarningStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// 課題 #20 の検査用 script を一時 directory に作成します。
    /// </summary>
    /// <param name="content">script 本文。</param>
    /// <returns>作成した script の絶対 path。</returns>
    private string CreateScript(string content)
    {
        string scriptPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.csx");
        File.WriteAllText(scriptPath, content);

        return scriptPath;
    }

    /// <summary>
    /// 課題 #20 の検査用一時 directory を削除します。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
