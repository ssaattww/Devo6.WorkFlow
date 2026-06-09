using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using System.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// リポジトリに配置した利用者向け例を検査します。
/// </summary>
public sealed class SampleWorkflowTests
{
    /// <summary>
    /// サンプルが参照する NuGet package id を表します。
    /// </summary>
    private const string SampleNuGetPackageId = "Devo6.WorkFlow.Engine";

    /// <summary>
    /// サンプルが参照する NuGet package version を表します。
    /// </summary>
    private const string SampleNuGetPackageVersion = "0.1.0";

    /// <summary>
    /// サンプルが参照する第三者 NuGet package id を表します。
    /// </summary>
    private const string SampleYamlNuGetPackageId = "YamlDotNet";

    /// <summary>
    /// サンプルが参照する第三者 NuGet package version を表します。
    /// </summary>
    private const string SampleYamlNuGetPackageVersion = "16.3.0";

    /// <summary>
    /// サンプル lock file の target framework を表します。
    /// </summary>
    private const string SampleTargetFramework = "net8.0";

    /// <summary>
    /// サンプル lock file の runtime identifier を表します。
    /// </summary>
    private const string SampleRuntimeIdentifier = "ubuntu.24.04-x64";

    /// <summary>
    /// サンプル lock file の Dotnet.Script.Core version を表します。
    /// </summary>
    private const string SampleDotnetScriptCoreVersion = "2.0.1";

    /// <summary>
    /// サンプル lock file の package source 一覧を表します。
    /// </summary>
    private static readonly string[] SamplePackageSources = ["https://api.nuget.org/v3/index.json"];

    /// <summary>
    /// リポジトリのルートパスを保持します。
    /// </summary>
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// 複数フォルダに分かれた Step を main.csx の CompositeStep から実行できることを検査します。
    /// </summary>
    [Fact(DisplayName = "複数フォルダの Step を CompositeStep から実行できる")]
    public void MultiFolderCompositeSampleRuns()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string entryPath = Path.Combine(sampleDirectory, "main.csx");
        string configPath = Path.Combine(sampleDirectory, "appsettings.yaml");
        string outputPath = Path.Combine(sampleDirectory, "output/result.txt");

        DeleteOutputDirectory(outputPath);

        WorkflowResult result = CreateSampleLoader().Execute(
            entryPath,
            options: new WorkflowExecutionOptions(engineArguments: new EngineArguments
            {
                EntryPath = entryPath,
                WorkflowConfigPath = configPath,
            }));

