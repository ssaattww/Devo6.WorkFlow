using Devo6.WorkFlow.Abstractions;
using System.Diagnostics;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// Verifies the user-facing CLI run and validate commands.
/// </summary>
public sealed class CliRunValidateTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// Verifies that engine run exits with 0 for a valid Main entry.
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
    /// Verifies that engine validate exits with 0 for a valid Main entry.
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
    /// Verifies that engine run uses the entry name supplied by --entry.
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
    /// Verifies that engine validate uses the entry name supplied by --entry.
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
    /// Verifies that --config is resolved relative to the entry .csx directory and exposed through EngineArguments.
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
    /// Verifies that repeated --set values are exposed as strings through EngineArguments.
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
    /// Verifies that run and validate failures return non-zero exit codes.
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
                "--no-build",
                "--",
            },
        }.AddArguments(arguments)) ?? throw new InvalidOperationException("CLI process could not be started.");

        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CliResult(process.ExitCode, standardOutput, standardError);
    }

    private static void AssertSuccess(CliResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"終了コード: {result.ExitCode}{Environment.NewLine}標準出力: {result.StandardOutput}{Environment.NewLine}標準エラー: {result.StandardError}");
    }

    private static string CreateScript(string contents)
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-workflow-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "main.csx");
        File.WriteAllText(scriptPath, contents);

        return scriptPath;
    }

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

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
}

/// <summary>
/// Provides small process start info helpers for CLI integration tests.
/// </summary>
internal static class ProcessStartInfoExtensions
{
    /// <summary>
    /// Appends command-line arguments to a process start info instance used by CLI integration tests.
    /// </summary>
    /// <param name="startInfo">The process start info to update.</param>
    /// <param name="arguments">The arguments to append.</param>
    /// <returns>The same process start info instance.</returns>
    public static ProcessStartInfo AddArguments(this ProcessStartInfo startInfo, IEnumerable<string> arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
