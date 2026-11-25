using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Cli;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// 新しい file-based .cs 入力の復元前ゲートを検査します。
/// </summary>
public sealed class CsFileInputPreflightTests
{
    private const string Net8Host = ".NETCoreApp,Version=v8.0";
    private const string Net10Host = ".NETCoreApp,Version=v10.0";

    /// <summary>
    /// net8.0 ホストでは .cs 入力を lock 検査や loader 呼び出しより前に拒否します。
    /// </summary>
    [Fact(DisplayName = "net8 host rejects .cs input before NuGet resolution")]
    public void Net8HostRejectsCsBeforeNuGetResolution()
    {
        string sourcePath = CreateFileBasedSource();
        var provider = new CountingGraphProvider();

        CsFileInputPreflight.PreflightResult preflight = CsFileInputPreflight.Validate(sourcePath, Net8Host);
        int exitCode = Program.Run(["validate", sourcePath], provider, Net8Host);

        Assert.False(preflight.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptFileRequiresNet10Host, preflight.ErrorCode);
        Assert.Equal(1, exitCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// net10.0 ホストでも Engine の推移依存を含む入力は専用 lock 欠落時に復元前拒否します。
    /// </summary>
    [Fact(DisplayName = "net10 host rejects .cs input when the mandatory lock file is missing")]
    public void Net10HostRejectsMissingMandatoryLockBeforeNuGetResolution()
    {
        string sourcePath = CreateFileBasedSource();
        var provider = new CountingGraphProvider();

        CsFileInputPreflight.PreflightResult preflight = CsFileInputPreflight.Validate(sourcePath, Net10Host);
        int exitCode = Program.Run(["run", sourcePath], provider, Net10Host);

        Assert.False(preflight.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMissing, preflight.ErrorCode);
        Assert.Equal(1, exitCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// 不正な directive と可変版を restore 前に拒否します。
    /// </summary>
    [Theory(DisplayName = "unsupported .cs directive fails before NuGet resolution")]
    [InlineData("#:sdk Microsoft.NET.Sdk")]
    [InlineData("  #:sdk Microsoft.NET.Sdk")]
    [InlineData("#:package Devo6.WorkFlow.Engine@1.0.*")]
    [InlineData("#:property TargetFramework=net8.0")]
    public void InvalidDirectiveFailsBeforeNuGetResolution(string invalidDirective)
    {
        string sourcePath = CreateFileBasedSource(invalidDirective);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(sourcePath)!, "devo6.nuget.lock.yaml"), "version: 1\n");
        var provider = new CountingGraphProvider();

        CsFileInputPreflight.PreflightResult preflight = CsFileInputPreflight.Validate(sourcePath, Net10Host);
        int exitCode = Program.Run(["validate", sourcePath], provider, Net10Host);

        Assert.False(preflight.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptFileDirectiveInvalid, preflight.ErrorCode);
        Assert.Equal(1, exitCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

    /// <summary>
    /// 正しい復元前条件を通っても未実装の build / validate / run を成功扱いしません。
    /// </summary>
    [Fact(DisplayName = "valid .cs preflight does not claim build or execution success")]
    public void ValidPreflightStillFailsUntilBuildAndExecutionAreImplemented()
    {
        string sourcePath = CreateFileBasedSource();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(sourcePath)!, "devo6.nuget.lock.yaml"), "version: 1\n");
        var provider = new CountingGraphProvider();

        CsFileInputPreflight.PreflightResult preflight = CsFileInputPreflight.Validate(sourcePath, Net10Host);
        int exitCode = Program.Run(["run", sourcePath], provider, Net10Host);

        Assert.True(preflight.Succeeded);
        Assert.Equal(1, exitCode);
        Assert.Equal(0, provider.ResolveCallCount);
    }

#if NET10_0
    /// <summary>
    /// Engine だけを参照する .cs でも lock file が無ければ SDK runner の restore を呼びません。
    /// </summary>
    [Fact(DisplayName = "net10 Engine-only source rejects missing lock before SDK restore")]
    public void EngineOnlyMissingLockDoesNotInvokeRestore()
    {
        string sourcePath = CreateFileBasedSource();
        var runner = new RecordingSdkRunner();
        var builder = new FileBasedCsWorkflowBuilder(runner);

        FileBasedCsBuildResult result = builder.Build(sourcePath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.True(
            result.ErrorCode == WorkflowErrorCodes.ScriptNugetLockMissing,
            $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.DoesNotContain(runner.Calls, call => call.Count > 0 && call[0] == "restore");
        Assert.Empty(runner.Calls);
    }

    /// <summary>
    /// Engine package version がホスト package version と異なる場合に restore を呼びません。
    /// </summary>
    [Fact(DisplayName = "net10 host rejects a different Engine package version before SDK restore")]
    public void EnginePackageVersionMismatchDoesNotInvokeRestore()
    {
        string sourcePath = CreateFileBasedSource();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(sourcePath)!, "devo6.nuget.lock.yaml"), "version: 1\n");
        var runner = new RecordingSdkRunner();
        var builder = new FileBasedCsWorkflowBuilder(runner);

        FileBasedCsBuildResult result = builder.Build(sourcePath, "0.2.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptApiIdentityMismatch, result.ErrorCode);
        Assert.Empty(runner.Calls);
    }

    /// <summary>
    /// 配布 package 版が不明なら SDK 起動前に明示失敗します。
    /// </summary>
    [Fact(DisplayName = "net10 host rejects an unknown host package version before SDK restore")]
    public void UnknownHostPackageVersionDoesNotInvokeSdk()
    {
        string sourcePath = CreateFileBasedSource();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(sourcePath)!, "devo6.nuget.lock.yaml"), "version: 1\n");
        var runner = new RecordingSdkRunner();
        var builder = new FileBasedCsWorkflowBuilder(runner);

        FileBasedCsBuildResult result = builder.Build(sourcePath, "");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.PackageVersionUnknown, result.ErrorCode);
        Assert.Empty(runner.Calls);
    }

