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

    /// <summary>
    /// Verifies that an entry .csx file can load a relative local .csx file and execute a CompositeStep defined there.
    /// </summary>
    [Fact(DisplayName = "Entry csx から相対 load した file 側の CompositeStep を実行できる")]
    public void EntryCsxから相対LoadしたFile側のCompositeStepを実行できる()
    {
        string scriptPath = CreateScript(
            """
            #load "./build.csx"
            """,
            ("build.csx",
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class BuildStep : IStep<string>
            {
                public string Execute(StepInput input) => "build";
            }

            var Build = CompositeStep.Define("Build")
                .Run<BuildStep, string>()
                    .StoreAs();
            """));

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath, "Build");

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("BuildStep", traceStep.StepName);
    }

    /// <summary>
    /// Verifies that nested #load paths are resolved relative to the file that contains the directive.
    /// </summary>
    [Fact(DisplayName = "load 内の相対 path は load を書いた csx の directory 基準で解決される")]
    public void Load内の相対PathはLoadを書いたCsxのDirectory基準で解決される()
    {
        string scriptPath = CreateScript(
            """
            #load "./steps/build.csx"
            """,
            ("steps/build.csx",
            """
            #load "./nested/helper.csx"
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class BuildStep : IStep<string>
            {
                public string Execute(StepInput input) => BuildHelper.Value;
            }

            var Build = CompositeStep.Define("Build")
                .Run<BuildStep, string>()
                    .StoreAs();
            """),
            ("steps/nested/helper.csx",
            """
            public static class BuildHelper
            {
                public const string Value = "nested";
            }
            """));

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath, "Build");

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("BuildStep", traceStep.StepName);
    }

    /// <summary>
    /// Verifies that #load cannot resolve a file outside the workflow root.
    /// </summary>
    [Fact(DisplayName = "workflow root 外への load は SCRIPT_REFERENCE_NOT_ALLOWED になる")]
    public void WorkflowRoot外へのLoadはScriptReferenceNotAllowedになる()
    {
        string directory = CreateWorkflowDirectory();
        string root = Path.Combine(directory, "root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(directory, "outside.csx"), "public sealed class Outside { }");
        string scriptPath = Path.Combine(root, "main.csx");
        File.WriteAllText(
            scriptPath,
            """
            #load "../outside.csx"
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that a #load path through a root-local symlink to an outside file is rejected.
    /// </summary>
    [Fact(DisplayName = "root 内 symlink が root 外 file を指す load は SCRIPT_REFERENCE_NOT_ALLOWED になる")]
    public void Root内SymlinkがRoot外Fileを指すLoadはScriptReferenceNotAllowedになる()
    {
        string directory = CreateWorkflowDirectory();
        string root = Path.Combine(directory, "root");
        Directory.CreateDirectory(root);
        string outsidePath = Path.Combine(directory, "outside.csx");
        File.WriteAllText(outsidePath, "public sealed class Outside { }");
        File.CreateSymbolicLink(Path.Combine(root, "outside-link.csx"), outsidePath);
        string scriptPath = Path.Combine(root, "main.csx");
        File.WriteAllText(
            scriptPath,
            """
            #load "./outside-link.csx"
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that a #load path through a root-local symlink to an outside directory is rejected.
    /// </summary>
    [Fact(DisplayName = "root 内 symlink が root 外 directory を指す load は SCRIPT_REFERENCE_NOT_ALLOWED になる")]
    public void Root内SymlinkがRoot外Directoryを指すLoadはScriptReferenceNotAllowedになる()
    {
        string directory = CreateWorkflowDirectory();
        string root = Path.Combine(directory, "root");
        string outsideDirectory = Path.Combine(directory, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(outsideDirectory, "outside.csx"), "public sealed class Outside { }");
        Directory.CreateSymbolicLink(Path.Combine(root, "outside-link"), outsideDirectory);
        string scriptPath = Path.Combine(root, "main.csx");
        File.WriteAllText(
            scriptPath,
            """
            #load "./outside-link/outside.csx"
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that a #load cycle is reported without executing the workflow.
    /// </summary>
    [Fact(DisplayName = "load 循環は SCRIPT_LOAD_CYCLE_DETECTED になる")]
    public void Load循環はScriptLoadCycleDetectedになる()
    {
        string scriptPath = CreateScript(
            """
            #load "./a.csx"
            """,
            ("a.csx",
            """
            #load "./main.csx"
            """));

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptLoadCycleDetected, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that duplicate #load directives for the same normalized path do not compile duplicate definitions.
    /// </summary>
    [Fact(DisplayName = "同一正規 path の重複 load は compile を壊さない")]
    public void 同一正規Pathの重複LoadはCompileを壊さない()
    {
        string scriptPath = CreateScript(
            """
            #load "./shared.csx"
            #load "./nested/../shared.csx"
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => SharedValue.Text;
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """,
            ("shared.csx",
            """
            public static class SharedValue
            {
                public const string Text = "shared";
            }
            """));

        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(scriptPath)!, "nested"));

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("MainStep", traceStep.StepName);
    }

    /// <summary>
    /// Verifies that an unapproved #r directive is rejected before script compilation.
    /// </summary>
    [Fact(DisplayName = "許可外 r は SCRIPT_REFERENCE_NOT_ALLOWED になる")]
    public void 許可外RはScriptReferenceNotAllowedになる()
    {
        string scriptPath = CreateScript(
            """
            #r "System.Text.Json"
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that an explicitly allowed assembly name reference can be used by a workflow script.
    /// </summary>
    [Fact(DisplayName = "許可された assembly 名参照は実行できる")]
    public void 許可されたAssembly名参照は実行できる()
    {
        string scriptPath = CreateScript(
            """
            #r "System.Text.Json"
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.Text.Json;

            public sealed class JsonStep : IStep<string>
            {
                public string Execute(StepInput input) => JsonSerializer.Serialize("json");
            }

            var Main = CompositeStep.Define("Main")
                .Run<JsonStep, string>()
                    .StoreAs();
            """);
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            AllowedAssemblyReferences = ["System.Text.Json"],
        });

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("JsonStep", traceStep.StepName);
    }

    /// <summary>
    /// Verifies that unapproved NuGet references are rejected without restoring the package.
    /// </summary>
    [Fact(DisplayName = "許可一覧にない NuGet 参照は SCRIPT_REFERENCE_NOT_ALLOWED になる")]
    public void 許可一覧にないNuGet参照はScriptReferenceNotAllowedになる()
    {
        string scriptPath = CreateScript(
            """
            #r "nuget: CsvHelper, 33.0.1"
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that an explicitly allowed NuGet reference can restore and expose package types to the workflow script.
    /// </summary>
    [Fact(DisplayName = "許可された NuGet 参照は package 型を使って実行できる")]
    public void 許可されたNuGet参照はPackage型を使って実行できる()
    {
        string scriptPath = CreateScript(
            """
            #r "nuget: NodaTime, 3.1.11"
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using NodaTime;

            public sealed class JsonStep : IStep<string>
            {
                public string Execute(StepInput input) => new LocalDate(2024, 1, 2).Year.ToString();
            }

            var Main = CompositeStep.Define("Main")
                .Run<JsonStep, string>()
                    .StoreAs();
            """);
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            AllowedNuGetReferences = [new CsxNuGetReference("NodaTime", "3.1.11")],
        });

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("JsonStep", traceStep.StepName);
    }

    /// <summary>
    /// Verifies that floating NuGet versions are rejected even when the package id is otherwise allowed.
    /// </summary>
    [Fact(DisplayName = "浮動 NuGet version は SCRIPT_REFERENCE_NOT_ALLOWED になる")]
    public void 浮動NuGetVersionはScriptReferenceNotAllowedになる()
    {
        string scriptPath = CreateScript(
            """
            #r "nuget: CsvHelper, *"
            """);
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            AllowedNuGetReferences = [new CsxNuGetReference("CsvHelper", "33.0.1")],
        });

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, result.ErrorCode);
    }

    private static string CreateScript(string contents, params (string RelativePath, string Contents)[] additionalFiles)
    {
        string directory = CreateWorkflowDirectory();
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "main.csx");
        File.WriteAllText(scriptPath, contents);

        foreach ((string relativePath, string fileContents) in additionalFiles)
        {
            string filePath = Path.Combine(directory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, fileContents);
        }

        return scriptPath;
    }

    private static string CreateWorkflowDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "devo6-workflow-tests", Guid.NewGuid().ToString("N"));
    }
}
