using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using System.Diagnostics;
using System.Reflection;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// 標準 Config 読み込みの利用者向け契約を検査します。
/// </summary>
public sealed class StandardConfigLoadingContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// CLI run が YAML Config を型付き値として StepContext に登録することを検査します。
    /// </summary>
    [Fact(DisplayName = "engine run main.csx --config は YAML 値を StepContext から型付き取得できる")]
    public async Task CliRunWithConfigLoadsYamlIntoStepContext()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class AppConfig
            {
                public string Title { get; set; } = "";
                public int Port { get; set; }
            }

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    string text = $"{config.Title}|{config.Port}";
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "config-marker.txt"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        File.WriteAllText(Path.Combine(directory, "appsettings.yaml"), "Title: configured" + Environment.NewLine + "Port: 5071" + Environment.NewLine);

        CliResult result = await RunCliAsync("run", scriptPath, "--config", "appsettings.yaml");

        AssertSuccess(result);
        Assert.Equal("configured|5071", File.ReadAllText(Path.Combine(directory, "config-marker.txt")));
    }

    /// <summary>
    /// Entry directory 基準の相対 Config path 解決が実行 cwd に依存しないことを検査します。
    /// </summary>
    [Fact(DisplayName = "--config config/appsettings.yaml は Entry directory 基準で解決される")]
    public async Task RelativeConfigPathIsResolvedFromEntryDirectory()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class AppConfig
            {
                public string Title { get; set; } = "";
            }

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "entry-relative-marker.txt"), config.Title);

                    return config.Title;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        string entryDirectory = Path.GetDirectoryName(scriptPath)!;
        Directory.CreateDirectory(Path.Combine(entryDirectory, "config"));
        File.WriteAllText(Path.Combine(entryDirectory, "config", "appsettings.yaml"), "Title: entry-directory" + Environment.NewLine);
        string unrelatedWorkingDirectory = Path.Combine(Path.GetTempPath(), "devo6-workflow-config-cwd", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(unrelatedWorkingDirectory, "config"));
        File.WriteAllText(Path.Combine(unrelatedWorkingDirectory, "config", "appsettings.yaml"), "Title: wrong-cwd" + Environment.NewLine);

        CliResult result = await RunCliInWorkingDirectoryAsync(
            unrelatedWorkingDirectory,
            "run",
            scriptPath,
            "--config",
            "config/appsettings.yaml");

        AssertSuccess(result);
        Assert.Equal("entry-directory", File.ReadAllText(Path.Combine(entryDirectory, "entry-relative-marker.txt")));
    }

    /// <summary>
    /// CompositeStep の公開 API が標準 Config 型 metadata を宣言できることを検査します。
    /// </summary>
    [Fact(DisplayName = "CompositeStep は WithConfig<TConfig>() と ConfigType metadata を公開する")]
    public void CompositeStepExposesWithConfigAndConfigTypeMetadata()
    {
        MethodInfo? withConfig = typeof(CompositeStep<string>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method => method.Name == "WithConfig" && method.IsGenericMethodDefinition);
        PropertyInfo? configType = typeof(CompositeStep<string>).GetProperty("ConfigType", BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(withConfig);
        Assert.Empty(withConfig!.GetParameters());
        Assert.Equal(typeof(CompositeStep<string>), withConfig.ReturnType);
        Assert.NotNull(configType);
        Assert.Equal(typeof(Type), configType!.PropertyType);

        CompositeStep<string> step = CompositeStep.Define("Main")
            .Run<ConfigMetadataStep, string>()
                .StoreAs();
        object configuredStep = withConfig.MakeGenericMethod(typeof(ApiConfig)).Invoke(step, [])!;

        Assert.Same(typeof(ApiConfig), configType.GetValue(configuredStep));
    }

    /// <summary>
    /// Config 型要求時に --config 未指定なら Step 実行前に CONFIG_NOT_FOUND になることを検査します。
    /// </summary>
    [Fact(DisplayName = "WithConfig 使用時に --config 未指定なら CONFIG_NOT_FOUND で失敗する")]
    public async Task MissingConfigArgumentFailsBeforeStepExecutionWithConfigNotFound()
    {
        string scriptPath = CreateConfigReadingScript("missing-argument-marker.txt");

        CliResult result = await RunCliAsync("run", scriptPath);

        AssertFailure(result, WorkflowErrorCodes.ConfigNotFound);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(scriptPath)!, "missing-argument-marker.txt")));
    }

    /// <summary>
    /// 存在しない Config file が CLI run で CONFIG_NOT_FOUND になることを検査します。
    /// </summary>
    [Fact(DisplayName = "存在しない config file は CLI run で CONFIG_NOT_FOUND になる")]
    public async Task MissingConfigFileFailsCliRunWithConfigNotFound()
    {
        string scriptPath = CreateConfigReadingScript("missing-file-marker.txt");

        CliResult result = await RunCliAsync("run", scriptPath, "--config", "missing.yaml");

        AssertFailure(result, WorkflowErrorCodes.ConfigNotFound);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(scriptPath)!, "missing-file-marker.txt")));
    }

    /// <summary>
    /// YAML 型変換失敗が CLI run で CONFIG_LOAD_FAILED になることを検査します。
    /// </summary>
    [Fact(DisplayName = "型変換できない YAML は CLI run で CONFIG_LOAD_FAILED になる")]
    public async Task InvalidYamlTypeConversionFailsCliRunWithConfigLoadFailed()
    {
        string scriptPath = CreateConfigReadingScript("type-conversion-marker.txt");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, "appsettings.yaml"), "Title: broken" + Environment.NewLine + "Port: not-a-number" + Environment.NewLine);

        CliResult result = await RunCliAsync("run", scriptPath, "--config", "appsettings.yaml");

        AssertFailure(result, WorkflowErrorCodes.ConfigLoadFailed);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(scriptPath)!, "type-conversion-marker.txt")));
    }

    /// <summary>
    /// DataAnnotations 検証失敗が CLI run で CONFIG_LOAD_FAILED になることを検査します。
    /// </summary>
    [Fact(DisplayName = "DataAnnotations 検証失敗は CLI run で CONFIG_LOAD_FAILED になる")]
    public async Task DataAnnotationsValidationFailureFailsCliRunWithConfigLoadFailed()
    {
        string scriptPath = CreateScript(
            """
            #r "System.ComponentModel.Annotations"

            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.ComponentModel.DataAnnotations;
            using System.IO;

            public sealed class AppConfig
            {
                [Required]
                public string? Title { get; set; }
            }

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "validation-marker.txt"), config.Title ?? "");

                    return config.Title ?? "";
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, "appsettings.yaml"), "{}" + Environment.NewLine);

        CliResult result = await RunCliAsync("run", scriptPath, "--config", "appsettings.yaml");

        AssertFailure(result, WorkflowErrorCodes.ConfigLoadFailed);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(scriptPath)!, "validation-marker.txt")));
    }

    /// <summary>
    /// T23 では --set が標準 Config に反映されず EngineArguments.Settings には保持されることを検査します。
    /// </summary>
    [Fact(DisplayName = "T23 では --set は標準 Config に反映されず EngineArguments.Settings に保持される")]
    public async Task SetArgumentsAreNotAppliedToStandardConfigDuringT23()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class AppConfig
            {
                public string Title { get; set; } = "";
            }

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    string text = $"{config.Title}|{arguments.Settings["Title"]}";
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "set-boundary-marker.txt"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, "appsettings.yaml"), "Title: yaml-value" + Environment.NewLine);

        CliResult result = await RunCliAsync("run", scriptPath, "--config", "appsettings.yaml", "--set", "Title=cli-value");

        AssertSuccess(result);
        Assert.Equal("yaml-value|cli-value", File.ReadAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, "set-boundary-marker.txt")));
    }

    /// <summary>
    /// 標準 Config 読み込み用の共通 .csx を作成します。
    /// </summary>
    /// <param name="markerFileName">Step が実行された場合に作成する marker file 名。</param>
    /// <returns>作成した Entry .csx path。</returns>
    private static string CreateConfigReadingScript(string markerFileName)
    {
        return CreateScript(
            $$"""
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class AppConfig
            {
                public string Title { get; set; } = "";
                public int Port { get; set; }
            }

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    string text = $"{config.Title}|{config.Port}";
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "{{markerFileName}}"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
    }

    /// <summary>
    /// CLI を repository root から実行します。
    /// </summary>
    /// <param name="arguments">CLI に渡す引数。</param>
    /// <returns>CLI の終了コードと出力。</returns>
    private static Task<CliResult> RunCliAsync(params string[] arguments)
    {
        return RunCliInWorkingDirectoryAsync(RepositoryRoot, arguments);
    }

    /// <summary>
    /// CLI を指定 cwd から実行します。
    /// </summary>
    /// <param name="workingDirectory">CLI process の working directory。</param>
    /// <param name="arguments">CLI に渡す引数。</param>
    /// <returns>CLI の終了コードと出力。</returns>
    private static async Task<CliResult> RunCliInWorkingDirectoryAsync(string workingDirectory, params string[] arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = workingDirectory,
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

        return new CliResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
        };
    }

    /// <summary>
    /// CLI 成功を検査します。
    /// </summary>
    /// <param name="result">検査対象の CLI 結果。</param>
    private static void AssertSuccess(CliResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"終了コード: {result.ExitCode}{Environment.NewLine}標準出力: {result.StandardOutput}{Environment.NewLine}標準エラー: {result.StandardError}");
    }

    /// <summary>
    /// CLI 失敗と期待 error code を検査します。
    /// </summary>
    /// <param name="result">検査対象の CLI 結果。</param>
    /// <param name="expectedErrorCode">標準エラーに含まれるべき error code。</param>
    private static void AssertFailure(CliResult result, string expectedErrorCode)
    {
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedErrorCode, result.StandardError);
    }

    /// <summary>
    /// 一時 directory に Entry .csx を作成します。
    /// </summary>
    /// <param name="contents">Entry .csx の内容。</param>
    /// <returns>作成した Entry .csx path。</returns>
    private static string CreateScript(string contents)
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-workflow-standard-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "main.csx");
        File.WriteAllText(scriptPath, contents);

        return scriptPath;
    }

    /// <summary>
    /// solution file を持つ repository root を探索します。
    /// </summary>
    /// <returns>repository root path。</returns>
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
    /// 公開 API metadata 検査で使う Config 型です。
    /// </summary>
    private sealed class ApiConfig;

    /// <summary>
    /// 公開 API metadata 検査で使う最小 Step です。
    /// </summary>
    private sealed class ConfigMetadataStep : IStep<string>
    {
        /// <summary>
        /// 固定値を返します。
        /// </summary>
        /// <param name="input">未使用の Step 入力。</param>
        /// <returns>固定文字列。</returns>
        public string Execute(StepInput input)
        {
            return "ok";
        }
    }

    /// <summary>
    /// CLI process の実行結果を保持します。
    /// </summary>
    private sealed class CliResult
    {
        /// <summary>
        /// CLI process の終了コードです。
        /// </summary>
        public required int ExitCode { get; init; }

        /// <summary>
        /// CLI process の標準出力です。
        /// </summary>
        public required string StandardOutput { get; init; }

        /// <summary>
        /// CLI process の標準エラーです。
        /// </summary>
        public required string StandardError { get; init; }
    }
}
