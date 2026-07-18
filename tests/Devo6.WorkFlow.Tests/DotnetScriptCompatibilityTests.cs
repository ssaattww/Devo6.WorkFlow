using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// dotnet-script と同じ nullable 診断境界で csx を検証することを確認します。
/// </summary>
public sealed class DotnetScriptCompatibilityTests
{
    /// <summary>
    /// nullable context 外の nullable annotation を実行時コンパイル失敗として扱うことを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute は nullable context 外の nullable annotation をコンパイル失敗にする")]
    public void ExecuteTreatsNullableAnnotationOutsideContextAsCompileFailure()
    {
        string scriptPath = CreateScript(CreateNullableScript(nullableEnabled: false));

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptCompileFailed, result.ErrorCode);
        Assert.Contains("CS8632", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// nullable context 外の nullable annotation を実行前検証でも同じコンパイル失敗として扱うことを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate は nullable context 外の nullable annotation をコンパイル失敗にする")]
    public void ValidateTreatsNullableAnnotationOutsideContextAsCompileFailure()
    {
        string scriptPath = CreateScript(CreateNullableScript(nullableEnabled: false));

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        Assert.False(result.Succeeded);
        ValidationError error = Assert.Single(result.Errors);
        Assert.Equal(WorkflowErrorCodes.ScriptCompileFailed, error.Code);
        Assert.Contains("CS8632", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// nullable context を有効にした同等 script は実行できることを確認します。
    /// </summary>
    [Fact(DisplayName = "#nullable enable がある nullable annotation は実行できる")]
    public void ExecuteAllowsNullableAnnotationWhenContextIsEnabled()
    {
        string scriptPath = CreateScript(CreateNullableScript(nullableEnabled: true));

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.True(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
    }

    /// <summary>
    /// dotnet-script がエラーへ昇格しない通常 warning は実行を妨げないことを確認します。
    /// </summary>
    [Fact(DisplayName = "nullable 以外の通常 warning はコンパイル失敗にしない")]
    public void ExecuteDoesNotPromoteUnrelatedWarning()
    {
        string scriptPath = CreateScript(
            """
            #warning expected ordinary warning
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class WarningStep : IStep<string>
            {
                public string Execute(StepInput input) => "ok";
            }

            var Main = CompositeStep.Define("Main")
                .Run<WarningStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// nullable context の有無を切り替えた検査用 script を作成します。
    /// </summary>
    /// <param name="nullableEnabled">nullable context を有効にする場合は true。</param>
    /// <returns>検査用 csx 本文。</returns>
    private static string CreateNullableScript(bool nullableEnabled)
    {
        string nullableDirective = nullableEnabled ? "#nullable enable" : "";

        return $$"""
            {{nullableDirective}}
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class NullableStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    string? value = "ok";
                    return value;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<NullableStep, string>()
                    .StoreAs();
            """;
    }

    /// <summary>
    /// 一時 directory に検査用 csx を作成します。
    /// </summary>
    /// <param name="source">書き込む csx 本文。</param>
    /// <returns>作成した csx の絶対 path。</returns>
    private static string CreateScript(string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"devo6-dotnet-script-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "main.csx");
        File.WriteAllText(scriptPath, source);

        return scriptPath;
    }
}