        Assert.True(
            result.Succeeded,
            $"エラーコード: {result.ErrorCode}{Environment.NewLine}エラー: {result.ErrorMessage}");
        string report = File.ReadAllText(outputPath);
        Assert.Contains("# Composite sample report", report);
        Assert.Contains("Title: Composite sample", report);
        Assert.Contains("Category: guide", report);
        Assert.Contains("Tags: workflow, nuget, yaml", report);
        Assert.Contains("Line count: 3", report);
        Assert.Contains("Word count: 16", report);
        Assert.Contains("Character count: 104", report);
        Assert.Contains("Tag count: 3", report);
        Assert.Contains("HELLO FROM MULTI FOLDER COMPOSITE", report);
        Assert.Contains("THIS SAMPLE KEEPS YAML METADATA SEPARATE.", report);
        Assert.Contains("NESTED STEPS BUILD A REPORT.", report);
    }

    /// <summary>
    /// Step 既定 Config と root Config の結合結果に CLI override を適用できることを検査します。
    /// </summary>
    [Fact(DisplayName = "Step 既定 Config と root Config の結合結果へ CLI override を適用できる")]
    public void MultiFolderCompositeSampleMergedYamlFragmentsCanBeOverridden()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string entryPath = Path.Combine(sampleDirectory, "main.csx");
        string configPath = Path.Combine(sampleDirectory, "appsettings.yaml");
        string outputPath = Path.Combine(sampleDirectory, "output/result.txt");

        DeleteOutputDirectory(outputPath);

        WorkflowResult result = CreateSampleLoader().Execute(
            entryPath,
            options: new WorkflowExecutionOptions(engineArguments: new EngineArguments
            {
                EntryPath = entryPath,
                WorkflowConfigPath = configPath,
                WorkflowSettings = new Dictionary<string, string>
                {
                    ["Pipeline.Normalize.Uppercase"] = "false",
                    ["Pipeline.Report.Heading"] = "Override report",
                },
            }));

        Assert.True(
            result.Succeeded,
            $"エラーコード: {result.ErrorCode}{Environment.NewLine}エラー: {result.ErrorMessage}");
        string report = File.ReadAllText(outputPath);
        Assert.Contains("# Override report", report);
        Assert.Contains("hello from multi folder composite", report);
        Assert.DoesNotContain("HELLO FROM MULTI FOLDER COMPOSITE", report);
    }

    /// <summary>
    /// 実行用 root Config が Step 既定 Config への上書きだけを持つことを検査します。
    /// </summary>
    [Fact(DisplayName = "実行用 root Config は Step 既定 Config への上書きだけを持つ")]
    public void MultiFolderCompositeSampleRootConfigContainsOnlyOverrides()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string rootConfigPath = Path.Combine(sampleDirectory, "appsettings.yaml");

        Assert.False(YamlPathExists(rootConfigPath, "Pipeline", "Load"));
        Assert.False(YamlPathExists(rootConfigPath, "Pipeline", "Parse"));
        Assert.False(YamlPathExists(rootConfigPath, "Pipeline", "Analyze"));
        Assert.False(YamlPathExists(rootConfigPath, "Save"));
        Assert.Equal("Composite sample report", ReadYamlScalar(rootConfigPath, "Pipeline", "Report", "Heading"));
    }

    /// <summary>
    /// 複数フォルダのサンプルが engine config と利用手順を分けて示すことを検査します。
    /// </summary>
    [Fact(DisplayName = "複数フォルダのサンプルは engine config と実行例を示す")]
    public void MultiFolderCompositeSampleDocumentsEngineConfigAndRunExamples()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string engineConfigPath = Path.Combine(sampleDirectory, "engine.yaml");
        string readmePath = Path.Combine(sampleDirectory, "README.md");

        Assert.True(File.Exists(engineConfigPath));
        Assert.Equal("true", ReadYamlScalar(engineConfigPath, "Logging", "File", "Enabled"));
        Assert.Equal("logs", ReadYamlScalar(engineConfigPath, "Logging", "File", "Directory"));
        Assert.Equal("{Timestamp:yyMMdd-HHmmss}_{RootStepName}.log", ReadYamlScalar(engineConfigPath, "Logging", "File", "NameFormat"));
        Assert.Equal("Text", ReadYamlScalar(engineConfigPath, "Logging", "File", "Format"));

        Assert.True(File.Exists(readmePath));
        string readme = File.ReadAllText(readmePath);
        Assert.Contains("--workflow-config", readme);
        Assert.Contains("--engine-config", readme);
        Assert.Contains("--wset", readme);
        Assert.Contains("--eset", readme);
        Assert.Contains("Logging.Console.Enabled: true", readme);
        Assert.Contains("標準出力", readme);
        Assert.Contains("260609-120000_Main.log", readme);
        Assert.Contains("{Timestamp:yyMMdd-HHmmss}_{RootStepName}.log", readme);
        Assert.Contains("Main", readme);
    }

    /// <summary>
    /// 複数フォルダのサンプルを CLI と engine config で実行できることを検査します。
    /// </summary>
    [Fact(DisplayName = "複数フォルダのサンプルは CLI と engine config で実行できる")]
    public async Task MultiFolderCompositeSampleRunsThroughCliWithEngineConfig()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string outputPath = Path.Combine(sampleDirectory, "output/result.txt");
        string logDirectory = Path.Combine(sampleDirectory, "logs");

        DeleteOutputDirectory(outputPath);
        DeleteDirectoryIfExists(logDirectory);

        try
        {
            CliResult result = await RunCliAsync(
                "run",
                "samples/multi-folder-composite/main.csx",
                "--workflow-config",
                "appsettings.yaml",
                "--engine-config",
                "engine.yaml",
                "--wset",
                "Pipeline.Report.Heading=CLI override report",
                "--eset",
                "Logging.File.Directory=logs");

            AssertSuccess(result);
            Assert.Contains("# CLI override report", File.ReadAllText(outputPath));
            string logPath = AssertSingleLogFile(logDirectory, "Main");
            Assert.Matches(@"^\d{6}-\d{6}_Main\.log$", Path.GetFileName(logPath));
            Assert.Contains("Entry succeeded", File.ReadAllText(logPath));
        }
        finally
        {
            DeleteDirectoryIfExists(logDirectory);
        }
    }

    /// <summary>
    /// --eset が engine config のログ出力先とファイル名を上書きすることを検査します。
    /// </summary>
    [Fact(DisplayName = "複数フォルダのサンプルは --eset でログ設定を上書きできる")]
    public async Task MultiFolderCompositeSampleEngineSetOverridesLogFileSettings()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string outputPath = Path.Combine(sampleDirectory, "output/result.txt");
        string configuredLogDirectory = Path.Combine(sampleDirectory, "logs");
        string overriddenLogDirectory = Path.Combine(sampleDirectory, "override-logs");

        DeleteOutputDirectory(outputPath);
        DeleteDirectoryIfExists(configuredLogDirectory);
        DeleteDirectoryIfExists(overriddenLogDirectory);

        try
        {
            CliResult result = await RunCliAsync(
                "run",
                "samples/multi-folder-composite/main.csx",
                "--workflow-config",
                "appsettings.yaml",
                "--engine-config",
                "engine.yaml",
                "--wset",
                "Pipeline.Report.Heading=Engine set report",
                "--eset",
                "Logging.File.Directory=override-logs",
                "--eset",
                "Logging.File.NameFormat=override_{RootStepName}.log");

            AssertSuccess(result);
            Assert.Contains("# Engine set report", File.ReadAllText(outputPath));
            Assert.False(Directory.Exists(configuredLogDirectory));
            string logPath = Path.Combine(overriddenLogDirectory, "override_Main.log");
            Assert.True(File.Exists(logPath));
            Assert.Contains("Entry succeeded", File.ReadAllText(logPath));
        }
        finally
        {
            DeleteDirectoryIfExists(configuredLogDirectory);
            DeleteDirectoryIfExists(overriddenLogDirectory);
        }
    }

    /// <summary>
    /// 複数フォルダのサンプルが参照用 NuGet package を入口だけで参照し、通常利用では lock file を同梱しないことを検査します。
    /// </summary>
    [Fact(DisplayName = "複数フォルダのサンプルは参照用 NuGet package を入口だけで参照する")]
    public void MultiFolderCompositeSampleUsesNuGetReferencePackage()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string entryPath = Path.Combine(sampleDirectory, "main.csx");
        string lockPath = Path.Combine(sampleDirectory, "devo6.nuget.lock.yaml");
        string entrySource = File.ReadAllText(entryPath);

        Assert.Contains($"#r \"nuget: {SampleNuGetPackageId}, {SampleNuGetPackageVersion}\"", entrySource);
        Assert.Contains($"#r \"nuget: {SampleYamlNuGetPackageId}, {SampleYamlNuGetPackageVersion}\"", entrySource);
        foreach (string scriptPath in Directory.EnumerateFiles(sampleDirectory, "*.csx", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(entryPath), StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("#r \"nuget:", File.ReadAllText(scriptPath));
        }

        Assert.False(File.Exists(lockPath));
    }

    /// <summary>
    /// 複数フォルダのサンプルが内側と外側で責務を分けた CompositeStep 構成であることを検査します。
    /// </summary>
    [Fact(DisplayName = "複数フォルダのサンプルは内側と外側で責務を分ける")]
    public void MultiFolderCompositeSampleUsesNestedCompositeStep()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string entryPath = Path.Combine(sampleDirectory, "main.csx");
        string source = File.ReadAllText(entryPath);

        Assert.Contains("public sealed class RunTextPipelineStep : IStep<ReportTextResult>", source);
        Assert.Contains("CompositeStep.Define(\"TextPipeline\")", source);
        Assert.Contains(".Run<LoadTextStep, LoadTextResult>()", source);
        Assert.Contains(".Run<ParseDocumentStep, ParsedDocument>()", source);
        Assert.Contains(".Run<NormalizeTextStep, NormalizedDocument>()", source);
        Assert.Contains(".Run<AnalyzeTextStep, AnalyzedDocument>()", source);
        Assert.Contains(".Run<BuildReportStep, ReportTextResult>()", source);
        Assert.Contains(".Run<RunTextPipelineStep, ReportTextResult>()", source);
        Assert.Contains(".Run<SaveTextStep, Unit>()", source);
        Assert.Contains(".WithConfig<LoadTextStep.Config>(\"Pipeline.Load\"", source);
        Assert.Contains(".WithConfig<ParseDocumentStep.Config>(\"Pipeline.Parse\"", source);
        Assert.Contains(".WithConfig<NormalizeTextStep.Config>(\"Pipeline.Normalize\"", source);
        Assert.Contains(".WithConfig<AnalyzeTextStep.Config>(\"Pipeline.Analyze\"", source);
        Assert.Contains(".WithConfig<BuildReportStep.Config>(\"Pipeline.Report\"", source);
        Assert.Contains(".WithConfig<SaveTextStep.Config>(\"Save\")", source);
    }

    /// <summary>
    /// リポジトリルートを探索します。
    /// </summary>
    /// <returns>検出したリポジトリルート。</returns>
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

        throw new InvalidOperationException("リポジトリルートを特定できませんでした。");
    }

    /// <summary>
    /// サンプルの NuGet 参照を外部通信なしで解決する loader を作成します。
    /// </summary>
    /// <returns>固定 NuGet graph を使う loader。</returns>
    private static CsxEntryLoader CreateSampleLoader()
    {
        return new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            AllowedNuGetReferences =
            [
                new CsxNuGetReference(SampleNuGetPackageId, SampleNuGetPackageVersion),
                new CsxNuGetReference(SampleYamlNuGetPackageId, SampleYamlNuGetPackageVersion),
            ],
            NuGetDependencyGraphProvider = new FixedNuGetDependencyGraphProvider(CreateSampleNuGetGraph()),
        });
    }

    /// <summary>
    /// サンプル用の固定 NuGet dependency graph を作成します。
    /// </summary>
    /// <returns>固定 NuGet dependency graph。</returns>
    private static CsxNuGetDependencyGraph CreateSampleNuGetGraph()
    {
        return new CsxNuGetDependencyGraph(
            CreateSampleNuGetDependencies(),
            referencePaths:
            [
                typeof(CompositeStep).Assembly.Location,
                typeof(IStep<>).Assembly.Location,
                typeof(YamlStream).Assembly.Location,
            ],
            resolutionMetadata: CreateSampleNuGetResolutionMetadata());
    }

    /// <summary>
    /// サンプル用の固定 NuGet 依存関係を作成します。
    /// </summary>
    /// <returns>固定 NuGet 依存関係。</returns>
    private static CsxResolvedNuGetDependency[] CreateSampleNuGetDependencies()
    {
        return
        [
            new(SampleNuGetPackageId, SampleNuGetPackageVersion, isDirect: true),
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
            new(SampleYamlNuGetPackageId, SampleYamlNuGetPackageVersion, isDirect: true),
        ];
    }

    /// <summary>
    /// サンプル用の固定 NuGet 解決 metadata を作成します。
    /// </summary>
    /// <returns>固定 NuGet 解決 metadata。</returns>
    private static CsxNuGetResolutionMetadata CreateSampleNuGetResolutionMetadata()
    {
        return new CsxNuGetResolutionMetadata(
            SampleTargetFramework,
            SampleRuntimeIdentifier,
            SamplePackageSources,
            SampleDotnetScriptCoreVersion);
    }

    /// <summary>
    /// サンプルの出力ディレクトリを削除します。
    /// </summary>
    /// <param name="outputPath">出力ファイル path。</param>
    private static void DeleteOutputDirectory(string outputPath)
    {
        string outputDirectory = Path.GetDirectoryName(outputPath)!;
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 指定された directory が存在する場合は削除します。
    /// </summary>
    /// <param name="directoryPath">削除する directory path。</param>
    private static void DeleteDirectoryIfExists(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    /// <summary>
    /// CLI を repository root から実行します。
    /// </summary>
    /// <param name="arguments">CLI へ渡す引数。</param>
    /// <returns>CLI 実行結果。</returns>
    private static async Task<CliResult> RunCliAsync(params string[] arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot,
        }.AddArguments([
            "run",
            "--project",
            Path.Combine(RepositoryRoot, "src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj"),
            "--configuration",
            TestBuildConfiguration.Current,
            "--no-build",
            "--",
            .. arguments,
        ]))!;
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        bool exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(60));
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;

        return new CliResult(exited ? process.ExitCode : -1, standardOutput, standardError);
    }

    /// <summary>
    /// 指定時間内に process が終了するかどうかを返します。
    /// </summary>
    /// <param name="process">終了を待つ process。</param>
    /// <param name="timeout">待機する最大時間。</param>
    /// <returns>指定時間内に終了した場合は true。</returns>
    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        Task exitTask = process.WaitForExitAsync();
        Task completedTask = await Task.WhenAny(exitTask, Task.Delay(timeout));

        return completedTask == exitTask;
    }

    /// <summary>
    /// CLI 実行が成功したことを検査します。
    /// </summary>
    /// <param name="result">CLI 実行結果。</param>
    private static void AssertSuccess(CliResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"""
            Expected exit code 0 but got {result.ExitCode}.
            STDOUT:
            {result.StandardOutput}
            STDERR:
            {result.StandardError}
            """);
    }

    /// <summary>
    /// 指定 directory のログファイルが 1 つだけで root Step 名を含むことを検査します。
    /// </summary>
    /// <param name="logDirectory">ログ directory。</param>
    /// <param name="rootStepName">期待する root Step 名。</param>
    /// <returns>見つかったログファイル path。</returns>
    private static string AssertSingleLogFile(string logDirectory, string rootStepName)
    {
        string[] logFiles = Directory.GetFiles(logDirectory, "*.log");

        Assert.Single(logFiles);
        Assert.Contains(rootStepName, Path.GetFileName(logFiles[0]));

        return logFiles[0];
    }

    /// <summary>
    /// YAML ファイルから指定された path の scalar 値を読み取ります。
    /// </summary>
    /// <param name="yamlPath">読み取る YAML ファイル。</param>
    /// <param name="segments">読み取る path の区切り。</param>
    /// <returns>読み取った scalar 値。</returns>
    private static string ReadYamlScalar(string yamlPath, params string[] segments)
    {
        var yaml = new YamlStream();
        using StreamReader reader = File.OpenText(yamlPath);
        yaml.Load(reader);

        YamlNode current = yaml.Documents[0].RootNode;
        foreach (string segment in segments)
        {
            var mapping = Assert.IsType<YamlMappingNode>(current);
            KeyValuePair<YamlNode, YamlNode> pair = mapping.Children.Single(child =>
                child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, segment, StringComparison.Ordinal));
            current = pair.Value;
        }

        return Assert.IsType<YamlScalarNode>(current).Value ?? "";
    }

    /// <summary>
    /// YAML ファイルに指定された path が存在するかどうかを返します。
    /// </summary>
    /// <param name="yamlPath">読み取る YAML ファイル。</param>
    /// <param name="segments">存在を確認する path の区切り。</param>
    /// <returns>指定 path が存在する場合は true。</returns>
    private static bool YamlPathExists(string yamlPath, params string[] segments)
    {
        var yaml = new YamlStream();
        using StreamReader reader = File.OpenText(yamlPath);
        yaml.Load(reader);

        YamlNode current = yaml.Documents[0].RootNode;
        foreach (string segment in segments)
        {
            if (current is not YamlMappingNode mapping)
            {
                return false;
            }

            KeyValuePair<YamlNode, YamlNode>? pair = mapping.Children.FirstOrDefault(child =>
                child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, segment, StringComparison.Ordinal));
            if (pair is null || pair.Value.Value is null)
            {
                return false;
            }

            current = pair.Value.Value;
        }

        return true;
    }

    /// <summary>
    /// 外部通信を使わずに固定された NuGet dependency graph を返します。
    /// </summary>
    /// <param name="graph">返却する固定 NuGet dependency graph。</param>
    private sealed class FixedNuGetDependencyGraphProvider(CsxNuGetDependencyGraph graph) : ICsxNuGetDependencyGraphProvider
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

    /// <summary>
    /// CLI 実行結果です。
    /// </summary>
    /// <param name="ExitCode">process exit code。</param>
    /// <param name="StandardOutput">標準出力。</param>
    /// <param name="StandardError">標準エラー出力。</param>
    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
}
