using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Cli;
using Devo6.WorkFlow.Engine;
using System.Diagnostics;
using System.Text.RegularExpressions;

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
    /// 引数が無い場合の help 表示に engine defaults YAML の実行時解決済み完全パスが含まれることを検査します。
    /// </summary>
    [Fact(DisplayName = "engine 引数なしヘルプはエンジン既定 YAML の解決済み完全パスを表示する")]
    public async Task EngineNoArgsHelpにエンジン既定YAMLの完全パスを表示する()
    {
        CliResult result = await RunCliAsync();

        string defaultsPath = ExtractEngineDefaultsPath(result.StandardOutput);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("engine.defaults.yaml", defaultsPath);
        Assert.Contains(Path.Combine("config", "engine.defaults.yaml"), defaultsPath);
        Assert.True(Path.IsPathFullyQualified(defaultsPath));
    }

    /// <summary>
    /// help コマンド実行時に引数なしヘルプと同じ engine defaults YAML 解決済み完全パスを表示することを検査します。
    /// </summary>
    [Fact(DisplayName = "engine help は引数なし help と同じエンジン既定 YAML path を表示する")]
    public async Task EngineHelpCommandで引数なしHelpと同じ完全パスを表示する()
    {
        CliResult result = await RunCliAsync("help");
        string defaultsPath = ExtractEngineDefaultsPath(result.StandardOutput);

        Assert.Equal(0, result.ExitCode);
        Assert.True(Path.IsPathFullyQualified(defaultsPath));
        Assert.EndsWith(Path.Combine("config", "engine.defaults.yaml"), defaultsPath, StringComparison.Ordinal);
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
    /// --workflow-config が Entry .csx directory 基準で解決され EngineArguments から公開されることを検査します。
    /// </summary>
    [Fact(DisplayName = "--workflow-config は Entry directory 基準で解決され StepContext から取得できる")]
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
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "config-path.txt"), arguments.WorkflowConfigPath);

                    return arguments.WorkflowConfigPath;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<ArgumentsStep, string>()
                    .StoreAs();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        string configPath = Path.Combine(directory, "appsettings.yaml");
        File.WriteAllText(configPath, "name: test");

        CliResult result = await RunCliAsync("run", scriptPath, "--workflow-config", "appsettings.yaml");

        AssertSuccess(result);
        Assert.Equal(configPath, File.ReadAllText(Path.Combine(directory, "config-path.txt")));
    }

    /// <summary>
    /// 複数の --workflow-set 値が EngineArguments から文字列として公開されることを検査します。
    /// </summary>
    [Fact(DisplayName = "複数 --workflow-set は文字列として StepContext から取得できる")]
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
                    string text = string.Join("|", arguments.WorkflowSettings.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
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
            "--workflow-set",
            "convert.toUpper=false",
            "--workflow-set",
            "save.path=out.txt");

        AssertSuccess(result);
        Assert.Equal("convert.toUpper=false|save.path=out.txt", File.ReadAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, "settings.txt")));
    }

    /// <summary>
    /// --wset は --workflow-set と同じ扱いで WorkflowSettings へ格納されることを検査します。
    /// </summary>
    [Fact(DisplayName = "--wset は --workflow-set と同じ扱いで WorkflowSettings へ格納される")]
    public async Task WsetAliasStoresWorkflowSettings()
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
                    string text = string.Join("|", arguments.WorkflowSettings.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
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
            "--wset",
            "convert.toUpper=false",
            "--wset",
            "save.path=out.txt");

        AssertSuccess(result);
        Assert.Equal("convert.toUpper=false|save.path=out.txt", File.ReadAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, "settings.txt")));
    }

    /// <summary>
    /// --engine-config が Entry directory 基準で解決され StepContext から公開されることを検査します。
    /// </summary>
    [Fact(DisplayName = "--engine-config は Entry directory 基準で解決され StepContext から取得できる")]
    public async Task EngineConfigIsResolvedFromEntryDirectoryAndAvailableFromStepContext()
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
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "config-path.txt"), arguments.EngineConfigPath);

                    return arguments.EngineConfigPath;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<ArgumentsStep, string>()
                    .StoreAs();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        string engineConfigPath = Path.Combine(directory, "engine.yaml");
        File.WriteAllText(engineConfigPath, "logging: enabled");

        CliResult result = await RunCliAsync("run", scriptPath, "--engine-config", "engine.yaml");

        AssertSuccess(result);
        Assert.Equal(engineConfigPath, File.ReadAllText(Path.Combine(directory, "config-path.txt")));
    }

    /// <summary>
    /// 絶対 path の Entry を指定した run と validate が Entry directory 基準で config と local load を解決することを検査します。
    /// </summary>
    [Fact(DisplayName = "絶対 Entry path の run と validate は Entry directory 基準で config と load を解決する")]
    public async Task AbsoluteEntryPathRunAndValidateResolveConfigAndLocalLoadFromEntryDirectory()
    {
        string scriptPath = CreateScript("");
        string directory = Path.GetDirectoryName(scriptPath)!;
        string loadedPath = Path.Combine(directory, "steps", "build.csx");
        Directory.CreateDirectory(Path.GetDirectoryName(loadedPath)!);
        File.WriteAllText(
            loadedPath,
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class ArgumentsStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    string text = $"{arguments.EntryPath}|{arguments.WorkflowConfigPath}|{arguments.EngineConfigPath}";
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "absolute-entry.txt"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<ArgumentsStep, string>()
                    .StoreAs();
            """);
        File.WriteAllText(
            scriptPath,
            $$"""
            #load "{{loadedPath}}"
            """);
        string workflowConfigPath = Path.Combine(directory, "appsettings.yaml");
        string engineConfigPath = Path.Combine(directory, "engine.yaml");
        File.WriteAllText(workflowConfigPath, "name: test");
        File.WriteAllText(
            engineConfigPath,
            """
            Logging:
              Console:
                Enabled: false
            """);

        CliResult validateResult = await RunCliAsync("validate", scriptPath, "--workflow-config", "appsettings.yaml", "--engine-config", "engine.yaml");
        CliResult runResult = await RunCliAsync("run", scriptPath, "--workflow-config", "appsettings.yaml", "--engine-config", "engine.yaml");

        AssertSuccess(validateResult);
        AssertSuccess(runResult);
        Assert.Equal($"{scriptPath}|{workflowConfigPath}|{engineConfigPath}", File.ReadAllText(Path.Combine(directory, "absolute-entry.txt")));
    }

    /// <summary>
    /// --engine-set と --eset は EngineSettings へ文字列として格納されることを検査します。
    /// </summary>
    [Fact(DisplayName = "--engine-set と --eset は EngineSettings に文字列として保存される")]
    public async Task EngineSetAliasStoresEngineSettings()
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
                    string text = string.Join("|", arguments.EngineSettings.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
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
            "--engine-set",
            "Logging.Console.Enabled=true",
            "--eset",
            "Logging.File.Format=Json");

        AssertSuccess(result);
        Assert.Equal("Logging.Console.Enabled=true|Logging.File.Format=Json", File.ReadAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, "settings.txt")));
    }

    /// <summary>
    /// ログファイル設定を YAML の Logging.File から有効化すると、実行ログが EntryName を含むファイル名で出力されることを検証します。
    /// </summary>
    [Fact(DisplayName = "engine config の Logging.File 設定でファイルログを有効化すると Entry ログファイルが作成される")]
    public async Task EngineRunCreatesFileLogWhenFileLoggingEnabledInConfig()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => "main";
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        string engineConfigPath = Path.Combine(directory, "engine.yaml");
        File.WriteAllText(
            engineConfigPath,
            """
            Logging:
              File:
                Enabled: true
                Directory: logs
                NameFormat: "{Timestamp:yyMMdd-HHmmss}_{RootStepName}.log"
              Console:
                Enabled: false
            """);

        CliResult result = await RunCliAsync("run", scriptPath, "--engine-config", "engine.yaml");

        AssertSuccess(result);
        string logsDirectory = Path.Combine(directory, "logs");
        string[] logFiles = Directory.GetFiles(logsDirectory, "*_Main.log");
        Assert.Single(logFiles);
        string content = File.ReadAllText(logFiles[0]);
        Assert.Contains("Entry started", content, StringComparison.Ordinal);
        Assert.Contains("Entry succeeded", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// --engine-set によって File ログ設定を有効化するとログファイルを作成できることを検証します。
    /// </summary>
    [Fact(DisplayName = "--engine-set で File.Enabled=true と File.Directory=logs を指定するとログファイルが作成される")]
    public async Task EngineRunWritesFileLogWhenFileLoggingEnabledByEngineSet()
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

        CliResult result = await RunCliAsync(
            "run",
            scriptPath,
            "--engine-set",
            "Logging.File.Enabled=true",
            "--engine-set",
            "Logging.File.Directory=logs");

        AssertSuccess(result);

        string directory = Path.GetDirectoryName(scriptPath)!;
        string logsDirectory = Path.Combine(directory, "logs");
        string[] logFiles = Directory.GetFiles(logsDirectory, "*_Main.log");
        Assert.Single(logFiles);
    }

    /// <summary>
    /// Logging.Console.Enabled=true のとき、CLI run の標準出力に engine/step のログ本文が含まれることを検証します。
    /// </summary>
    [Fact(DisplayName = "Logging.Console.Enabled=true のとき CLI 出力にエンジンログが含まれる")]
    public async Task EngineRunWritesEngineLogsToConsoleWhenConsoleEnabled()
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

        CliResult result = await RunCliAsync(
            "run",
            scriptPath,
            "--engine-set",
            "Logging.Console.Enabled=true");

        AssertSuccess(result);
        Assert.Contains("Entry started", result.StandardOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// 未知の Logging ファイル形式を engine-set で指定すると run が CONFIG_LOAD_FAILED で失敗することを検証します。
    /// </summary>
    [Fact(DisplayName = "Logging.File.Format に未対応の値を指定すると run が CONFIG_LOAD_FAILED になる")]
    public async Task EngineRunFailsWithUnsupportedLoggingFileFormat()
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

        CliResult result = await RunCliAsync(
            "run",
            scriptPath,
            "--engine-set",
            "Logging.File.Format=Bad");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.ConfigLoadFailed, result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// engine config の Logging セクションに未知キーを含めると run が CONFIG_LOAD_FAILED で失敗することを検証します。
    /// </summary>
    [Fact(DisplayName = "engine config の Logging で未知キーを使うと CONFIG_LOAD_FAILED になる")]
    public async Task EngineRunFailsWithUnsupportedLoggingConfigPath()
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
        string directory = Path.GetDirectoryName(scriptPath)!;
        string engineConfigPath = Path.Combine(directory, "engine.yaml");
        File.WriteAllText(
            engineConfigPath,
            """
            Logging:
              File:
                Unknown: true
            """);

        CliResult result = await RunCliAsync("run", scriptPath, "--engine-config", "engine.yaml");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.ConfigLoadFailed, result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// --engine-set に未知のトップレベル path を指定すると run が CONFIG_LOAD_FAILED で失敗することを検証します。
    /// </summary>
    [Fact(DisplayName = "--engine-set の未知トップレベル path は CONFIG_LOAD_FAILED になる")]
    public async Task EngineRunFailsWithUnsupportedEngineSetRootPath()
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

        CliResult result = await RunCliAsync("run", scriptPath, "--engine-set", "Typo.Value=1");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.ConfigLoadFailed, result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// --engine-set / --eset のドットなし未知 path と section 直接指定は CONFIG_LOAD_FAILED で失敗することを検証します。
    /// </summary>
    /// <param name="option">engine 設定 CLI オプション。</param>
    /// <param name="setting">拒否される engine 設定。</param>
    [Theory(DisplayName = "engine-set のドットなし未知 path と section 直接指定は CONFIG_LOAD_FAILED になる")]
    [InlineData("--engine-set", "Typo=1")]
    [InlineData("--engine-set", "Retry=1")]
    [InlineData("--eset", "Logging=Json")]
    public async Task EngineRunFailsWithUnsupportedEngineSetTopLevelSetting(string option, string setting)
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

        CliResult result = await RunCliAsync("run", scriptPath, option, setting);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.ConfigLoadFailed, result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// engine config の未知トップレベル section は CONFIG_LOAD_FAILED で失敗することを検証します。
    /// </summary>
    [Fact(DisplayName = "engine config の未知トップレベル section は CONFIG_LOAD_FAILED になる")]
    public async Task EngineRunFailsWithUnsupportedEngineConfigRootPath()
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
        string directory = Path.GetDirectoryName(scriptPath)!;
        string engineConfigPath = Path.Combine(directory, "engine.yaml");
        File.WriteAllText(
            engineConfigPath,
            """
            Typo:
              Value: 1
            """);

        CliResult result = await RunCliAsync("run", scriptPath, "--engine-config", "engine.yaml");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.ConfigLoadFailed, result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// engine config の既知 section 内に未知キーを含めると CONFIG_LOAD_FAILED で失敗することを検証します。
    /// </summary>
    [Fact(DisplayName = "engine config の既知 section 内未知キーは CONFIG_LOAD_FAILED になる")]
    public async Task EngineRunFailsWithUnsupportedEngineConfigNestedPath()
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
        string directory = Path.GetDirectoryName(scriptPath)!;
        string engineConfigPath = Path.Combine(directory, "engine.yaml");
        File.WriteAllText(
            engineConfigPath,
            """
            Retry:
              Unknown: 1
            """);

        CliResult result = await RunCliAsync("run", scriptPath, "--engine-config", "engine.yaml");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.ConfigLoadFailed, result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// --engine-set の Timeout.StepTimeout が CLI 実行時の step timeout として適用され、タイムアウトで失敗することを検証します。
    /// </summary>
    [Fact(DisplayName = "engine set の Timeout.StepTimeout で step timeout を上書きすると timeout 失敗する")]
    public async Task EngineRunUsesTimeoutFromEngineSet()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class SlowAsyncStep : IAsyncStep<string>
            {
                public async Task<string> ExecuteAsync(StepInput input, CancellationToken cancellationToken)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                    return "slow";
                }
            }

            var Main = CompositeStep.Define("Main")
                .RunAsync<SlowAsyncStep, string>()
                    .StoreAs();
            """);

        CliResult result = await RunCliAsync("run", scriptPath, "--engine-set", "Timeout.StepTimeout=00:00:00.030");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.StepTimeout, result.StandardError);
    }

    /// <summary>
    /// engine YAML の Retry.MaxAttempts を 3 にした場合、2 回失敗後 3 回目で成功することを検証します。
    /// </summary>
    [Fact(DisplayName = "engine-config の Retry.MaxAttempts=3 で step が 2 回失敗して 3 回目で成功する")]
    public async Task EngineRunUsesRetryFromEngineConfig()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;
            using System;
            using System.Threading;

            public sealed class RetryState
            {
                public static int Attempts;
            }

            public sealed class RetryStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    int attempt = Interlocked.Increment(ref RetryState.Attempts);
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "attempt.txt"), attempt.ToString());

                    if (attempt < 3)
                    {
                        throw new InvalidOperationException($"attempt:{attempt}");
                    }

                    return "ok";
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<RetryStep, string>()
                    .StoreAs();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        string engineConfigPath = Path.Combine(directory, "engine.yaml");
        File.WriteAllText(engineConfigPath, "Retry:\n  MaxAttempts: 3");

        CliResult result = await RunCliAsync("run", scriptPath, "--engine-config", "engine.yaml");

        AssertSuccess(result);
        Assert.Equal("3", File.ReadAllText(Path.Combine(directory, "attempt.txt")));
    }

    /// <summary>
    /// `--eset Retry.MaxAttempts=1` が engine-config の 3 を上書きし、再試行せず失敗することを検証します。
    /// </summary>
    [Fact(DisplayName = "engine-set の Retry.MaxAttempts=1 で engine-config の Retry.MaxAttempts=3 を上書きする")]
    public async Task EngineRunOverwritesEngineConfigRetryWithEngineSet()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;
            using System;
            using System.Threading;

            public sealed class RetryState
            {
                public static int Attempts;
            }

            public sealed class RetryStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    int attempt = Interlocked.Increment(ref RetryState.Attempts);
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "attempt.txt"), attempt.ToString());

                    if (attempt < 3)
                    {
                        throw new InvalidOperationException($"attempt:{attempt}");
                    }

                    return "ok";
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<RetryStep, string>()
                    .StoreAs();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        string engineConfigPath = Path.Combine(directory, "engine.yaml");
        File.WriteAllText(engineConfigPath, "Retry:\n  MaxAttempts: 3");

        CliResult result = await RunCliAsync(
            "run",
            scriptPath,
            "--engine-config",
            "engine.yaml",
            "--eset",
            "Retry.MaxAttempts=1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.StepExecutionFailed, result.StandardError);
        Assert.Equal("1", File.ReadAllText(Path.Combine(directory, "attempt.txt")));
    }

    /// <summary>
    /// Config 型を見ない --workflow-set 無効書式が command error になることを検査します。
    /// </summary>
    /// <param name="setArgument">CLI 解析で拒否される --workflow-set 引数。</param>
    [Theory(DisplayName = "Config 型を見ない --workflow-set 無効書式は exit code 2 になる")]
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

        CliResult result = await RunCliAsync("run", scriptPath, "--workflow-set", setArgument);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("key=value", result.StandardError);
    }

    /// <summary>
    /// old --config と --set は CLI 解析で受け付けられないことを検査します。
    /// </summary>
    /// <param name="option">旧オプション名。</param>
    /// <param name="value">旧オプションに対応する値。</param>
    [Theory(DisplayName = "旧 --config と --set は command error になる")]
    [InlineData("--config", "appsettings.yaml")]
    [InlineData("--set", "Convert.ToUpper=false")]
    public void LegacyOptionsAreRejected(string option, string value)
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => "main";
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """);

        int exitCode = Program.Run(["run", scriptPath, option, value]);

        Assert.Equal(2, exitCode);
    }

    /// <summary>
    /// validate は workflow-config が指定されていなくても engine-config の存在確認が実行されることを検査します。
    /// </summary>
    [Fact(DisplayName = "validate は --engine-config の存在確認を行う")]
    public async Task ValidateChecksMissingEngineConfigFile()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => "main";
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        File.WriteAllText(Path.Combine(directory, "appsettings.yaml"), "title: test");

        CliResult result = await RunCliAsync("validate", scriptPath, "--workflow-config", "appsettings.yaml", "--engine-config", "missing-engine.yaml");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(WorkflowErrorCodes.ConfigNotFound, result.StandardError);
        Assert.Contains("missing-engine.yaml", result.StandardError);
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
    /// ヘルプ出力から engine defaults YAML の解決済み完全パスを抽出します。
    /// </summary>
    /// <param name="standardOutput">CLI 標準出力。</param>
    /// <returns>ヘルプ出力に含まれる engine defaults YAML パス。</returns>
    private static string ExtractEngineDefaultsPath(string standardOutput)
    {
        Match defaultsPathMatch = Regex.Match(
            standardOutput,
            @"(?im)^\s*Engine defaults:\s*(?<path>.+\.defaults\.yaml)\s*$");

        Assert.True(
            defaultsPathMatch.Success,
            $"ヘルプ出力に engine defaults YAML 行が含まれませんでした。{Environment.NewLine}{standardOutput}");

        return defaultsPathMatch.Groups["path"].Value;
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
