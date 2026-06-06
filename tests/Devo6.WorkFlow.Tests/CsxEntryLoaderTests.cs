using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// Verifies the user-facing entry loading contract for trusted single-file .csx workflows.
/// </summary>
public sealed class CsxEntryLoaderTests
{
    /// <summary>
    /// Verifies that the default Main entry can be loaded from a sample .csx file and executed successfully.
    /// </summary>
    [Fact(DisplayName = "sample csx から既定 Entry 名 Main の CompositeStep を取得して実行できる")]
    public void SampleCsxから既定Entry名MainのCompositeStepを取得して実行できる()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => "main";
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.True(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("MainStep", traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, traceStep.Status);
    }

    /// <summary>
    /// Verifies that a named Build entry can be loaded from a sample .csx file and executed successfully.
    /// </summary>
    [Fact(DisplayName = "sample csx から指定 Entry 名 Build の CompositeStep を取得して実行できる")]
    public void SampleCsxから指定Entry名BuildのCompositeStepを取得して実行できる()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => "main";
            }

            public sealed class BuildStep : IStep<string>
            {
                public string Execute(StepInput input) => "build";
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            var Build = CompositeStep.Define("Build")
                .Run<BuildStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath, "Build");

        Assert.True(result.Succeeded);
        Assert.Equal("Build", result.EntryName);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("BuildStep", traceStep.StepName);
        Assert.Equal(ExecutionTraceStepStatus.Succeeded, traceStep.Status);
    }

    /// <summary>
    /// Verifies that a missing entry file is returned as a SCRIPT_LOAD_FAILED workflow failure.
    /// </summary>
    [Fact(DisplayName = "Entry file が存在しない場合は SCRIPT_LOAD_FAILED の失敗結果になる")]
    public void EntryFileが存在しない場合はScriptLoadFailedの失敗結果になる()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.csx");

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.ScriptLoadFailed, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that script compile errors are returned as SCRIPT_COMPILE_FAILED workflow failures.
    /// </summary>
    [Fact(DisplayName = "script compile error は SCRIPT_COMPILE_FAILED の失敗結果になる")]
    public void ScriptCompileErrorはScriptCompileFailedの失敗結果になる()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Engine;

            var Main = CompositeStep.Define("Main")
                .Run<MissingStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.ScriptCompileFailed, result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    /// <summary>
    /// Verifies that a missing entry name is returned as an ENTRY_STEP_NOT_FOUND workflow failure in the T14 loader path.
    /// </summary>
    [Fact(DisplayName = "Entry 名が存在しない場合は ENTRY_STEP_NOT_FOUND の失敗結果になる")]
    public void Entry名が存在しない場合はEntryStepNotFoundの失敗結果になる()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => "main";
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath, "Build");

        Assert.False(result.Succeeded);
        Assert.Equal("Build", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.EntryStepNotFound, result.ErrorCode);
    }

    private static string CreateScript(string contents)
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-workflow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "main.csx");
        File.WriteAllText(scriptPath, contents);

        return scriptPath;
    }
}
