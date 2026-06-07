using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// Verifies the user-facing entry loading contract for trusted single-file .csx workflows.
/// </summary>
public sealed class CsxEntryLoaderTests
{
    /// <summary>
    /// 標準 NuGet lock file 名を表します。
    /// </summary>
    private const string DefaultNuGetLockFileName = "devo6.nuget.lock.yaml";

    /// <summary>
    /// 標準 fixture の target framework を表します。
    /// </summary>
    private const string DefaultTargetFramework = "net8.0";

    /// <summary>
    /// 標準 fixture の runtime identifier を表します。
    /// </summary>
    private const string DefaultRuntimeIdentifier = "linux-x64";

    /// <summary>
    /// 標準 fixture の Dotnet.Script.Core version を表します。
    /// </summary>
    private const string DefaultDotnetScriptCoreVersion = "2.0.1";

    /// <summary>
    /// 標準 fixture の package source 一覧を表します。
    /// </summary>
    private static readonly string[] DefaultPackageSources = ["https://api.nuget.org/v3/index.json"];

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
    /// Verifies that a .csx entry using RunAsync is awaited through the normal loader execution path.
    /// </summary>
    [Fact(DisplayName = "RunAsync を含む csx Entry を読み込んで実行できる")]
    public void RunAsyncを含むCsxEntryを読み込んで実行できる()
    {
        string scriptPath = CreateScript(
            """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class AsyncMainStep : IAsyncStep<string>
            {
                public async Task<string> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();

                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "async-ran.txt"), "done");

                    return "main";
                }
            }

            var Main = CompositeStep.Define("Main")
                .RunAsync<AsyncMainStep, string>()
                    .StoreAs();
            """);
        string markerPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "async-ran.txt");

        WorkflowResult result = new CsxEntryLoader().Execute(
            scriptPath,
            options: new WorkflowExecutionOptions(engineArguments: new EngineArguments { EntryPath = scriptPath }));

        Assert.True(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal("done", File.ReadAllText(markerPath));
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("AsyncMainStep", traceStep.StepName);
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
    /// 名前空間付き Entry を公開完全修飾名で実行できることを検査します。
    /// </summary>
    [Fact(DisplayName = "CompositeStep の名前空間付き Entry は公開完全修飾名で実行できる")]
    public void ExecuteQualifiedNamespaceEntryByPublicName()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// Deploy 名前空間の Build Entry で実行する Step です。
            /// </summary>
            public sealed class DeployBuildStep : IStep<string>
            {
                /// <summary>
                /// Deploy Build の固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>Deploy Build の固定値。</returns>
                public string Execute(StepInput input) => "deploy";
            }

            var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<DeployBuildStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath, "Deploy.Build");

