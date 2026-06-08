using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Cli;
using Devo6.WorkFlow.Engine;
using System.Diagnostics;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// user-facing CLI run と validate command を検査します。
/// </summary>
public sealed class CliRunValidateTests
{
    /// <summary>
    /// repository root path を保持します。
    /// </summary>
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// engine run が有効な Main Entry で exit code 0 になることを検査します。
    /// </summary>
    [Fact(DisplayName = "engine run main.csx は成功時 exit code 0 になる")]
    public async Task EngineRunMainCsxは成功時ExitCode0になる()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                /// <summary>
                /// 固定値を返します。
                /// </summary>
                /// <param name="input">未使用の Step 入力。</param>
                /// <returns>固定文字列。</returns>
                public string Execute(StepInput input) => "main";
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """);

        CliResult result = await RunCliAsync("run", scriptPath);

        AssertSuccess(result);
    }

    /// <summary>
    /// engine validate が有効な Main Entry で exit code 0 になることを検査します。
    /// </summary>
    [Fact(DisplayName = "engine validate main.csx は成功時 exit code 0 になる")]
    public async Task EngineValidateMainCsxは成功時ExitCode0になる()
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

        CliResult result = await RunCliAsync("validate", scriptPath);

        AssertSuccess(result);
    }

    /// <summary>
    /// engine run が --entry で指定した Entry 名を使用することを検査します。
    /// </summary>
    [Fact(DisplayName = "engine run main.csx --entry Build は指定 Entry を実行する")]
    public async Task EngineRunMainCsxEntryBuildは指定Entryを実行する()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class BuildStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "build-ran.txt"), "Build");

                    return "build";
                }
            }

            var Build = CompositeStep.Define("Build")
                .Run<BuildStep, string>()
                    .StoreAs();
            """);
        string markerPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "build-ran.txt");

        CliResult result = await RunCliAsync("run", scriptPath, "--entry", "Build");

        AssertSuccess(result);
        Assert.Equal("Build", File.ReadAllText(markerPath));
    }

    /// <summary>
    /// engine validate が --entry で指定した Entry 名を使用することを検査します。
    /// </summary>
    [Fact(DisplayName = "engine validate main.csx --entry Build は指定 Entry を検証する")]
    public async Task EngineValidateMainCsxEntryBuildは指定Entryを検証する()
    {
        string scriptPath = CreateScript(
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
            """);

        CliResult result = await RunCliAsync("validate", scriptPath, "--entry", "Build");

        AssertSuccess(result);
    }

    /// <summary>
    /// CLI run が名前空間付き Entry を公開完全修飾名の --entry で実行できることを検査します。
    /// </summary>
    [Fact(DisplayName = "engine run main.csx --entry Deploy.Build は名前空間付き Entry を実行する")]
    public async Task EngineRunMainCsxEntryDeployBuildExecutesNamespaceEntry()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            /// <summary>
            /// Deploy 名前空間の Build Entry で実行する Step です。
            /// </summary>
            public sealed class DeployBuildStep : IStep<string>
            {
                /// <summary>
                /// Deploy Build の実行 marker を書き込みます。
                /// </summary>
                /// <param name="input">EngineArguments を取得する Step 入力。</param>
                /// <returns>Deploy Build の固定値。</returns>
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "deploy-build-ran.txt"), "Deploy.Build");

                    return "deploy";
                }
            }

            var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<DeployBuildStep, string>()
                    .StoreAs();
            """);
        string markerPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "deploy-build-ran.txt");

        CliResult result = await RunCliAsync("run", scriptPath, "--entry", "Deploy.Build");

        AssertSuccess(result);
        Assert.Contains("Succeeded: Deploy.Build", result.StandardOutput);
        Assert.Equal("Deploy.Build", File.ReadAllText(markerPath));
    }

    /// <summary>
    /// CLI validate が名前空間付き Entry を公開完全修飾名の --entry で検証できることを検査します。
    /// </summary>
    [Fact(DisplayName = "engine validate main.csx --entry Deploy.Build は名前空間付き Entry を検証する")]
    public async Task EngineValidateMainCsxEntryDeployBuildValidatesNamespaceEntry()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// Deploy 名前空間の Build Entry で検証対象にする Step です。
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

        CliResult result = await RunCliAsync("validate", scriptPath, "--entry", "Deploy.Build");

        AssertSuccess(result);
    }

    /// <summary>
    /// CLI run の短い --entry が名前空間付き候補 1 件へ互換解決できることを検査します。
    /// </summary>
    [Fact(DisplayName = "engine run main.csx --entry Build は名前空間付き候補が一意なら互換解決する")]
    public async Task EngineRunShortEntryBuildResolvesSingleNamespaceCandidate()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            /// <summary>
            /// Deploy 名前空間の Build Entry で実行する Step です。
            /// </summary>
            public sealed class DeployBuildStep : IStep<string>
            {
                /// <summary>
                /// Deploy Build の実行 marker を書き込みます。
                /// </summary>
                /// <param name="input">EngineArguments を取得する Step 入力。</param>
                /// <returns>Deploy Build の固定値。</returns>
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "short-build-ran.txt"), "Deploy.Build");

                    return "deploy";
                }
            }

            var DeployBuild = CompositeStep.Define("Build", namespaceName: "Deploy")
                .Run<DeployBuildStep, string>()
                    .StoreAs();
            """);
        string markerPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "short-build-ran.txt");

        CliResult result = await RunCliAsync("run", scriptPath, "--entry", "Build");

        AssertSuccess(result);
        Assert.Contains("Succeeded: Deploy.Build", result.StandardOutput);
        Assert.Equal("Deploy.Build", File.ReadAllText(markerPath));
    }

    /// <summary>
    /// CLI validate の短い --entry が複数名前空間候補に一致すると曖昧指定として失敗することを検査します。
    /// </summary>
    [Fact(DisplayName = "engine validate main.csx --entry Build は複数名前空間候補で ENTRY_STEP_NOT_FOUND になる")]
    public async Task EngineValidateShortEntryBuildFailsWhenNamespaceCandidatesAreAmbiguous()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            /// <summary>
            /// Deploy 名前空間の Build Entry で検証対象にする Step です。
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
            /// Test 名前空間の Build Entry で検証対象にする Step です。
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

        CliResult result = await RunCliAsync("validate", scriptPath, "--entry", "Build");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.EntryStepNotFound, result.StandardError);
        Assert.Contains("Deploy.Build", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Test.Build", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// --config が Entry .csx directory 基準で解決され EngineArguments から公開されることを検査します。
    /// </summary>
    [Fact(DisplayName = "--config は Entry directory 基準で解決され StepContext から取得できる")]
    public async Task ConfigはEntryDirectory基準で解決されStepContextから取得できる()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class ArgumentsStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "config-path.txt"), arguments.ConfigPath);

                    return arguments.ConfigPath;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<ArgumentsStep, string>()
                    .StoreAs();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        string configPath = Path.Combine(directory, "appsettings.yaml");
        File.WriteAllText(configPath, "name: test");

        CliResult result = await RunCliAsync("run", scriptPath, "--config", "appsettings.yaml");

        AssertSuccess(result);
        Assert.Equal(configPath, File.ReadAllText(Path.Combine(directory, "config-path.txt")));
    }

    /// <summary>
    /// 複数の --set 値が EngineArguments から文字列として公開されることを検査します。
    /// </summary>
    [Fact(DisplayName = "複数 --set は文字列として StepContext から取得できる")]
    public async Task 複数Setは文字列としてStepContextから取得できる()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;
            using System.Linq;

            public sealed class ArgumentsStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    string text = string.Join("|", arguments.Settings.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "settings.txt"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<ArgumentsStep, string>()
                    .StoreAs();
            """);

        CliResult result = await RunCliAsync(
            "run",
            scriptPath,
            "--set",
            "convert.toUpper=false",
            "--set",
            "save.path=out.txt");

        AssertSuccess(result);
        Assert.Equal("convert.toUpper=false|save.path=out.txt", File.ReadAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, "settings.txt")));
    }

    /// <summary>
    /// Config 型を見ない --set 無効書式が command error になることを検査します。
    /// </summary>
    /// <param name="setArgument">CLI 解析で拒否される --set 引数。</param>
    [Theory(DisplayName = "Config 型を見ない --set 無効書式は exit code 2 になる")]
    [InlineData("=value")]
    [InlineData("key")]
    public async Task InvalidSetSyntaxFailsWithCommandErrorExitCode2(string setArgument)
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

        CliResult result = await RunCliAsync("run", scriptPath, "--set", setArgument);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--set", result.StandardError);
    }

    /// <summary>
    /// CLI run が --allow-nuget の固定参照を loader options に渡すことを検査します。
    /// </summary>
    [Fact(DisplayName = "engine run は --allow-nuget の固定参照を許可する")]
    public void EngineRunAllowNuGetReferenceFromOption()
    {
        string scriptPath = CreateNuGetScript();
        var provider = new FakeNuGetDependencyGraphProvider(CreateWorkflowPackageGraph());

        int exitCode = Program.Run(
            ["run", scriptPath, "--allow-nuget", " Devo6.WorkFlow.Engine, 0.1.0 "],
            provider);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// CLI validate が --allow-nuget の固定参照を loader options に渡すことを検査します。
    /// </summary>
    [Fact(DisplayName = "engine validate は --allow-nuget の固定参照を許可する")]
    public void EngineValidateAllowNuGetReferenceFromOption()
    {
        string scriptPath = CreateNuGetScript();
        var provider = new FakeNuGetDependencyGraphProvider(CreateWorkflowPackageGraph());

        int exitCode = Program.Run(
            ["validate", scriptPath, "--allow-nuget", "Devo6.WorkFlow.Engine,0.1.0"],
            provider);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// CLI run が --allow-nuget なしで固定 NuGet 参照を通常解決へ渡すことを検査します。
    /// </summary>
    [Fact(DisplayName = "engine run は --allow-nuget なしで NuGet 参照を許可する")]
    public void EngineRunAcceptsNuGetReferenceWithoutAllowNuGetOption()
    {
        string scriptPath = CreateNuGetScript();
        var provider = new FakeNuGetDependencyGraphProvider(CreateWorkflowPackageGraph());

        int exitCode = Program.Run(["run", scriptPath], provider);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// CLI validate が --allow-nuget なしで固定 NuGet 参照を通常解決へ渡すことを検査します。
    /// </summary>
    [Fact(DisplayName = "engine validate は --allow-nuget なしで NuGet 参照を許可する")]
    public void EngineValidateAcceptsNuGetReferenceWithoutAllowNuGetOption()
    {
        string scriptPath = CreateNuGetScript();
        var provider = new FakeNuGetDependencyGraphProvider(CreateWorkflowPackageGraph());

        int exitCode = Program.Run(["validate", scriptPath], provider);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// CLI run の --locked が NuGet lock file 欠落を実行前失敗にすることを検査します。
    /// </summary>
    [Fact(DisplayName = "engine run --locked は NuGet lock file 欠落を失敗にする")]
    public void EngineRunLockedRequiresNuGetLockFile()
    {
        string scriptPath = CreateNuGetScript();
        var provider = new FakeNuGetDependencyGraphProvider(CreateWorkflowPackageGraph());

        int exitCode = Program.Run(
            ["run", scriptPath, "--locked"],
            provider);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// CLI validate の --locked が NuGet lock file 欠落を検証失敗にすることを検査します。
    /// </summary>
    [Fact(DisplayName = "engine validate --locked は NuGet lock file 欠落を失敗にする")]
    public void EngineValidateLockedRequiresNuGetLockFile()
    {
        string scriptPath = CreateNuGetScript();
        var provider = new FakeNuGetDependencyGraphProvider(CreateWorkflowPackageGraph());

        int exitCode = Program.Run(
            ["validate", scriptPath, "--locked"],
            provider);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// CLI が不正な --allow-nuget 値を command error として返すことを検査します。
    /// </summary>
    [Fact(DisplayName = "不正な --allow-nuget 値は exit code 2 になる")]
    public void InvalidAllowNuGetSyntaxFailsWithCommandErrorExitCode2()
    {
        string scriptPath = CreateNuGetScript();

        int exitCode = Program.Run(["validate", scriptPath, "--allow-nuget", "Devo6.WorkFlow.Engine"]);

        Assert.Equal(2, exitCode);
    }

    /// <summary>
    /// run と validate の失敗が 0 以外の exit code を返すことを検査します。
    /// </summary>
    [Fact(DisplayName = "validate と run の失敗時は exit code が 0 以外になる")]
    public async Task ValidateとRunの失敗時はExitCodeが0以外になる()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Engine;

            var Main = CompositeStep.Define("Main")
                .Run<MissingStep, string>()
                    .StoreAs();
            """);

        CliResult validateResult = await RunCliAsync("validate", scriptPath);
        CliResult runResult = await RunCliAsync("run", scriptPath);

        Assert.NotEqual(0, validateResult.ExitCode);
        Assert.NotEqual(0, runResult.ExitCode);
    }

    /// <summary>
    /// CLI process を指定引数で実行します。
    /// </summary>
    /// <param name="arguments">CLI に渡す引数。</param>
    /// <returns>CLI process の実行結果。</returns>
    private static async Task<CliResult> RunCliAsync(params string[] arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = RepositoryRoot,
            ArgumentList =
            {
                "run",
                "--project",
                Path.Combine(RepositoryRoot, "src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj"),
                "--configuration",
                TestBuildConfiguration.Current,
                "--no-build",
                "--",
            },
        }.AddArguments(arguments)) ?? throw new InvalidOperationException("CLI process could not be started.");

        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CliResult(process.ExitCode, standardOutput, standardError);
    }

    /// <summary>
    /// CLI result が成功していることを検査します。
    /// </summary>
    /// <param name="result">検査対象の CLI result。</param>
    private static void AssertSuccess(CliResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"終了コード: {result.ExitCode}{Environment.NewLine}標準出力: {result.StandardOutput}{Environment.NewLine}標準エラー: {result.StandardError}");
    }

    /// <summary>
    /// 一時 directory に main.csx を作成します。
    /// </summary>
    /// <param name="contents">main.csx に書き込む内容。</param>
    /// <returns>作成した script path。</returns>
    private static string CreateScript(string contents)
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-workflow-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "main.csx");
        File.WriteAllText(scriptPath, contents);

        return scriptPath;
    }

    /// <summary>
    /// NuGet 参照を含む一時 workflow script を作成します。
    /// </summary>
    /// <returns>作成した script path。</returns>
    private static string CreateNuGetScript()
    {
        return CreateScript(
            """
            #r "nuget: Devo6.WorkFlow.Engine, 0.1.0"
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
    }

    /// <summary>
    /// NuGet 参照用の標準 lock file fixture を作成します。
    /// </summary>
    /// <param name="scriptPath">entry script path。</param>
    private static void WriteNuGetLockFile(string scriptPath)
    {
        string lockPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "devo6.nuget.lock.yaml");
        File.WriteAllText(
            lockPath,
            """
            version: 1
            entry: main.csx
            targetFramework: net8.0
            runtimeIdentifier: ubuntu.24.04-x64
            packageSources:
              - https://api.nuget.org/v3/index.json
            dotnetScriptCoreVersion: 2.0.1
            directReferences:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
            resolvedDependencies:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
              - packageId: Dotnet.Script.Core
                version: 2.0.1
                direct: false
              - packageId: Dotnet.Script.DependencyModel
                version: 2.0.1
                direct: false
              - packageId: Dotnet.Script.DependencyModel.NuGet
                version: 2.0.1
                direct: false
              - packageId: Gapotchenko.FX
                version: 2024.2.5
                direct: false
              - packageId: Gapotchenko.FX.Reflection.Loader
                version: 2024.2.5
                direct: false
              - packageId: Microsoft.CodeAnalysis.Common
                version: 5.0.0-2.final
                direct: false
              - packageId: Microsoft.CodeAnalysis.CSharp
                version: 5.0.0-2.final
                direct: false
              - packageId: Microsoft.CodeAnalysis.CSharp.Scripting
                version: 5.0.0-2.final
                direct: false
              - packageId: Microsoft.CodeAnalysis.Scripting.Common
                version: 5.0.0-2.final
                direct: false
              - packageId: Microsoft.DotNet.PlatformAbstractions
                version: 3.1.6
                direct: false
              - packageId: Microsoft.Extensions.DependencyInjection.Abstractions
                version: 8.0.0
                direct: false
              - packageId: Microsoft.Extensions.Logging.Abstractions
                version: 8.0.0
                direct: false
              - packageId: Microsoft.NETCore.App
                version: 8.0.27
                direct: false
              - packageId: Newtonsoft.Json
                version: 13.0.3
                direct: false
              - packageId: NuGet.Common
                version: 6.14.3
                direct: false
              - packageId: NuGet.Configuration
                version: 6.14.3
                direct: false
              - packageId: NuGet.DependencyResolver.Core
                version: 6.14.3
                direct: false
              - packageId: NuGet.Frameworks
                version: 6.14.3
                direct: false
              - packageId: NuGet.LibraryModel
                version: 6.14.3
                direct: false
              - packageId: NuGet.Packaging
                version: 6.14.3
                direct: false
              - packageId: NuGet.ProjectModel
                version: 6.14.3
                direct: false
              - packageId: NuGet.Protocol
                version: 6.14.3
                direct: false
              - packageId: NuGet.Versioning
                version: 6.14.3
                direct: false
              - packageId: ReadLine
                version: 2.0.1
                direct: false
              - packageId: System.Collections.Immutable
                version: 9.0.0
                direct: false
              - packageId: System.Formats.Asn1
                version: 6.0.0
                direct: false
              - packageId: System.Reflection.Metadata
                version: 9.0.0
                direct: false
              - packageId: System.Security.Cryptography.Pkcs
                version: 6.0.4
                direct: false
              - packageId: System.Security.Cryptography.ProtectedData
                version: 4.4.0
                direct: false
              - packageId: YamlDotNet
                version: 16.3.0
                direct: false
            """);
    }

    /// <summary>
    /// 検査用 workflow package の固定 NuGet graph を作成します。
    /// </summary>
    /// <returns>固定 NuGet graph。</returns>
    private static CsxNuGetDependencyGraph CreateWorkflowPackageGraph()
    {
        return new CsxNuGetDependencyGraph(
            CreateWorkflowPackageDependencies(),
            referencePaths:
            [
                typeof(CompositeStep).Assembly.Location,
                typeof(IStep<>).Assembly.Location,
            ],
            resolutionMetadata: new CsxNuGetResolutionMetadata(
                "net8.0",
                "ubuntu.24.04-x64",
                ["https://api.nuget.org/v3/index.json"],
                "2.0.1"));
    }

    /// <summary>
    /// 検査用 workflow package の固定 NuGet 依存関係を作成します。
    /// </summary>
    /// <returns>固定 NuGet 依存関係。</returns>
    private static CsxResolvedNuGetDependency[] CreateWorkflowPackageDependencies()
    {
        return
        [
            new("Devo6.WorkFlow.Engine", "0.1.0", isDirect: true),
            new("Dotnet.Script.Core", "2.0.1", isDirect: false),
            new("Dotnet.Script.DependencyModel", "2.0.1", isDirect: false),
            new("Dotnet.Script.DependencyModel.NuGet", "2.0.1", isDirect: false),
            new("Gapotchenko.FX", "2024.2.5", isDirect: false),
            new("Gapotchenko.FX.Reflection.Loader", "2024.2.5", isDirect: false),
            new("Microsoft.CodeAnalysis.Common", "5.0.0-2.final", isDirect: false),
            new("Microsoft.CodeAnalysis.CSharp", "5.0.0-2.final", isDirect: false),
            new("Microsoft.CodeAnalysis.CSharp.Scripting", "5.0.0-2.final", isDirect: false),
            new("Microsoft.CodeAnalysis.Scripting.Common", "5.0.0-2.final", isDirect: false),
            new("Microsoft.DotNet.PlatformAbstractions", "3.1.6", isDirect: false),
            new("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0", isDirect: false),
            new("Microsoft.Extensions.Logging.Abstractions", "8.0.0", isDirect: false),
            new("Microsoft.NETCore.App", "8.0.27", isDirect: false),
            new("Newtonsoft.Json", "13.0.3", isDirect: false),
            new("NuGet.Common", "6.14.3", isDirect: false),
            new("NuGet.Configuration", "6.14.3", isDirect: false),
            new("NuGet.DependencyResolver.Core", "6.14.3", isDirect: false),
            new("NuGet.Frameworks", "6.14.3", isDirect: false),
            new("NuGet.LibraryModel", "6.14.3", isDirect: false),
            new("NuGet.Packaging", "6.14.3", isDirect: false),
            new("NuGet.ProjectModel", "6.14.3", isDirect: false),
            new("NuGet.Protocol", "6.14.3", isDirect: false),
            new("NuGet.Versioning", "6.14.3", isDirect: false),
            new("ReadLine", "2.0.1", isDirect: false),
            new("System.Collections.Immutable", "9.0.0", isDirect: false),
            new("System.Formats.Asn1", "6.0.0", isDirect: false),
            new("System.Reflection.Metadata", "9.0.0", isDirect: false),
            new("System.Security.Cryptography.Pkcs", "6.0.4", isDirect: false),
            new("System.Security.Cryptography.ProtectedData", "4.4.0", isDirect: false),
            new("YamlDotNet", "16.3.0", isDirect: false),
        ];
    }

    /// <summary>
    /// repository root path を探索します。
    /// </summary>
    /// <returns>検出した repository root path。</returns>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Devo6.WorkFlow.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("repository root を特定できませんでした。");
    }

    /// <summary>
    /// CLI process の実行結果です。
    /// </summary>
    /// <param name="ExitCode">CLI process の exit code。</param>
    /// <param name="StandardOutput">CLI process の標準出力。</param>
    /// <param name="StandardError">CLI process の標準エラー。</param>
    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// 外部通信を使わずに固定 NuGet graph を返す provider です。
    /// </summary>
    /// <param name="graph">返却する固定 NuGet graph。</param>
    private sealed class FakeNuGetDependencyGraphProvider(CsxNuGetDependencyGraph graph) : ICsxNuGetDependencyGraphProvider
    {
        /// <summary>
        /// Resolve が呼び出された回数を取得します。
        /// </summary>
        public int ResolveCallCount { get; private set; }

        /// <summary>
        /// 固定 NuGet graph を返します。
        /// </summary>
        /// <param name="directReferences">script から読んだ直接 NuGet 参照。</param>
        /// <param name="request">dependency graph 解決 request。</param>
        /// <returns>固定 NuGet graph。</returns>
        public CsxNuGetDependencyGraph Resolve(
            IReadOnlyList<CsxNuGetReference> directReferences,
            CsxNuGetDependencyGraphRequest request)
        {
            ResolveCallCount++;

            return graph;
        }
    }
}

/// <summary>
/// CLI integration test 用の ProcessStartInfo helper を提供します。
/// </summary>
internal static class ProcessStartInfoExtensions
{
    /// <summary>
    /// CLI integration test で使う process start info に command-line 引数を追加します。
    /// </summary>
    /// <param name="startInfo">更新する process start info。</param>
    /// <param name="arguments">追加する引数。</param>
    /// <returns>同じ process start info instance。</returns>
    public static ProcessStartInfo AddArguments(this ProcessStartInfo startInfo, IEnumerable<string> arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
