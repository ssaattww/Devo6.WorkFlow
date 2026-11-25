#if NET10_0
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// SDK contract checks that must run before restore.
/// </summary>
public sealed class FileBasedCsWorkflowBuilderContractTests
{
    [Theory(DisplayName = "SDK 10.0.404 and later in the 10.0.4xx band are accepted")]
    [InlineData("10.0.404", true)]
    [InlineData("10.0.499", true)]
    [InlineData("10.0.403", false)]
    [InlineData("10.0.500", false)]
    [InlineData("9.0.404", false)]
    /// <summary>
    /// SDK の最低版と許容 feature band を検証します。
    /// </summary>
    public void SdkVersionHasMinimumAndFeatureBand(string version, bool expected) =>
        Assert.Equal(expected, FileBasedCsWorkflowBuilder.IsSupportedSdkVersion(version));

    [Fact(DisplayName = "effective SDK evaluation accepts the exact project contract")]
    /// <summary>
    /// 正確な Library、package、SDK path の evaluation を受け入れます。
    /// </summary>
    public void EffectiveProjectMatchesContract()
    {
        string json = EvaluationJson("net10.0", "[0.1.0]");
        Assert.True(FileBasedCsWorkflowBuilder.ValidateEffectiveProjectJson(
            json,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Devo6.WorkFlow.Engine"] = "0.1.0" },
            ["https://packages.example/v3/index.json"],
            "10.0.404",
            out string error), error);
    }

    [Theory(DisplayName = "effective SDK evaluation rejects inherited changes before restore")]
    [InlineData("net8.0", "[0.1.0]")]
    [InlineData("net10.0", "0.1.0")]
    [InlineData("net10.0", "[0.2.0]")]
    /// <summary>
    /// TFM と厳密な PackageReference の不一致を拒否します。
    /// </summary>
    public void EffectiveProjectRejectsFrameworkAndPackageChanges(string targetFramework, string version)
    {
        Assert.False(FileBasedCsWorkflowBuilder.ValidateEffectiveProjectJson(
            EvaluationJson(targetFramework, version),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Devo6.WorkFlow.Engine"] = "0.1.0" },
            ["https://packages.example/v3/index.json"],
            "10.0.404",
            out _));
    }

    [Fact(DisplayName = "C# compiler diagnostics are distinguished from MSBuild failures")]
    /// <summary>
    /// C# compiler error と一般 build error を分けます。
    /// </summary>
    public void BuildFailuresAreClassifiedByCompilerDiagnostic()
    {
        Assert.Equal(WorkflowErrorCodes.ScriptCompileFailed, FileBasedCsWorkflowBuilder.ClassifyBuildFailure(
            new FileBasedSdkProcessResult(1, "main.cs(4,9): error CS0246: missing type", "")));
        Assert.Equal(WorkflowErrorCodes.ScriptBuildFailed, FileBasedCsWorkflowBuilder.ClassifyBuildFailure(
            new FileBasedSdkProcessResult(1, "error MSB4018: target failed", "")));
    }

    [Theory(DisplayName = "SDK runfile artifacts use the SDK root and normalized entry path")]
    [InlineData(true, "/tmp", "/home/user/.local/share")]
    [InlineData(false, "/tmp", "/home/user/.local/share")]
    /// <summary>
    /// SDK runfile の root、正規化ハッシュ、排他キーを照合します。
    /// </summary>
    public void ArtifactPathMatchesSdkRootAndNormalizedHash(bool isWindows, string tempPath, string localPath)
    {
        string root = FileBasedCsWorkflowBuilder.GetSdkRunFileRoot(isWindows, tempPath, localPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(isWindows ? tempPath : localPath, "dotnet", "runfile")), root);

        string first = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(Path.Combine(Path.GetTempPath(), "Workflow.cs"), root);
        string second = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(Path.Combine(Path.GetTempPath(), "workflow.cs"), root);
        Assert.Equal(Path.GetFileName(first)!.Split('-').Last(), Path.GetFileName(second)!.Split('-').Last());
        Assert.Equal(
            FileBasedCsWorkflowBuilder.GetFileBasedOutputLockPath(root, first, isWindows: true),
            FileBasedCsWorkflowBuilder.GetFileBasedOutputLockPath(root, second, isWindows: true));
    }

    /// <summary>
    /// SDK evaluation JSON の契約 fixture を作ります。
    /// </summary>
    private static string EvaluationJson(string targetFramework, string packageVersion) => $$"""
        {
          "Properties": {
            "TargetFramework": "{{targetFramework}}",
            "TargetFrameworks": "",
            "OutputType": "Library",
            "PublishAot": "false",
            "RestorePackagesWithLockFile": "false",
            "ManagePackageVersionsCentrally": "false",
            "RestoreAdditionalProjectSources": "",
            "RestoreConfigFile": "",
            "RestoreSources": "https://packages.example/v3/index.json",
            "MSBuildSDKsPath": "/usr/share/dotnet/sdk/10.0.404/Sdks"
          },
          "Items": {
            "PackageReference": [
              { "Identity": "Devo6.WorkFlow.Engine", "Version": "{{packageVersion}}" }
            ]
          }
        }
        """;
}
#endif