        Assert.True(result.Succeeded);
        Assert.Equal("Deploy.Build", result.EntryName);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("DeployBuildStep", traceStep.StepName);
    }

    /// <summary>
    /// 異なる名前空間で同じ短い Entry 名を持つ Entry が共存できることを検査します。
    /// </summary>
    [Fact(DisplayName = "異なる名前空間の同名 Entry は共存して実行できる")]
    public void ExecuteAllowsSameShortNameInDifferentNamespaces()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// Deploy 名前空間の Build Entry で実行する Step です。
            /// </summary>
            public sealed class DeployBuildStep : IStep<string>
            {
                /// <summary>
                /// Deploy Build の固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>Deploy Build の固定値。</returns>
                public string Execute(StepInput input) => "deploy";
            }

            /// <summary>
            /// Test 名前空間の Build Entry で実行する Step です。
            /// </summary>
            public sealed class TestBuildStep : IStep<string>
            {
                /// <summary>
                /// Test Build の固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>Test Build の固定値。</returns>
                public string Execute(StepInput input) => "test";
            }

            var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<DeployBuildStep, string>()
                    .StoreAs();
            var TestBuild = CompositeStep.Define("Build", namespaceName: "Test")
                .Run<TestBuildStep, string>()
                    .StoreAs();
            """);

        WorkflowResult deployResult = new CsxEntryLoader().Execute(scriptPath, "Deploy.Build");
        WorkflowResult testResult = new CsxEntryLoader().Execute(scriptPath, "Test.Build");

        Assert.True(deployResult.Succeeded);
        Assert.Equal("Deploy.Build", deployResult.EntryName);
        Assert.Equal("DeployBuildStep", Assert.Single(deployResult.Trace!.Steps).StepName);
        Assert.True(testResult.Succeeded);
        Assert.Equal("Test.Build", testResult.EntryName);
        Assert.Equal("TestBuildStep", Assert.Single(testResult.Trace!.Steps).StepName);
    }

    /// <summary>
    /// 名前空間なし候補がなく短い Entry 名候補が一意なら短い Entry 名で互換解決できることを検査します。
    /// </summary>
    [Fact(DisplayName = "短い Entry 名は名前空間付き候補が一意なら互換解決できる")]
    public void ExecuteShortEntryNameResolvesSingleNamespaceCandidate()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// Deploy 名前空間の Build Entry で実行する Step です。
            /// </summary>
            public sealed class DeployBuildStep : IStep<string>
            {
                /// <summary>
                /// Deploy Build の固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>Deploy Build の固定値。</returns>
                public string Execute(StepInput input) => "deploy";
            }

            var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<DeployBuildStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath, "Build");

        Assert.True(result.Succeeded);
        Assert.Equal("Deploy.Build", result.EntryName);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("DeployBuildStep", traceStep.StepName);
    }

    /// <summary>
    /// 短い Entry 名が複数の名前空間付き候補に一致すると曖昧指定として失敗することを検査します。
    /// </summary>
    [Fact(DisplayName = "短い Entry 名が複数名前空間候補に一致すると ENTRY_STEP_NOT_FOUND になる")]
    public void ExecuteShortEntryNameAmbiguityFailsWithEntryStepNotFound()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// Deploy 名前空間の Build Entry で実行する Step です。
            /// </summary>
            public sealed class DeployBuildStep : IStep<string>
            {
                /// <summary>
                /// Deploy Build の固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>Deploy Build の固定値。</returns>
                public string Execute(StepInput input) => "deploy";
            }

            /// <summary>
            /// Test 名前空間の Build Entry で実行する Step です。
            /// </summary>
            public sealed class TestBuildStep : IStep<string>
            {
                /// <summary>
                /// Test Build の固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>Test Build の固定値。</returns>
                public string Execute(StepInput input) => "test";
            }

            var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<DeployBuildStep, string>()
                    .StoreAs();
            var TestBuild = CompositeStep.Define("Build", namespaceName: "Test")
                .Run<TestBuildStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath, "Build");

        Assert.False(result.Succeeded);
        Assert.Equal("Build", result.EntryName);
        Assert.Equal(WorkflowErrorCodes.EntryStepNotFound, result.ErrorCode);
        Assert.Contains("multiple", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Deploy.Build", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Test.Build", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// load 先で定義された名前空間付き Entry を公開完全修飾名で実行できることを検査します。
    /// </summary>
    [Fact(DisplayName = "load 先の名前空間付き Entry は公開完全修飾名で実行できる")]
    public void ExecuteLoadedNamespaceEntryByQualifiedName()
    {
        string scriptPath = CreateScript(
            """
            #load "./deploy.csx"
            """,
            ("deploy.csx",
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// load 先の Deploy Build Entry で実行する Step です。
            /// </summary>
            public sealed class LoadedDeployBuildStep : IStep<string>
            {
                /// <summary>
                /// load 先の Deploy Build 固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>load 先の Deploy Build 固定値。</returns>
                public string Execute(StepInput input) => "loaded";
            }

            var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<LoadedDeployBuildStep, string>()
                    .StoreAs();
            """));

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath, "Deploy.Build");

        Assert.True(result.Succeeded);
        Assert.Equal("Deploy.Build", result.EntryName);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("LoadedDeployBuildStep", traceStep.StepName);
    }

    /// <summary>
    /// WithConfig と RunAsync の chain 後も名前空間付き Entry metadata が維持されることを検査します。
    /// </summary>
    [Fact(DisplayName = "WithConfig と RunAsync chain 後も名前空間付き Entry metadata は維持される")]
    public void ExecutePreservesNamespaceMetadataAfterConfigRunAndRunAsyncChain()
    {
        string scriptPath = CreateScript(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// chain 維持検査用 Config です。
            /// </summary>
            public sealed class ChainConfig
            {
                /// <summary>
                /// 検査用 title を取得または設定します。
                /// </summary>
                public string Title { get; set; } = "";
            }

            /// <summary>
            /// chain の先頭で固定値を返す Step です。
            /// </summary>
            public sealed class ChainFirstStep : IStep<int>
            {
                /// <summary>
                /// 固定の整数値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>固定の整数値。</returns>
                public int Execute(StepInput input) => 1;
            }

            /// <summary>
            /// chain の末尾で非同期に固定値を返す Step です。
            /// </summary>
            public sealed class ChainAsyncStep : IAsyncStep<string>
            {
                /// <summary>
                /// 固定の文字列値を非同期に返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <param name="cancellationToken">キャンセル通知。</param>
                /// <returns>固定の文字列値。</returns>
                public async Task<string> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
                {
                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();

                    return "chain";
                }
            }

            var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<ChainFirstStep, int>()
                .WithConfig<ChainConfig>()
                .RunAsync<ChainAsyncStep, string>()
                    .StoreAs();
            """);
        string configPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "appsettings.yaml");
        File.WriteAllText(configPath, "Title: chain");

        WorkflowResult result = new CsxEntryLoader().Execute(
            scriptPath,
            "Deploy.Build",
            new WorkflowExecutionOptions(engineArguments: new EngineArguments
            {
                EntryPath = scriptPath,
                ConfigPath = configPath,
            }));

        Assert.True(result.Succeeded);
        Assert.Equal("Deploy.Build", result.EntryName);
        Assert.Collection(
            result.Trace!.Steps,
            first => Assert.Equal("ChainFirstStep", first.StepName),
            second => Assert.Equal("ChainAsyncStep", second.StepName));
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
    /// 許可された NuGet 参照が lock file と fake provider だけで実行できることを確認します。
    /// </summary>
    [Fact(DisplayName = "許可された NuGet 参照は lock file と fake provider で実行できる")]
    public void AllowedNuGetReferenceCanExecuteWithLockFileAndFakeProvider()
    {
        string scriptPath = CreateScript(
            """
            #r "nuget: NodaTime, 3.1.11"
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class JsonStep : IStep<string>
            {
                public string Execute(StepInput input) => "locked";
            }

            var Main = CompositeStep.Define("Main")
                .Run<JsonStep, string>()
                    .StoreAs();
            """);
        WriteDefaultLockFile(scriptPath, "NodaTime", "3.1.11");
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            AllowedNuGetReferences = [new CsxNuGetReference("NodaTime", "3.1.11")],
            NuGetDependencyGraphProvider = new FakeNuGetDependencyGraphProvider(
                new CsxNuGetDependencyGraph(
                    [new CsxResolvedNuGetDependency("NodaTime", "3.1.11", isDirect: true)],
                    resolutionMetadata: CreateDefaultResolutionMetadata())),
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

    /// <summary>
    /// 標準名の NuGet lock file fixture を作成します。
    /// </summary>
    private static void WriteDefaultLockFile(string scriptPath, string packageId, string version)
    {
        string directory = Path.GetDirectoryName(scriptPath)!;
        string lockPath = Path.Combine(directory, DefaultNuGetLockFileName);
        File.WriteAllText(
            lockPath,
            $$"""
            version: 1
            entry: main.csx
            targetFramework: {{DefaultTargetFramework}}
            runtimeIdentifier: {{DefaultRuntimeIdentifier}}
            packageSources:
              - {{DefaultPackageSources[0]}}
            dotnetScriptCoreVersion: {{DefaultDotnetScriptCoreVersion}}
            directReferences:
              - packageId: {{packageId}}
                version: {{version}}
            resolvedDependencies:
              - packageId: {{packageId}}
                version: {{version}}
                direct: true
            """);
    }

    /// <summary>
    /// fake provider が返す標準の NuGet 解決 metadata を作成します。
    /// </summary>
    private static CsxNuGetResolutionMetadata CreateDefaultResolutionMetadata()
    {
        return new CsxNuGetResolutionMetadata(
            DefaultTargetFramework,
            DefaultRuntimeIdentifier,
            DefaultPackageSources,
            DefaultDotnetScriptCoreVersion);
    }

    /// <summary>
    /// 外部通信を使わずに固定 graph を返す NuGet dependency graph provider です。
    /// </summary>
    /// <param name="graph">返却する固定 NuGet dependency graph。</param>
    private sealed class FakeNuGetDependencyGraphProvider(CsxNuGetDependencyGraph graph) : ICsxNuGetDependencyGraphProvider
    {
        /// <summary>
        /// 固定された NuGet dependency graph を返します。
        /// </summary>
        /// <param name="directReferences">script から読んだ直接 NuGet 参照。</param>
        /// <param name="request">dependency graph 解決 request。</param>
        /// <returns>固定された NuGet dependency graph。</returns>
        public CsxNuGetDependencyGraph Resolve(
            IReadOnlyList<CsxNuGetReference> directReferences,
            CsxNuGetDependencyGraphRequest request)
        {
            return graph;
        }
    }
}