    /// <summary>
    /// 不許可 directive と不正 lock は SDK を起動する前に拒否します。
    /// </summary>
    [Theory(DisplayName = "invalid .cs preflight inputs do not invoke the SDK runner")]
    [InlineData("#:sdk Microsoft.NET.Sdk", "version: 1\n")]
    [InlineData(null, "version: 2\n")]
    public void InvalidPreflightDoesNotInvokeSdk(string? additionalDirective, string lockContents)
    {
        string sourcePath = CreateFileBasedSource(additionalDirective);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(sourcePath)!, "devo6.nuget.lock.yaml"), lockContents);
        var runner = new RecordingSdkRunner();
        var builder = new FileBasedCsWorkflowBuilder(runner);

        FileBasedCsBuildResult result = builder.Build(sourcePath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
    }

    /// <summary>
    /// 指定した Config がなければ build と metadata validate より前に失敗します。
    /// </summary>
    [Fact(DisplayName = "missing workflow config fails before SDK restore")]
    public void MissingConfigDoesNotInvokeSdk()
    {
        string sourcePath = CreateFileBasedSource();
        var runner = new RecordingSdkRunner();
        var builder = new FileBasedCsWorkflowBuilder(runner);
        const string missingConfigPath = "missing.yaml";

        WorkflowValidationResult result = builder.Validate(
            sourcePath,
            "0.1.0",
            configPaths: [missingConfigPath]);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ConfigNotFound, Assert.Single(result.Errors).Code);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(sourcePath)!, missingConfigPath), Assert.Single(result.Errors).Path);
        Assert.Empty(runner.Calls);
    }

    private sealed class RecordingSdkRunner : IFileBasedSdkRunner
    {
        /// <summary>
        /// SDK runner が受け取った呼び出し列を取得します。
        /// </summary>
        public List<IReadOnlyList<string>> Calls { get; } = [];

        /// <summary>
        /// SDK の file-based restore または build process を実行します。
        /// </summary>
        public FileBasedSdkProcessResult Run(
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            return new FileBasedSdkProcessResult(0, "10.0.404", "");
        }
    }
#endif

    /// <summary>
    /// file-based workflow のテスト入力を作成します。
    /// </summary>
    private static string CreateFileBasedSource(string? additionalDirective = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-workflow-cs-preflight-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "main.cs");
        File.WriteAllText(
            sourcePath,
            string.Join(Environment.NewLine,
            [
                "#:property OutputType=Library",
                "#:property TargetFramework=net10.0",
                "#:property PublishAot=false",
                "#:package Devo6.WorkFlow.Engine@[0.1.0]",
                additionalDirective ?? "",
                "public sealed class MainWorkflow { }",
            ]));

        return sourcePath;
    }

    private sealed class CountingGraphProvider : ICsxNuGetDependencyGraphProvider
    {
        /// <summary>
        /// dependency graph resolver の呼出数を取得します。
        /// </summary>
        public int ResolveCallCount { get; private set; }

        /// <summary>
        /// NuGet dependency graph のテスト用解決結果を返します。
        /// </summary>
        public CsxNuGetDependencyGraph Resolve(
            IReadOnlyList<CsxNuGetReference> directReferences,
            CsxNuGetDependencyGraphRequest request)
        {
            ResolveCallCount++;
            return new CsxNuGetDependencyGraph([]);
        }
    }
}
