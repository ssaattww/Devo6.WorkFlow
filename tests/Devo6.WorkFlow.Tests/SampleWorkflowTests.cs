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
    /// 結合した Step YAML 断片に CLI override を適用できることを検査します。
    /// </summary>
    [Fact(DisplayName = "結合した Step YAML 断片へ CLI override を適用できる")]
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
    /// 実行用 Config が複数フォルダの Step YAML 断片を参照していることを検査します。
    /// </summary>
    [Fact(DisplayName = "実行用 Config は複数フォルダの Step YAML 断片を参照する")]
    public void MultiFolderCompositeSampleRuntimeConfigReferencesYamlFragments()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string rootConfigPath = Path.Combine(sampleDirectory, "appsettings.yaml");

        Assert.Equal(
            "steps/load/appsettings.yaml",
            ReadYamlScalar(rootConfigPath, "Load"));
        Assert.Equal(
            "steps/convert/appsettings.yaml",
            ReadYamlScalar(rootConfigPath, "Convert"));
        Assert.Equal(
            "steps/save/appsettings.yaml",
            ReadYamlScalar(rootConfigPath, "Save"));
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
}
