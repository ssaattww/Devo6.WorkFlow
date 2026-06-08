using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// 信頼済み .csx workflow の実行前 validation が利用者向けの成功結果と validation error を返すことを検査します。
/// </summary>
public sealed class CsxEntryValidationTests
{
    /// <summary>
    /// 妥当な csx workflow は Step を実行せずに validation success となり errors が空になることを検査します。
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
    /// 存在しない Entry csx に ENTRY_SCRIPT_NOT_FOUND の validation error が返ることを検査します。
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
    /// 指定した Entry 名が存在しない場合に、その Entry 名を path として ENTRY_STEP_NOT_FOUND が返ることを検査します。
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
    /// 名前空間付き Entry を公開完全修飾名で検証できることを検査します。
    /// </summary>
    [Fact(DisplayName = "名前空間付き Entry は公開完全修飾名で検証できる")]
    public void ValidateQualifiedNamespaceEntryByPublicName()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// デプロイ用 Deploy 名前空間の Build Entry で検証対象にする Step です。
            /// </summary>
            public sealed class DeployBuildStep : IStep<string>
            {
                /// <summary>
                /// デプロイ用 Deploy Build の固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>Deploy Build の固定値。</returns>
                public string Execute(StepInput input) => "deploy";
            }

            var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<DeployBuildStep, string>()
                    .StoreAs();
            """);

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath, "Deploy.Build");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// 読み込まれた script 変数を含む公開 CompositeStep 名の重複が DUPLICATE_STEP_NAME として返ることを検査します。
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
    /// 名前空間付き Entry の完全修飾名重複が DUPLICATE_STEP_NAME になることを検査します。
    /// </summary>
    [Fact(DisplayName = "名前空間付き Entry の完全修飾名重複は DUPLICATE_STEP_NAME が返る")]
    public void ValidateDuplicateQualifiedNamespaceEntryFailsWithDuplicateStepName()
    {
        string scriptPath = CreateScript(
            """
            #load "./deploy-copy.csx"
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// 直接定義された Deploy Build Entry で検証対象にする Step です。
            /// </summary>
            public sealed class DirectDeployBuildStep : IStep<string>
            {
                /// <summary>
                /// 直接定義された Deploy Build の固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>直接定義された Deploy Build の固定値。</returns>
                public string Execute(StepInput input) => "direct";
            }

            var DirectDeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<DirectDeployBuildStep, string>()
                    .StoreAs();
            """,
            ("deploy-copy.csx",
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// 読み込み先で重複定義された Deploy Build Entry の Step です。
            /// </summary>
            public sealed class LoadedDeployBuildStep : IStep<string>
            {
                /// <summary>
                /// 読み込み先の Deploy Build 固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>load 先の Deploy Build 固定値。</returns>
                public string Execute(StepInput input) => "loaded";
            }

            var LoadedDeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<LoadedDeployBuildStep, string>()
                    .StoreAs();
            """));

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath, "Deploy.Build");

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal("Deploy.Build", error.Path);
        Assert.Equal(WorkflowErrorCodes.DuplicateStepName, error.Code);
    }

    /// <summary>
    /// ワークフロー root 外を指す load 参照解決エラーが validation error として返ることを検査します。
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
    /// 読み込み循環が validation error として返り SCRIPT_LOAD_CYCLE_DETECTED になることを検査します。
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
    /// 許可されていない r 参照が validation error として返り SCRIPT_REFERENCE_NOT_ALLOWED になることを検査します。
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
    /// 明示制限された NuGet 参照が validation error として返り SCRIPT_REFERENCE_NOT_ALLOWED になることを検査します。
    /// </summary>
    [Fact(DisplayName = "明示制限外 NuGet は validation error として返る")]
    public void 明示制限外NuGetはValidationErrorとして返る()
    {
        string scriptPath = CreateScript("#r \"nuget: CsvHelper, 33.0.1\"");
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            AllowedNuGetReferences = [new CsxNuGetReference("Other.Package", "1.0.0")],
        });

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, error.Code);
    }

    /// <summary>
    /// スクリプト csx の compile error が SCRIPT_COMPILE_FAILED の validation error として返ることを検査します。
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
    /// 複製された workflow public API assembly への参照が API identity mismatch として拒否されることを検査します。
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
    /// 存在しない config file path が CONFIG_NOT_FOUND の validation error として返ることを検査します。
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

    /// <summary>
    /// 一時 workflow directory に main.csx と追加 file を作成します。
    /// </summary>
    /// <param name="contents">main.csx に書き込む内容。</param>
    /// <param name="additionalFiles">main.csx と同じ workflow directory に作成する追加 file。</param>
    /// <returns>作成した main.csx の path。</returns>
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

    /// <summary>
    /// 検証用の一時 workflow directory path を作成します。
    /// </summary>
    /// <returns>検証用の一時 workflow directory path。</returns>
    private static string CreateWorkflowDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "devo6-workflow-validation-tests", Guid.NewGuid().ToString("N"));
    }
}
