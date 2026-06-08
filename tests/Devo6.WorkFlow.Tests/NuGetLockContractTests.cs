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
    /// NuGet 参照がある実行で lock file が無い場合に通常の NuGet 解決へ進むことを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute succeeds when NuGet reference has no lock file by default")]
    public void ExecuteSucceedsWhenNuGetReferenceHasNoLockFileByDefault()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
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
    /// NuGet 参照がある validation で lock file が無い場合に通常の NuGet 解決へ進むことを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate succeeds when NuGet reference has no lock file by default")]
    public void ValidateSucceedsWhenNuGetReferenceHasNoLockFileByDefault()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// 厳格指定された実行で NuGet 参照があるのに lock file が無い場合に専用 error code で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock missing when NuGet lock is required")]
    public void ExecuteReturnsLockMissingWhenNuGetLockIsRequired()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        var provider = new FakeNuGetDependencyGraphProvider();
        CsxEntryLoader loader = CreateLoader(scriptPath, provider, requireNuGetLock: true);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMissing, result.ErrorCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// 厳格指定された validation で NuGet 参照があるのに lock file が無い場合に専用 error code で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate returns lock missing when NuGet lock is required")]
    public void ValidateReturnsLockMissingWhenNuGetLockIsRequired()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        var provider = new FakeNuGetDependencyGraphProvider();
        CsxEntryLoader loader = CreateLoader(scriptPath, provider, requireNuGetLock: true);

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
    /// package source 検証が未指定の場合に lock file の packageSources が無くても成功することを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate succeeds when package sources are missing and verification is unspecified")]
    public void ValidateSucceedsWhenPackageSourcesAreMissingAndVerificationIsUnspecified()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(
            scriptPath,
            directVersion: "33.0.1",
            resolvedVersion: "8.0.0",
            includePackageSources: false);
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraph("CsvHelper", "33.0.1", ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.Equal(1, provider.ResolveCallCount);
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
    /// package source 検証が有効な場合に順序だけが違う source は lock file と一致することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute treats package source order as lock equivalent when verification is enabled")]
    public void ExecuteTreatsPackageSourceOrderAsLockEquivalentWhenVerificationIsEnabled()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(
            scriptPath,
            directVersion: "33.0.1",
            resolvedVersion: "8.0.0",
            packageSources: DefaultPackageSources.Reverse().ToArray(),
            verifyPackageSources: true);
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
    /// package source 検証が無効な場合に source の値が異なっても成功することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute succeeds when package source value differs and verification is disabled")]
    public void ExecuteSucceedsWhenPackageSourceValueDiffersAndVerificationIsDisabled()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(
            scriptPath,
            directVersion: "33.0.1",
            resolvedVersion: "8.0.0",
            packageSources: ["https://api.nuget.org/v3/index.json", "https://different.example.invalid/v3/index.json"],
            verifyPackageSources: false);
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
    /// package source 検証が有効な場合に source の値が異なると不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock mismatch when package source value differs and verification is enabled")]
    public void ExecuteReturnsLockMismatchWhenPackageSourceValueDiffersAndVerificationIsEnabled()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(
            scriptPath,
            directVersion: "33.0.1",
            resolvedVersion: "8.0.0",
            packageSources: ["https://api.nuget.org/v3/index.json", "https://different.example.invalid/v3/index.json"],
            verifyPackageSources: true);
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
    /// package source 検証が有効な場合に packageSources が無いと metadata 不足で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate returns lock mismatch before restore when package source verification lacks package sources")]
    public void ValidateReturnsLockMismatchBeforeRestoreWhenPackageSourceVerificationLacksPackageSources()
    {
        string scriptPath = CreateScript(CreateWorkflowScript("CsvHelper", "33.0.1"));
        WriteDefaultLockFile(
            scriptPath,
            directVersion: "33.0.1",
            resolvedVersion: "8.0.0",
            includePackageSources: false,
            verifyPackageSources: true);
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
    /// package 内 path を含む独自 NuGet script load 文法が lock 検査より前に拒否されることを確認します。
    /// </summary>
    [Fact(DisplayName = "NuGet script load rejects package path syntax before provider")]
    public void NuGetScriptLoadRejectsPackagePathSyntaxBeforeProvider()
    {
        string scriptPath = CreateScript(
            """
            #load "nuget: CsvHelper, 33.0.1, contentFiles/csx/net8.0/csvhelper.csx"
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
    /// NuGet script load が provider の script 解決情報から展開され、読み込まれた Step を実行できることを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute loads NuGet script from provider and runs loaded Step")]
    public void ExecuteLoadsNuGetScriptFromProviderAndRunsLoadedStep()
    {
        string markerPath = CreateMarkerPath();
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "CsvHelperLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", CreateLoadedStepScript("CsvHelperLoadedStep", markerPath))],
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.True(result.Succeeded);
        Assert.Equal("Main", result.EntryName);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("CsvHelperLoadedStep", traceStep.StepName);
        Assert.Equal("CsvHelperLoadedStep;", File.ReadAllText(markerPath));
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// Validate でも NuGet script load が provider の script 解決情報から展開されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate accepts NuGet script load from provider")]
    public void ValidateAcceptsNuGetScriptLoadFromProvider()
    {
        string markerPath = CreateMarkerPath();
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "CsvHelperLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", CreateLoadedStepScript("CsvHelperLoadedStep", markerPath))],
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.False(File.Exists(markerPath));
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// provider が返した script 内の許可外 nested NuGet script load が provider 後に拒否されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate rejects unallowed nested NuGet script load after provider")]
    public void ValidateRejectsUnallowedNestedNuGetScriptLoadAfterProvider()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "NestedLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", "#load \"nuget: Other.Package, 1.0.0\"")],
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, error.Code);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// provider が返した script 内の浮動 version nested NuGet script load が provider 後に拒否されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate rejects floating nested NuGet script load after provider")]
    public void ValidateRejectsFloatingNestedNuGetScriptLoadAfterProvider()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "NestedLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", "#load \"nuget: CsvHelper, *\"")],
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, error.Code);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// provider が返した script 内の package path 付き nested NuGet script load が provider 後に拒否されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate rejects package path nested NuGet script load after provider")]
    public void ValidateRejectsPackagePathNestedNuGetScriptLoadAfterProvider()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "NestedLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", "#load \"nuget: CsvHelper, 33.0.1, contentFiles/csx/net8.0/csvhelper.csx\"")],
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptReferenceNotAllowed, error.Code);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// provider が返した script 内の nested NuGet script load が lock directReferences に含まれない場合に不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate returns lock mismatch when nested NuGet script direct reference is missing from lock")]
    public void ValidateReturnsLockMismatchWhenNestedNuGetScriptDirectReferenceIsMissingFromLock()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "NestedLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", "#load \"nuget: Nested.Package, 1.2.3\"")],
                ("Nested.Package", "1.2.3"),
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(
            scriptPath,
            provider,
            [
                new CsxNuGetReference("CsvHelper", "33.0.1"),
                new CsxNuGetReference("Nested.Package", "1.2.3"),
            ]);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, error.Code);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// provider が返した script 内の nested NuGet script load が lock directReferences として扱われることを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute treats nested NuGet script load as locked direct reference")]
    public void ExecuteTreatsNestedNuGetScriptLoadAsLockedDirectReference()
    {
        string markerPath = CreateMarkerPath();
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "NestedLoadedStep"));
        WriteNuGetLockFile(
            scriptPath,
            [("CsvHelper", "33.0.1"), ("Nested.Package", "1.2.3")],
            [
                ("CsvHelper", "33.0.1", true),
                ("Nested.Package", "1.2.3", true),
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0", false),
            ]);
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = new CsxNuGetDependencyGraph(
                [
                    new CsxResolvedNuGetDependency("CsvHelper", "33.0.1", isDirect: true),
                    new CsxResolvedNuGetDependency("Nested.Package", "1.2.3", isDirect: false),
                    new CsxResolvedNuGetDependency("Microsoft.Bcl.AsyncInterfaces", "8.0.0", isDirect: false),
                ],
                scripts:
                [
                    CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", "#load \"nuget: Nested.Package, 1.2.3\""),
                    CreateResolvedScript("Nested.Package", "1.2.3", "contentFiles/csx/net8.0/nested.csx", CreateLoadedStepScript("NestedLoadedStep", markerPath)),
                ],
                resolutionMetadata: CreateDefaultResolutionMetadata()),
        };
        CsxEntryLoader loader = CreateLoader(
            scriptPath,
            provider,
            [
                new CsxNuGetReference("CsvHelper", "33.0.1"),
                new CsxNuGetReference("Nested.Package", "1.2.3"),
            ]);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("NestedLoadedStep", traceStep.StepName);
        Assert.Equal("NestedLoadedStep;", File.ReadAllText(markerPath));
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// 許可外 NuGet script load が lock file 検査と provider 解決より前に拒否されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute rejects unallowed NuGet script load before lock and provider")]
    public void ExecuteRejectsUnallowedNuGetScriptLoadBeforeLockAndProvider()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "CsvHelperLoadedStep"));
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
    /// 浮動 version の NuGet script load が lock file 検査と provider 解決より前に拒否されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate rejects floating NuGet script load before lock and provider")]
    public void ValidateRejectsFloatingNuGetScriptLoadBeforeLockAndProvider()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "*", "CsvHelperLoadedStep"));
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
    /// 厳格指定された NuGet script load で lock file が無い場合に provider 解決前の専用 error code で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock missing for NuGet script load when lock is required")]
    public void ExecuteReturnsLockMissingForNuGetScriptLoadWhenLockIsRequired()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "CsvHelperLoadedStep"));
        var provider = new FakeNuGetDependencyGraphProvider();
        CsxEntryLoader loader = CreateLoader(scriptPath, provider, requireNuGetLock: true);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMissing, result.ErrorCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// NuGet script load の直接参照が lock file と異なる場合に provider 解決前の不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns lock mismatch for NuGet script direct reference before provider")]
    public void ExecuteReturnsLockMismatchForNuGetScriptDirectReferenceBeforeProvider()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "CsvHelperLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.0", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", CreateLoadedStepScript("CsvHelperLoadedStep", CreateMarkerPath()))],
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, result.ErrorCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// NuGet script load の解決済み依存が provider 解決後に lock file と異なる場合に不一致で失敗することを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate returns lock mismatch when NuGet script resolved dependency differs")]
    public void ValidateReturnsLockMismatchWhenNuGetScriptResolvedDependencyDiffers()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "CsvHelperLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "7.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", CreateLoadedStepScript("CsvHelperLoadedStep", CreateMarkerPath()))],
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowValidationResult result = loader.Validate(scriptPath);

        ValidationError error = Assert.Single(result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, error.Code);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// NuGet script load の循環が既存の循環 error code に正規化されることを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute returns load cycle detected for NuGet script load cycle")]
    public void ExecuteReturnsLoadCycleDetectedForNuGetScriptLoadCycle()
    {
        string scriptPath = CreateScript(CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "CsvHelperLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", CreateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "CsvHelperLoadedStep"))],
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptLoadCycleDetected, result.ErrorCode);
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// 同じ NuGet script load が重複しても一度だけ展開され、Step も重複しないことを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute expands duplicate NuGet script load once")]
    public void ExecuteExpandsDuplicateNuGetScriptLoadOnce()
    {
        string markerPath = CreateMarkerPath();
        string scriptPath = CreateScript(CreateDuplicateNuGetLoadWorkflowScript("CsvHelper", "33.0.1", "CsvHelperLoadedStep"));
        WriteDefaultLockFile(scriptPath, directVersion: "33.0.1", resolvedVersion: "8.0.0");
        var provider = new FakeNuGetDependencyGraphProvider
        {
            Graph = CreateGraphWithScripts(
                "CsvHelper",
                "33.0.1",
                [CreateResolvedScript("CsvHelper", "33.0.1", "contentFiles/csx/net8.0/csvhelper.csx", CreateLoadedStepScript("CsvHelperLoadedStep", markerPath, recordExpansion: true))],
                ("Microsoft.Bcl.AsyncInterfaces", "8.0.0")),
        };
        CsxEntryLoader loader = CreateLoader(scriptPath, provider);

        WorkflowResult result = loader.Execute(scriptPath);

        Assert.True(result.Succeeded);
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps);
        Assert.Equal("CsvHelperLoadedStep", traceStep.StepName);
        Assert.Equal("expanded;CsvHelperLoadedStep;", File.ReadAllText(markerPath));
        Assert.Equal(1, provider.ResolveCallCount);
    }

    /// <summary>
    /// 指定された script path に対する T27 用 loader を作成します。
    /// </summary>
    private static CsxEntryLoader CreateLoader(
        string scriptPath,
        FakeNuGetDependencyGraphProvider provider,
        IReadOnlyList<CsxNuGetReference>? allowedNuGetReferences = null,
        bool requireNuGetLock = false)
    {
        return new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            WorkflowRoot = Path.GetDirectoryName(scriptPath),
            AllowedNuGetReferences = allowedNuGetReferences ?? [new CsxNuGetReference("CsvHelper", "33.0.1")],
            RequireNuGetLock = requireNuGetLock,
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
    /// NuGet script load を含む最小の workflow script を作成します。
    /// </summary>
    private static string CreateNuGetLoadWorkflowScript(string packageId, string version, string stepTypeName)
    {
        return $$"""
            #load "nuget: {{packageId}}, {{version}}"
            using Devo6.WorkFlow.Engine;

            var Main = CompositeStep.Define("Main")
                .Run<{{stepTypeName}}, string>()
                    .StoreAs();
            """;
    }

    /// <summary>
    /// 同じ NuGet script load を二度書いた workflow script を作成します。
    /// </summary>
    private static string CreateDuplicateNuGetLoadWorkflowScript(string packageId, string version, string stepTypeName)
    {
        return $$"""
            #load "nuget: {{packageId}}, {{version}}"
            #load "nuget: {{packageId}}, {{version}}"
            using Devo6.WorkFlow.Engine;

            var Main = CompositeStep.Define("Main")
                .Run<{{stepTypeName}}, string>()
                    .StoreAs();
            """;
    }

    /// <summary>
    /// NuGet script package から解決された Step 定義 script を作成します。
    /// </summary>
    private static string CreateLoadedStepScript(string stepTypeName, string markerPath, bool recordExpansion = false)
    {
        string expansionMarker = recordExpansion
            ? $$"""
            System.IO.File.AppendAllText("{{markerPath}}", "expanded;");

            """
            : "";

        return $$"""
            using Devo6.WorkFlow.Abstractions;

            {{expansionMarker}}/// <summary>
            /// NuGet script package から読み込まれた検査用 Step です。
            /// </summary>
            public sealed class {{stepTypeName}} : IStep<string>
            {
                /// <summary>
                /// NuGet script package から読み込まれた Step の実行を marker file に記録します。
                /// </summary>
                public string Execute(StepInput input)
                {
                    System.IO.File.AppendAllText("{{markerPath}}", "{{stepTypeName}};");

                    return "{{stepTypeName}}";
                }
            }
            """;
    }

    /// <summary>
    /// marker file の一時 path を作成します。
    /// </summary>
    private static string CreateMarkerPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-workflow-nuget-load-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "marker.txt");
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
        IReadOnlyList<string>? packageSources = null,
        bool includePackageSources = true,
        bool? verifyPackageSources = null)
    {
        WriteNuGetLockFile(
            scriptPath,
            [("CsvHelper", directVersion)],
            [("CsvHelper", directVersion, true), ("Microsoft.Bcl.AsyncInterfaces", resolvedVersion, false)],
            targetFramework,
            runtimeIdentifier,
            dotnetScriptCoreVersion,
            packageSources,
            includePackageSources,
            verifyPackageSources);
    }

    /// <summary>
    /// 指定した directReferences と resolvedDependencies を持つ NuGet lock file fixture を script directory に書き込みます。
    /// </summary>
    private static void WriteNuGetLockFile(
        string scriptPath,
        IReadOnlyList<(string PackageId, string Version)> directReferences,
        IReadOnlyList<(string PackageId, string Version, bool IsDirect)> resolvedDependencies,
        string targetFramework = DefaultTargetFramework,
        string runtimeIdentifier = DefaultRuntimeIdentifier,
        string dotnetScriptCoreVersion = DefaultDotnetScriptCoreVersion,
        IReadOnlyList<string>? packageSources = null,
        bool includePackageSources = true,
        bool? verifyPackageSources = null)
    {
        string directory = Path.GetDirectoryName(scriptPath)!;
        string lockPath = Path.Combine(directory, DefaultNuGetLockFileName);
        string packageSourceVerificationYaml = verifyPackageSources.HasValue
            ? $"{Environment.NewLine}verifyPackageSources: {verifyPackageSources.Value.ToString().ToLowerInvariant()}"
            : "";
        string packageSourceYaml = includePackageSources
            ? $"{Environment.NewLine}packageSources:{Environment.NewLine}{ToPackageSourceYaml(packageSources ?? DefaultPackageSources)}"
            : "";
        string directReferenceYaml = ToDirectReferenceYaml(directReferences);
        string resolvedDependencyYaml = ToResolvedDependencyYaml(resolvedDependencies);
        File.WriteAllText(
            lockPath,
            $$"""
            version: 1
            entry: main.csx
            targetFramework: {{targetFramework}}
            runtimeIdentifier: {{runtimeIdentifier}}
            {{packageSourceVerificationYaml}}{{packageSourceYaml}}
            dotnetScriptCoreVersion: {{dotnetScriptCoreVersion}}
            directReferences:
            {{directReferenceYaml}}
            resolvedDependencies:
            {{resolvedDependencyYaml}}
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
    /// NuGet 直接参照 fixture を YAML sequence に変換します。
    /// </summary>
    private static string ToDirectReferenceYaml(IReadOnlyList<(string PackageId, string Version)> directReferences)
    {
        return string.Join(
            Environment.NewLine,
            directReferences.Select(reference => $"  - packageId: {reference.PackageId}{Environment.NewLine}    version: {reference.Version}"));
    }

    /// <summary>
    /// NuGet 解決済み依存 fixture を YAML sequence に変換します。
    /// </summary>
    private static string ToResolvedDependencyYaml(IReadOnlyList<(string PackageId, string Version, bool IsDirect)> resolvedDependencies)
    {
        return string.Join(
            Environment.NewLine,
            resolvedDependencies.Select(dependency =>
                $"  - packageId: {dependency.PackageId}{Environment.NewLine}    version: {dependency.Version}{Environment.NewLine}    direct: {dependency.IsDirect.ToString().ToLowerInvariant()}"));
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
    /// fake provider が返す script 解決情報付きの NuGet dependency graph を作成します。
    /// </summary>
    private static CsxNuGetDependencyGraph CreateGraphWithScripts(
        string directPackageId,
        string directVersion,
        IReadOnlyList<CsxResolvedNuGetScript> scripts,
        params (string PackageId, string Version)[] transitiveDependencies)
    {
        var dependencies = new List<CsxResolvedNuGetDependency>
        {
            new(directPackageId, directVersion, isDirect: true),
        };

        dependencies.AddRange(transitiveDependencies.Select(
            dependency => new CsxResolvedNuGetDependency(dependency.PackageId, dependency.Version, isDirect: false)));

        return new CsxNuGetDependencyGraph(dependencies, scripts: scripts, resolutionMetadata: CreateDefaultResolutionMetadata());
    }

    /// <summary>
    /// fake provider が返す NuGet script 解決情報を作成します。
    /// </summary>
    private static CsxResolvedNuGetScript CreateResolvedScript(string packageId, string version, string scriptPath, string sourceCode)
    {
        return new CsxResolvedNuGetScript(packageId, version, scriptPath, sourceCode);
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
