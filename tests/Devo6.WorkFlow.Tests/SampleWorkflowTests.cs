using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
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
                ConfigPath = configPath,
            }));

        Assert.True(
            result.Succeeded,
            $"エラーコード: {result.ErrorCode}{Environment.NewLine}エラー: {result.ErrorMessage}");
        Assert.Equal("converted: HELLO FROM MULTI FOLDER COMPOSITE", File.ReadAllText(outputPath));
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
                ConfigPath = configPath,
                Settings = new Dictionary<string, string>
                {
                    ["Convert.ToUpper"] = "false",
                },
            }));

        Assert.True(
            result.Succeeded,
            $"エラーコード: {result.ErrorCode}{Environment.NewLine}エラー: {result.ErrorMessage}");
        Assert.Equal("converted: hello from multi folder composite", File.ReadAllText(outputPath));
    }

    /// <summary>
    /// 実行用 root Config が Step 既定 Config への上書きだけを持つことを検査します。
    /// </summary>
    [Fact(DisplayName = "実行用 root Config は Step 既定 Config への上書きだけを持つ")]
    public void MultiFolderCompositeSampleRootConfigContainsOnlyOverrides()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string rootConfigPath = Path.Combine(sampleDirectory, "appsettings.yaml");

        Assert.False(YamlPathExists(rootConfigPath, "Load"));
        Assert.Equal("converted: ", ReadYamlScalar(rootConfigPath, "Convert", "Prefix"));
        Assert.False(YamlPathExists(rootConfigPath, "Save"));
    }

    /// <summary>
    /// 複数フォルダのサンプルが参照用 NuGet package を入口だけで参照することを検査します。
    /// </summary>
    [Fact(DisplayName = "複数フォルダのサンプルは参照用 NuGet package を入口だけで参照する")]
    public void MultiFolderCompositeSampleUsesNuGetReferencePackage()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string entryPath = Path.Combine(sampleDirectory, "main.csx");
        string lockPath = Path.Combine(sampleDirectory, "devo6.nuget.lock.yaml");
        string entrySource = File.ReadAllText(entryPath);

        Assert.Contains($"#r \"nuget: {SampleNuGetPackageId}, {SampleNuGetPackageVersion}\"", entrySource);
        foreach (string scriptPath in Directory.EnumerateFiles(sampleDirectory, "*.csx", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(entryPath), StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("#r \"nuget:", File.ReadAllText(scriptPath));
        }

        string lockSource = File.ReadAllText(lockPath);
        Assert.Contains($"packageId: {SampleNuGetPackageId}", lockSource);
        Assert.Contains($"version: {SampleNuGetPackageVersion}", lockSource);
        Assert.Contains("directReferences:", lockSource);
        Assert.Contains("resolvedDependencies:", lockSource);
    }

    /// <summary>
    /// 複数フォルダのサンプルが外側 Step から内側 CompositeStep を実行する構成であることを検査します。
    /// </summary>
    [Fact(DisplayName = "複数フォルダのサンプルは内側 CompositeStep を外側 Step から実行する")]
    public void MultiFolderCompositeSampleUsesNestedCompositeStep()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string entryPath = Path.Combine(sampleDirectory, "main.csx");
        string source = File.ReadAllText(entryPath);

        Assert.Contains("public sealed class RunTextPipelineStep : IStep<Unit>", source);
        Assert.Contains("CompositeStep.Define(\"TextPipeline\")", source);
        Assert.Contains(".Run<RunTextPipelineStep, Unit>()", source);
        Assert.Contains(".WithConfig<LoadTextStep.Config>(\"Load\")", source);
        Assert.Contains(".WithConfig<ConvertTextStep.Config>(\"Convert\")", source);
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
            AllowedNuGetReferences = [new CsxNuGetReference(SampleNuGetPackageId, SampleNuGetPackageVersion)],
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
            new("YamlDotNet", "16.3.0", isDirect: false),
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
}
