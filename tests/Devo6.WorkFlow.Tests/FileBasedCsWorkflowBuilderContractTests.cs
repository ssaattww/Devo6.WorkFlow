#if NET10_0
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using System.Text.Json;

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
            "/usr/share/dotnet/sdk/10.0.404/Sdks",
            "/tmp/workflow-artifacts",
            "/tmp/workflow.cs",
            out string error), error);
    }

    /// <summary>
    /// 正規 SDK installation から離れた MSBuildSDKsPath override を拒否します。
    /// </summary>
    [Fact(DisplayName = "effective SDK evaluation rejects an unapproved MSBuild SDK path override")]
    public void EffectiveProjectRejectsSdkPathOverride()
    {
        string json = EvaluationJson("net10.0", "[0.1.0]", "/opt/override/sdk/10.0.404/Sdks");

        Assert.False(FileBasedCsWorkflowBuilder.ValidateEffectiveProjectJson(
            json,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Devo6.WorkFlow.Engine"] = "0.1.0" },
            ["https://packages.example/v3/index.json"],
            "/usr/share/dotnet/sdk/10.0.404/Sdks",
            "/tmp/workflow-artifacts",
            "/tmp/workflow.cs",
            out _));
    }

    /// <summary>
    /// 選択 SDK と異なる MSBuildExtensionsPath override を拒否します。
    /// </summary>
    [Fact(DisplayName = "effective SDK evaluation rejects an unapproved MSBuild extensions path override")]
    public void EffectiveProjectRejectsSdkExtensionsPathOverride()
    {
        string json = EvaluationJson(
            "net10.0",
            "[0.1.0]",
            extensionsPath: "/opt/override/sdk/10.0.404/");

        Assert.False(FileBasedCsWorkflowBuilder.ValidateEffectiveProjectJson(
            json,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Devo6.WorkFlow.Engine"] = "0.1.0" },
            ["https://packages.example/v3/index.json"],
            "/usr/share/dotnet/sdk/10.0.404/Sdks",
            "/tmp/workflow-artifacts",
            "/tmp/workflow.cs",
            out _));
    }

    /// <summary>
    /// 子 SDK の固定 UI 言語で得た Base Path を選択版と照合します。
    /// </summary>
    [Fact(DisplayName = "selected SDK path is read from the fixed-language SDK info")]
    public void SelectedSdkPathComesFromSdkInfo()
    {
        Assert.True(FileBasedCsWorkflowBuilder.TryGetSdkSdksPath(
            ".NET SDK:\n Version: 10.0.404\n Base Path: /opt/secondary/sdk/10.0.404/",
            "10.0.404",
            out string sdksPath));
        Assert.Equal(Path.GetFullPath("/opt/secondary/sdk/10.0.404/Sdks"), sdksPath);
        Assert.False(FileBasedCsWorkflowBuilder.TryGetSdkSdksPath(
            ".NET SDK:\n Version: 10.0.404\n Base Path: /opt/override/sdk/10.0.403/",
            "10.0.404",
            out _));
        Assert.False(FileBasedCsWorkflowBuilder.TryGetSdkSdksPath(
            ".NET SDK:\n Version: 10.0.404\n Base Path: relative/sdk/10.0.404/",
            "10.0.404",
            out _));
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
            "/usr/share/dotnet/sdk/10.0.404/Sdks",
            "/tmp/workflow-artifacts",
            "/tmp/workflow.cs",
            out _));
    }

    [Theory(DisplayName = "effective SDK output and intermediate overrides outside the artifact directory are rejected")]
    [InlineData("\"BaseOutputPath\": \"/tmp/workflow-artifacts/bin/\"", "\"BaseOutputPath\": \"/tmp/unlocked/bin/\"")]
    [InlineData("\"IntermediateOutputPath\": \"/tmp/workflow-artifacts/obj/debug/\"", "\"IntermediateOutputPath\": \"/tmp/unlocked/obj/debug/\"")]
    [InlineData("\"MSBuildProjectExtensionsPath\": \"/tmp/workflow-artifacts/obj/\"", "\"MSBuildProjectExtensionsPath\": \"/tmp/unlocked/obj/\"")]
    [InlineData("\"TargetPath\": \"/tmp/workflow-artifacts/bin/debug/workflow.dll\"", "\"TargetPath\": \"/tmp/unlocked/workflow.dll\"")]
    /// <summary>
    /// DLL、assets、中間出力がロック対象の runfile directory から外れる評価結果を拒否します。
    /// </summary>
    public void EffectiveProjectRejectsOutputPathOverride(string expectedProperty, string overriddenProperty)
    {
        string json = EvaluationJson("net10.0", "[0.1.0]").Replace(expectedProperty, overriddenProperty, StringComparison.Ordinal);

        Assert.False(FileBasedCsWorkflowBuilder.ValidateEffectiveProjectJson(
            json,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Devo6.WorkFlow.Engine"] = "0.1.0" },
            ["https://packages.example/v3/index.json"],
            "/usr/share/dotnet/sdk/10.0.404/Sdks",
            "/tmp/workflow-artifacts",
            "/tmp/workflow.cs",
            out _));
    }

    [Fact(DisplayName = "NuGet source comparison follows URI and filesystem semantics")]
    /// <summary>
    /// URI host は大文字小文字を区別せず、URI path は区別して NuGet source を比較します。
    /// </summary>
    public void PackageSourcesUseUriSemantics()
    {
        Assert.True(FileBasedCsWorkflowBuilder.PackageSourcesEqual(
            ["https://Packages.Example:443/v3/index.json"],
            ["https://packages.example/v3/index.json"],
            "/tmp"));
        Assert.False(FileBasedCsWorkflowBuilder.PackageSourcesEqual(
            ["https://packages.example/v3/index.json"],
            ["https://packages.example/V3/index.json"],
            "/tmp"));
        Assert.False(FileBasedCsWorkflowBuilder.PackageSourcesEqual(
            ["https://packages.example/v3\\index.json"],
            ["https://packages.example/v3/index.json"],
            "/tmp"));
        Assert.True(FileBasedCsWorkflowBuilder.PackageSourcesEqual(
            ["../feed"],
            ["/tmp/feed"],
            "/tmp/workflow"));
        Assert.True(FileBasedCsWorkflowBuilder.PackageSourcesEqual(
            ["file:///tmp/feed"],
            ["/tmp/feed"],
            "/tmp"));
        Assert.True(FileBasedCsWorkflowBuilder.PackageSourcesEqual(
            ["/tmp/LocalFeed"],
            ["/tmp/localfeed"],
            "/tmp",
            isWindows: true));
        Assert.False(FileBasedCsWorkflowBuilder.PackageSourcesEqual(
            ["/tmp/LocalFeed"],
            ["/tmp/localfeed"],
            "/tmp",
            isWindows: false));
    }

    /// <summary>
    /// SDK 要求ごとに異なる data、temporary、NuGet、runfile directory を割り当てます。
    /// </summary>
    [Fact(DisplayName = "SDK requests receive isolated output and cache roots")]
    public void SdkRunContextsUseDistinctOutputAndCacheRoots()
    {
        string root = Path.Combine(Path.GetTempPath(), "devo6-sdk-run-context", Guid.NewGuid().ToString("N"));
        try
        {
            FileBasedSdkRunContext first = FileBasedSdkRunContext.Create(root);
            FileBasedSdkRunContext second = FileBasedSdkRunContext.Create(root);
            var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            first.ApplyTo(environment);

            Assert.NotEqual(first.RunFileRoot, second.RunFileRoot);
            Assert.NotEqual(first.NuGetPackagesDirectory, second.NuGetPackagesDirectory);
            Assert.NotEqual(first.NuGetHttpCacheDirectory, second.NuGetHttpCacheDirectory);
            Assert.Equal(first.CliHomeDirectory, environment["DOTNET_CLI_HOME"]);
            Assert.Equal(first.NuGetPackagesDirectory, environment["NUGET_PACKAGES"]);
            Assert.Equal(first.NuGetHttpCacheDirectory, environment["NUGET_HTTP_CACHE_PATH"]);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(first.TemporaryDirectory, environment["TEMP"]);
                Assert.Equal(first.TemporaryDirectory, environment["TMP"]);
            }
            else
            {
                Assert.Equal(first.DataDirectory, environment["XDG_DATA_HOME"]);
                Assert.Equal(first.RuntimeDirectory, environment["XDG_RUNTIME_DIR"]);
                Assert.Equal(first.TemporaryDirectory, environment["TMPDIR"]);
                Assert.Equal(Path.Combine(first.DataDirectory, "dotnet", "runfile"), first.RunFileRoot);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
    /// restore assets、build DLL、排他 lock が同じ SDK runfile output root を使うことを確認します。
    /// </summary>
    [Fact(DisplayName = "assets, assembly, and lock paths share one SDK output root")]
    public void AssetsAssemblyAndLockShareSdkOutputRoot()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"devo6-workflow-output-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string entryPath = Path.Combine(tempDirectory, "Workflow.cs");
        string runFileRoot = Path.Combine(tempDirectory, "sdk", "dotnet", "runfile");
        string artifactsDirectory = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(entryPath, runFileRoot);
        string assetsPath = Path.Combine(artifactsDirectory, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
        File.WriteAllText(entryPath, "// fixture");
        File.WriteAllText(assetsPath, JsonSerializer.Serialize(new
        {
            project = new { restore = new { projectPath = Path.GetFullPath(entryPath) + ".csproj" } },
        }));

        try
        {
            string? foundAssets = FileBasedCsWorkflowBuilder.FindAssetsFile(entryPath, runFileRoot);
            string assemblyPath = FileBasedCsWorkflowBuilder.GetSdkOutputAssemblyPath(entryPath, runFileRoot);
            string lockPath = FileBasedCsWorkflowBuilder.GetFileBasedOutputLockPath(
                runFileRoot,
                artifactsDirectory,
                isWindows: false);

            Assert.Equal(Path.GetFullPath(assetsPath), foundAssets);
            Assert.StartsWith(
                Path.GetFullPath(artifactsDirectory) + Path.DirectorySeparatorChar,
                assemblyPath);
            Assert.StartsWith(Path.GetFullPath(runFileRoot) + Path.DirectorySeparatorChar, lockPath);
            Assert.Contains(Path.Combine("dotnet", "runfile"), runFileRoot, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// SDK evaluation JSON の契約 fixture を作ります。
    /// </summary>
    private static string EvaluationJson(
        string targetFramework,
        string packageVersion,
        string sdkSdksPath = "/usr/share/dotnet/sdk/10.0.404/Sdks",
        string extensionsPath = "/usr/share/dotnet/sdk/10.0.404/") => $$"""
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
            "MSBuildSDKsPath": "{{sdkSdksPath}}",
            "MSBuildExtensionsPath": "{{extensionsPath}}",
            "BaseOutputPath": "/tmp/workflow-artifacts/bin/",
            "OutputPath": "/tmp/workflow-artifacts/bin/debug/",
            "OutDir": "/tmp/workflow-artifacts/bin/debug/",
            "BaseIntermediateOutputPath": "/tmp/workflow-artifacts/obj/",
            "IntermediateOutputPath": "/tmp/workflow-artifacts/obj/debug/",
            "MSBuildProjectExtensionsPath": "/tmp/workflow-artifacts/obj/",
            "TargetPath": "/tmp/workflow-artifacts/bin/debug/workflow.dll",
            "ArtifactsPath": "/tmp/workflow-artifacts",
            "RuntimeIdentifier": "",
            "RuntimeIdentifiers": ""
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
