using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// Verifies the user-facing pre-execution validation contract for trusted .csx workflows.
/// </summary>
public sealed class CsxEntryValidationTests
{
    /// <summary>
    /// Verifies that a valid .csx workflow validates successfully without errors.
    /// </summary>
    [Fact(DisplayName = "valid csx は validation success になり errors が空になる")]
    public void ValidCsxはValidationSuccessになりErrorsが空になる()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => throw new InvalidOperationException("validate must not execute steps");
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """);

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// Verifies that a missing entry .csx file returns ENTRY_SCRIPT_NOT_FOUND.
    /// </summary>
    [Fact(DisplayName = "Entry csx が存在しない場合は ENTRY_SCRIPT_NOT_FOUND が返る")]
    public void EntryCsxが存在しない場合はEntryScriptNotFoundが返る()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.csx");

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(scriptPath, error.Path);
        Assert.Equal(WorkflowErrorCodes.EntryScriptNotFound, error.Code);
    }

    /// <summary>
    /// Verifies that a missing requested entry name returns ENTRY_STEP_NOT_FOUND.
    /// </summary>
    [Fact(DisplayName = "指定 Entry 名が存在しない場合は ENTRY_STEP_NOT_FOUND が返る")]
    public void 指定Entry名が存在しない場合はEntryStepNotFoundが返る()
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

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath, "Build");

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal("Build", error.Path);
        Assert.Equal(WorkflowErrorCodes.EntryStepNotFound, error.Code);
    }

    /// <summary>
    /// Verifies that duplicate public CompositeStep names across loaded script variables return DUPLICATE_STEP_NAME.
    /// </summary>
    [Fact(DisplayName = "公開 CompositeStep 名の重複は DUPLICATE_STEP_NAME が返る")]
    public void 公開CompositeStep名の重複はDuplicateStepNameが返る()
    {
        string scriptPath = CreateScript(
            """
            #load "./build.csx"
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => "main";
            }

            var Main = CompositeStep.Define("Shared")
                .Run<MainStep, string>()
                    .StoreAs();
            """,
            ("build.csx",
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class BuildStep : IStep<string>
            {
                public string Execute(StepInput input) => "build";
            }

            var Build = CompositeStep.Define("Shared")
                .Run<BuildStep, string>()
                    .StoreAs();
            """));

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath, "Shared");

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal("Shared", error.Path);
        Assert.Equal(WorkflowErrorCodes.DuplicateStepName, error.Code);
    }

    /// <summary>
    /// Verifies that #load reference errors are returned as validation errors.
    /// </summary>
    [Fact(DisplayName = "load 参照解決エラーは validation error として返る")]
    public void Load参照解決エラーはValidationErrorとして返る()
    {
        string directory = CreateWorkflowDirectory();
        string root = Path.Combine(directory, "root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(directory, "outside.csx"), "public sealed class Outside { }");
        string scriptPath = Path.Combine(root, "main.csx");
        File.WriteAllText(scriptPath, "#load \"../outside.csx\"");

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, error.Code);
    }

    /// <summary>
    /// Verifies that #load cycles are returned as validation errors.
    /// </summary>
    [Fact(DisplayName = "load 循環は validation error として返る")]
    public void Load循環はValidationErrorとして返る()
    {
        string scriptPath = CreateScript(
            """
            #load "./a.csx"
            """,
            ("a.csx", "#load \"./main.csx\""));

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptLoadCycleDetected, error.Code);
    }

    /// <summary>
    /// Verifies that unapproved #r references are returned as validation errors.
    /// </summary>
    [Fact(DisplayName = "許可外 r は validation error として返る")]
    public void 許可外RはValidationErrorとして返る()
    {
        string scriptPath = CreateScript("#r \"System.Text.Json\"");

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, error.Code);
    }

    /// <summary>
    /// Verifies that unapproved NuGet references are returned as validation errors.
    /// </summary>
    [Fact(DisplayName = "許可外 NuGet は validation error として返る")]
    public void 許可外NuGetはValidationErrorとして返る()
    {
        string scriptPath = CreateScript("#r \"nuget: CsvHelper, 33.0.1\"");

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, error.Code);
    }

    /// <summary>
    /// Verifies that .csx compile errors are returned as SCRIPT_COMPILE_FAILED validation errors.
    /// </summary>
    [Fact(DisplayName = "csx compile error は SCRIPT_COMPILE_FAILED が返る")]
    public void CsxCompileErrorはScriptCompileFailedが返る()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Engine;

            var Main = CompositeStep.Define("Main")
                .Run<MissingStep, string>()
                    .StoreAs();
            """);

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptCompileFailed, error.Code);
    }

    /// <summary>
    /// Verifies that referencing a copied workflow API assembly is rejected as an API identity mismatch.
    /// </summary>
    [Fact(DisplayName = "別 copy の public API assembly 参照は SCRIPT_API_IDENTITY_MISMATCH が返る")]
    public void 別CopyのPublicApiAssembly参照はScriptApiIdentityMismatchが返る()
    {
        string scriptPath = CreateScript(
            """
            #r "./lib/Devo6.WorkFlow.Abstractions.dll"
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        string libDirectory = Path.Combine(directory, "lib");
        Directory.CreateDirectory(libDirectory);
        File.Copy(
            typeof(IStep<>).Assembly.Location,
            Path.Combine(libDirectory, "Devo6.WorkFlow.Abstractions.dll"));
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            AllowedReferenceDirectories = [libDirectory],
        });

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptApiIdentityMismatch, error.Code);
    }

    /// <summary>
    /// Verifies that missing config file paths are returned as CONFIG_NOT_FOUND validation errors.
    /// </summary>
    [Fact(DisplayName = "存在しない config file path は CONFIG_NOT_FOUND が返る")]
    public void 存在しないConfigFilePathはConfigNotFoundが返る()
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

        WorkflowValidationResult result = new CsxEntryLoader().Validate(
            scriptPath,
            validationOptions: new CsxValidationOptions
            {
                ConfigPaths = ["config/appsettings.yaml"],
            });

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal("config/appsettings.yaml", error.Path);
        Assert.Equal(WorkflowErrorCodes.ConfigNotFound, error.Code);
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
        return Path.Combine(Path.GetTempPath(), "devo6-workflow-validation-tests", Guid.NewGuid().ToString("N"));
    }
}
