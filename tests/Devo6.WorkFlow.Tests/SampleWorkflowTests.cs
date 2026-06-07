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

        if (Directory.Exists(Path.GetDirectoryName(outputPath)!))
        {
            Directory.Delete(Path.GetDirectoryName(outputPath)!, recursive: true);
        }

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
    /// 複数フォルダの Step ごとに置いた YAML 断片が実行用 Config と一致することを検査します。
    /// </summary>
    [Fact(DisplayName = "複数フォルダの Step YAML 断片は実行用 Config と一致する")]
    public void MultiFolderCompositeSampleYamlFragmentsMatchRuntimeConfig()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples/multi-folder-composite");
        string rootConfigPath = Path.Combine(sampleDirectory, "appsettings.yaml");

        Assert.Equal(
            ReadYamlScalar(rootConfigPath, "Load", "Path"),
            ReadYamlScalar(Path.Combine(sampleDirectory, "steps/load/appsettings.yaml"), "Path"));
        Assert.Equal(
            ReadYamlScalar(rootConfigPath, "Convert", "Prefix"),
            ReadYamlScalar(Path.Combine(sampleDirectory, "steps/convert/appsettings.yaml"), "Prefix"));
        Assert.Equal(
            ReadYamlScalar(rootConfigPath, "Convert", "ToUpper"),
            ReadYamlScalar(Path.Combine(sampleDirectory, "steps/convert/appsettings.yaml"), "ToUpper"));
        Assert.Equal(
            ReadYamlScalar(rootConfigPath, "Save", "Path"),
            ReadYamlScalar(Path.Combine(sampleDirectory, "steps/save/appsettings.yaml"), "Path"));
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
