using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// T27 の NuGet lock file 検査契約を検査先行で固定します。
/// </summary>
public sealed class NuGetLockContractTests
{
    /// <summary>
    /// 標準 lock file 名を表します。
    /// </summary>
    private const string DefaultNuGetLockFileName = "devo6.nuget.lock.yaml";

    /// <summary>
    /// 標準 fixture の target framework を表します。
    /// </summary>
    private const string DefaultTargetFramework = "net8.0";

    /// <summary>
    /// 標準 fixture の runtime identifier を表します。
    /// </summary>
    private const string DefaultRuntimeIdentifier = "linux-x64";

    /// <summary>
    /// 標準 fixture の Dotnet.Script.Core version を表します。
    /// </summary>
    private const string DefaultDotnetScriptCoreVersion = "2.0.1";

    /// <summary>
    /// 標準 fixture の package source 一覧を表します。
    /// </summary>
    private static readonly string[] DefaultPackageSources = ["https://api.nuget.org/v3/index.json", "https://example.invalid/nuget/v3/index.json"];

    /// <summary>
    /// NuGet 参照がある実行で lock file が無い場合に専用 error code で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock missing when NuGet reference has no lock file")]
    public void ExecuteReturnsLockMissingWhenNuGetReferenceHasNoLockFile()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        var provider = new FakeNuGetDependencyGraphProvider();
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMissing, result.ErrorCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// NuGet 参照がある validation で lock file が無い場合に専用 error code で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate returns lock missing when NuGet reference has no lock file")]
    public void ValidateReturnsLockMissingWhenNuGetReferenceHasNoLockFile()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        var provider = new FakeNuGetDependencyGraphProvider();
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMissing, error.Code);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// direct NuGet 参照の version が lock file と異なる場合に実行が不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock mismatch when direct NuGet version differs")]
    public void ExecuteReturnsLockMismatchWhenDirectNuGetVersionDiffers()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.0", resolvedVersion: "33.0.0");
        var provider = new FakeNuGetDependencyGraphProvider();
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, result.ErrorCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// 解決済み NuGet 依存の version が lock file と異なる場合に validation が不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate returns lock mismatch when resolved NuGet dependency differs")]
    public void ValidateReturnsLockMismatchWhenResolvedNuGetDependencyDiffers()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "7.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, error.Code);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// lock file に再現性 metadata が無い場合に復元前の不一致として失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate returns lock mismatch before restore when reproducibility metadata is missing")]
    public void ValidateReturnsLockMismatchBeforeRestoreWhenReproducibilityMetadataIsMissing()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteLegacyLockFileWithoutMetadata(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, error.Code);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// target framework が lock file と異なる場合に不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock mismatch when target framework differs")]
    public void ExecuteReturnsLockMismatchWhenTargetFrameworkDiffers()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0", targetFramework: "net9.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, result.ErrorCode);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// runtime identifier が lock file と異なる場合に不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock mismatch when runtime identifier differs")]
    public void ExecuteReturnsLockMismatchWhenRuntimeIdentifierDiffers()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0", runtimeIdentifier: "win-x64");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, result.ErrorCode);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// Dotnet.Script.Core version が lock file と異なる場合に不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock mismatch when Dotnet Script Core version differs")]
    public void ExecuteReturnsLockMismatchWhenDotnetScriptCoreVersionDiffers()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0", dotnetScriptCoreVersion: "2.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, result.ErrorCode);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// package source の順序だけが違う場合は lock file と一致することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute treats package source order as lock equivalent")]
    public void ExecuteTreatsPackageSourceOrderAsLockEquivalent()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(
            scriptPath,
            directVersion: "33.0.1",
            resolvedVersion: "8.0.0",
            packageSources: DefaultPackageSources.Reverse().ToArray());
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.True(result.Succeeded);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// package source の値が異なる場合に不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock mismatch when package source value differs")]
    public void ExecuteReturnsLockMismatchWhenPackageSourceValueDiffers()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(
            scriptPath,
            directVersion: "33.0.1",
            resolvedVersion: "8.0.0",
            packageSources: ["https://api.nuget.org/v3/index.json", "https://different.example.invalid/v3/index.json"]);
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, result.ErrorCode);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// lock file と解決済み NuGet 依存が一致する場合に fake graph provider だけで実行できることを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute uses locked NuGet dependencies when lock matches")]
    public void ExecuteUsesLockedNuGetDependenciesWhenLockMatches()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.True(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// 浮動 NuGet version が lock file 検査より前に拒否されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate keeps floating NuGet version rejected before lock check")]
    public void ValidateKeepsFloatingNuGetVersionRejectedBeforeLockCheck()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "*"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider();
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, error.Code);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// 許可外 NuGet 参照が dependency graph 解決より前に拒否されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute does not resolve dependencies when NuGet reference is not allowed")]
    public void ExecuteDoesNotResolveDependenciesWhenNuGetReferenceIsNotAllowed()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider();
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            WorkflowRoot = Path.GetDirectoryName(scriptPath),
            NuGetDependencyGraphProvider = provider,
        });

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, result.ErrorCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// T28 より前は NuGet load directive が lock 検査に進まず unsupported のままであることを確認します。
    /// </summary>
    [Fact(DisplayName = "NuGet load directive remains unsupported before T28")]
    public void NuGetLoadDirectiveRemainsUnsupportedBeforeT28()
    {
        string scriptPath = CreateScript(
            """
            #load "nuget: CsvHelper, 33.0.1"
            """);
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider();
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, result.ErrorCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// 指定された script path に対する T27 用 loader を作成します。
    /// </summary>
    private static CsxEntryLoader CreateLoader(string scriptPath, FakeNuGetDependencyGraphProvider provider)
    {
        return new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            WorkflowRoot = Path.GetDirectoryName(scriptPath),
            AllowedNuGetReferences = [new CsxNuGetReference("CsvHelper", "33.0.1")],
            NuGetDependencyGraphProvider = provider,
        });
    }

    /// <summary>
    /// NuGet 参照を含む最小の workflow script を作成します。
    /// </summary>
    private static string CreateWorkflowScript(string packageId, string version)
    {
        return $$"""
            #r "nuget: {{packageId}}, {{version}}"
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => "locked";
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """;
    }

    /// <summary>
    /// 標準名の NuGet lock file fixture を script directory に書き込みます。
    /// </summary>
    private static void WriteDefaultLockFile(
        string scriptPath,
        string directVersion,
        string resolvedVersion,
        string targetFramework = DefaultTargetFramework,
        string runtimeIdentifier = DefaultRuntimeIdentifier,
        string dotnetScriptCoreVersion = DefaultDotnetScriptCoreVersion,
        IReadOnlyList<string>? packageSources = null)
    {
        string directory = Path.GetDirectoryName(scriptPath)!;
        string lockPath = Path.Combine(directory, DefaultNuGetLockFileName);
        string packageSourceYaml = ToPackageSourceYaml(packageSources ?? DefaultPackageSources);
        File.WriteAllText(
            lockPath,
            $$"""
            version: 1
            entry: main.csx
            targetFramework: {{targetFramework}}
            runtimeIdentifier: {{runtimeIdentifier}}
            packageSources:
            {{packageSourceYaml}}
            dotnetScriptCoreVersion: {{dotnetScriptCoreVersion}}
            directReferences:
              - packageId: CsvHelper
                version: {{directVersion}}
            resolvedDependencies:
              - packageId: CsvHelper
                version: {{directVersion}}
                direct: true
              - packageId: Microsoft.Bcl.AsyncInterfaces
                version: {{resolvedVersion}}
                direct: false
            """);
    }

    /// <summary>
    /// T27 metadata 追加前の lock file fixture を script directory に書き込みます。
    /// </summary>
    private static void WriteLegacyLockFileWithoutMetadata(string scriptPath, string directVersion, string resolvedVersion)
    {
        string directory = Path.GetDirectoryName(scriptPath)!;
        string lockPath = Path.Combine(directory, DefaultNuGetLockFileName);
        File.WriteAllText(
            lockPath,
            $$"""
            version: 1
            entry: main.csx
            directReferences:
              - packageId: CsvHelper
                version: {{directVersion}}
            resolvedDependencies:
              - packageId: CsvHelper
                version: {{directVersion}}
                direct: true
              - packageId: Microsoft.Bcl.AsyncInterfaces
                version: {{resolvedVersion}}
                direct: false
            """);
    }

    /// <summary>
    /// package source 一覧を YAML sequence に変換します。
    /// </summary>
    private static string ToPackageSourceYaml(IReadOnlyList<string> packageSources)
    {
        return string.Join(Environment.NewLine, packageSources.Select(source => $"  - {source}"));
    }

    /// <summary>
    /// fake provider が返す解決済み NuGet dependency graph を作成します。
    /// </summary>
    private static CsxNuGetDependencyGraph CreateGraph(
        string directPackageId,
        string directVersion,
        params (string PackageId, string Version)[] transitiveDependencies)
    {
        var dependencies = new List<CsxResolvedNuGetDependency>
        {
            new(directPackageId, directVersion, isDirect: true),
        };

        dependencies.AddRange(transitiveDependencies.Select(
            dependency => new CsxResolvedNuGetDependency(dependency.PackageId, dependency.Version, isDirect: false)));

        return new CsxNuGetDependencyGraph(dependencies, resolutionMetadata: CreateDefaultResolutionMetadata());
    }

    /// <summary>
    /// fake provider が返す標準の NuGet 解決 metadata を作成します。
    /// </summary>
    private static CsxNuGetResolutionMetadata CreateDefaultResolutionMetadata()
    {
        return new CsxNuGetResolutionMetadata(
            DefaultTargetFramework,
            DefaultRuntimeIdentifier,
            DefaultPackageSources,
            DefaultDotnetScriptCoreVersion);
    }

    /// <summary>
    /// 一時 directory に main.csx と追加 file を作成します。
    /// </summary>
    private static string CreateScript(string contents, params (string RelativePath, string Contents)[] additionalFiles)
    {
        string directory = CreateWorkflowDirectory();
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "main.csx");
        File.WriteAllText(scriptPath, contents);

        foreach ((string relativePath, string fileContents) in additionalFiles)
        {
            string filePath = Path.Combine(directory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, fileContents);
        }

        return scriptPath;
    }

    /// <summary>
    /// テスト用 workflow directory の path を作成します。
    /// </summary>
    private static string CreateWorkflowDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "devo6-workflow-nuget-lock-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// 外部通信を使わずに固定された NuGet dependency graph を返します。
    /// </summary>
    private sealed class FakeNuGetDependencyGraphProvider : ICsxNuGetDependencyGraphProvider
    {
        /// <summary>
        /// Resolve が呼び出された回数を取得します。
        /// </summary>
        public int ResolveCallCount { get; private set; }

        /// <summary>
        /// Resolve が返す dependency graph を取得または設定します。
        /// </summary>
        public CsxNuGetDependencyGraph Graph { get; init; } = new([]);

        /// <summary>
        /// 固定された dependency graph を返します。
        /// </summary>
        public CsxNuGetDependencyGraph Resolve(
            IReadOnlyList<CsxNuGetReference> directReferences,
            CsxNuGetDependencyGraphRequest request)
        {
            ResolveCallCount++;

            return Graph;
        }
    }
}
