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

        WorkflowResult result = new CsxEntryLoader().Execute(
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

        WorkflowResult result = new CsxEntryLoader().Execute(
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
}
