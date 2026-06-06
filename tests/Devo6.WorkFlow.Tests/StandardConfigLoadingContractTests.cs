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
    /// CLI run の --set が標準 Config を上書きし、raw 設定も保持することを検査します。
    /// </summary>
    [Fact(DisplayName = "CLI run の --set は YAML 値を上書きし EngineArguments.Settings も保持する")]
    public async Task CliRunSetOverridesStandardConfigAndPreservesRawSettings()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class AppConfig
            {
                /// <summary>
                /// 変換設定を取得または設定します。
                /// </summary>
                public ConvertConfig Convert { get; set; } = new();

                /// <summary>
                /// 保存設定を取得または設定します。
                /// </summary>
                public SaveConfig Save { get; set; } = new();
            }

            public sealed class ConvertConfig
            {
                /// <summary>
                /// 大文字変換を有効にするかどうかを取得または設定します。
                /// </summary>
                public bool ToUpper { get; set; }
            }

            public sealed class SaveConfig
            {
                /// <summary>
                /// 保存 path を取得または設定します。
                /// </summary>
                public string Path { get; set; } = "";
            }

            public sealed class MainStep : IStep<string>
            {
                /// <summary>
                /// Config と raw 設定を marker file に書き込みます。
                /// </summary>
                /// <param name="input">Step 入力。</param>
                /// <returns>確認用文字列。</returns>
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    string text = $"{config.Convert.ToUpper}|{config.Save.Path}|{arguments.Settings["Convert.ToUpper"]}|{arguments.Settings["Save.Path"]}";
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "override-marker.txt"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        File.WriteAllText(
            Path.Combine(directory, "appsettings.yaml"),
            """
            Convert:
              ToUpper: true
            Save:
              Path: yaml.txt
            """);

        CliResult result = await RunCliAsync(
            "run",
            scriptPath,
            "--config",
            "appsettings.yaml",
            "--set",
            "Convert.ToUpper=false",
            "--set",
            "Save.Path=cli.txt");

        AssertSuccess(result);
        Assert.Equal("False|cli.txt|false|cli.txt", File.ReadAllText(Path.Combine(directory, "override-marker.txt")));
    }

    /// <summary>
    /// 同一 key の --set は後の値を Config と raw 設定に反映することを検査します。
    /// </summary>
    [Fact(DisplayName = "同一 key の --set は後勝ちで Config と EngineArguments.Settings に反映される")]
    public async Task RepeatedSetUsesLastValueForConfigAndRawSettings()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class AppConfig
            {
                /// <summary>
                /// タイトルを取得または設定します。
                /// </summary>
                public string Title { get; set; } = "";
            }

            public sealed class MainStep : IStep<string>
            {
                /// <summary>
                /// Config と raw 設定を marker file に書き込みます。
                /// </summary>
                /// <param name="input">Step 入力。</param>
                /// <returns>確認用文字列。</returns>
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    string text = $"{config.Title}|{arguments.Settings["Title"]}";
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "last-wins-marker.txt"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        File.WriteAllText(Path.Combine(directory, "appsettings.yaml"), "Title: yaml-value" + Environment.NewLine);

        CliResult result = await RunCliAsync(
            "run",
            scriptPath,
            "--config",
            "appsettings.yaml",
            "--set",
            "Title=first",
            "--set",
            "Title=second");

        AssertSuccess(result);
        Assert.Equal("second|second", File.ReadAllText(Path.Combine(directory, "last-wins-marker.txt")));
    }

    /// <summary>
    /// 入れ子 property の途中が null の場合に --set が中間 Config を自動生成することを検査します。
    /// </summary>
    [Fact(DisplayName = "入れ子 property の --set は null 中間 Config を自動生成する")]
    public async Task SetCreatesMissingNestedConfigObjects()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public sealed class AppConfig
            {
                /// <summary>
                /// 変換設定を取得または設定します。
                /// </summary>
                public ConvertConfig? Convert { get; set; }
            }

            public sealed class ConvertConfig
            {
                /// <summary>
                /// 大文字変換を有効にするかどうかを取得または設定します。
                /// </summary>
                public bool ToUpper { get; set; }
            }

            public sealed class MainStep : IStep<string>
            {
                /// <summary>
                /// 中間 Config の生成結果を marker file に書き込みます。
                /// </summary>
                /// <param name="input">Step 入力。</param>
                /// <returns>確認用文字列。</returns>
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    string text = config.Convert is null ? "missing" : config.Convert.ToUpper.ToString();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "nested-marker.txt"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        File.WriteAllText(Path.Combine(directory, "appsettings.yaml"), "{}" + Environment.NewLine);

        CliResult result = await RunCliAsync("run", scriptPath, "--config", "appsettings.yaml", "--set", "Convert.ToUpper=true");

        AssertSuccess(result);
        Assert.Equal("True", File.ReadAllText(Path.Combine(directory, "nested-marker.txt")));
    }

    /// <summary>
    /// --set が bool、int、enum、nullable primitive を対象型へ変換することを検査します。
    /// </summary>
    [Fact(DisplayName = "--set は bool int enum nullable primitive を Config 型へ変換する")]
    public async Task SetConvertsPrimitiveEnumAndNullableValues()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.IO;

            public enum RunMode
            {
                Slow,
                Fast
            }

            public sealed class AppConfig
            {
                /// <summary>
                /// 有効状態を取得または設定します。
                /// </summary>
                public bool Enabled { get; set; }

                /// <summary>
                /// ポート番号を取得または設定します。
                /// </summary>
                public int Port { get; set; }

                /// <summary>
                /// 実行 mode を取得または設定します。
                /// </summary>
                public RunMode Mode { get; set; }

                /// <summary>
                /// 任意の上限値を取得または設定します。
                /// </summary>
                public int? OptionalLimit { get; set; }
            }

            public sealed class MainStep : IStep<string>
            {
                /// <summary>
                /// 型変換後の Config 値を marker file に書き込みます。
                /// </summary>
                /// <param name="input">Step 入力。</param>
                /// <returns>確認用文字列。</returns>
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    string text = $"{config.Enabled}|{config.Port}|{config.Mode}|{config.OptionalLimit}";
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "typed-marker.txt"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        File.WriteAllText(
            Path.Combine(directory, "appsettings.yaml"),
            """
            Enabled: false
            Port: 1
            Mode: Slow
            OptionalLimit:
            """);

        CliResult result = await RunCliAsync(
            "run",
            scriptPath,
            "--config",
            "appsettings.yaml",
            "--set",
            "Enabled=true",
            "--set",
            "Port=8080",
            "--set",
            "Mode=Fast",
            "--set",
            "OptionalLimit=42");

        AssertSuccess(result);
        Assert.Equal("True|8080|Fast|42", File.ReadAllText(Path.Combine(directory, "typed-marker.txt")));
    }

    /// <summary>
    /// --set が list と array の既存要素 property を上書きすることを検査します。
    /// </summary>
    [Fact(DisplayName = "--set は list と array の既存要素 property を上書きする")]
    public async Task SetOverridesExistingListAndArrayElements()
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.Collections.Generic;
            using System.IO;

            public sealed class AppConfig
            {
                /// <summary>
                /// list の item 設定を取得または設定します。
                /// </summary>
                public List<ItemConfig> Items { get; set; } = new();

                /// <summary>
                /// array の item 設定を取得または設定します。
                /// </summary>
                public ItemConfig[] ArrayItems { get; set; } = [];
            }

            public sealed class ItemConfig
            {
                /// <summary>
                /// item 名を取得または設定します。
                /// </summary>
                public string Name { get; set; } = "";
            }

            public sealed class MainStep : IStep<string>
            {
                /// <summary>
                /// collection の Config 値を marker file に書き込みます。
                /// </summary>
                /// <param name="input">Step 入力。</param>
                /// <returns>確認用文字列。</returns>
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    AppConfig config = input.Context.Get<AppConfig>();
                    string text = $"{config.Items[0].Name}|{config.ArrayItems[0].Name}";
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "collection-marker.txt"), text);

                    return text;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        File.WriteAllText(
            Path.Combine(directory, "appsettings.yaml"),
            """
            Items:
              - Name: yaml-list
            ArrayItems:
              - Name: yaml-array
            """);

        CliResult result = await RunCliAsync(
            "run",
            scriptPath,
            "--config",
            "appsettings.yaml",
            "--set",
            "Items[0].Name=cli-list",
            "--set",
            "ArrayItems[0].Name=cli-array");

        AssertSuccess(result);
        Assert.Equal("cli-list|cli-array", File.ReadAllText(Path.Combine(directory, "collection-marker.txt")));
    }

    /// <summary>
    /// Config 型への --set 適用失敗が Step 実行前に CONFIG_LOAD_FAILED になることを検査します。
    /// </summary>
    /// <param name="setArgument">失敗させる --set 引数。</param>
    [Theory(DisplayName = "Config 型への --set 適用失敗は CONFIG_LOAD_FAILED で Step を実行しない")]
    [InlineData("Missing.Name=value")]
    [InlineData("Port=not-a-number")]
    [InlineData("Items[1].Name=value")]
    [InlineData("Items[-1].Name=value")]
    [InlineData("Items[abc].Name=value")]
    public async Task InvalidSetApplicationFailsBeforeStepExecutionWithConfigLoadFailed(string setArgument)
    {
        string scriptPath = CreateScript(
            """
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            using System.Collections.Generic;
            using System.IO;

            public sealed class AppConfig
            {
                /// <summary>
                /// ポート番号を取得または設定します。
                /// </summary>
                public int Port { get; set; }

                /// <summary>
                /// item 設定を取得または設定します。
                /// </summary>
                public List<ItemConfig> Items { get; set; } = new();
            }

            public sealed class ItemConfig
            {
                /// <summary>
                /// item 名を取得または設定します。
                /// </summary>
                public string Name { get; set; } = "";
            }

            public sealed class MainStep : IStep<string>
            {
                /// <summary>
                /// 実行されたことを marker file に書き込みます。
                /// </summary>
                /// <param name="input">Step 入力。</param>
                /// <returns>固定文字列。</returns>
                public string Execute(StepInput input)
                {
                    EngineArguments arguments = input.Context.Get<EngineArguments>();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(arguments.EntryPath)!, "invalid-set-marker.txt"), "ran");

                    return "ran";
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs()
                .WithConfig<AppConfig>();
            """);
        string directory = Path.GetDirectoryName(scriptPath)!;
        File.WriteAllText(
            Path.Combine(directory, "appsettings.yaml"),
            """
            Port: 5071
            Items:
              - Name: yaml
            """);

        CliResult result = await RunCliAsync("run", scriptPath, "--config", "appsettings.yaml", "--set", setArgument);

        AssertFailure(result, WorkflowErrorCodes.ConfigLoadFailed);
        Assert.False(File.Exists(Path.Combine(directory, "invalid-set-marker.txt")));
    }

    /// <summary>
    /// validate は T24 で --set の型検証を行わず Config path 存在確認までで成功することを検査します。
    /// </summary>
    [Fact(DisplayName = "validate は T24 で --set の型検証を行わない")]
    public async Task ValidateDoesNotTypeCheckSetOverridesDuringT24()
    {
        string scriptPath = CreateConfigReadingScript("validate-set-marker.txt");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, "appsettings.yaml"), "Title: configured" + Environment.NewLine + "Port: 5071" + Environment.NewLine);

        CliResult result = await RunCliAsync("validate", scriptPath, "--config", "appsettings.yaml", "--set", "Port=not-a-number");

        AssertSuccess(result);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(scriptPath)!, "validate-set-marker.txt")));
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
