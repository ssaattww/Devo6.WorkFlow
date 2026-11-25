using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Cli;
using Devo6.WorkFlow.Engine;
using System.Collections;

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
    /// .cs CLI の validate / run は --allow-nuget の外にある直接依存を SDK 起動前に拒否します。
    /// </summary>
#if NET10_0
    [Theory(DisplayName = ".cs validate and run enforce the NuGet allow list before SDK restore")]
    [InlineData("validate")]
    [InlineData("run")]
    public void CliRejectsDisallowedPackageBeforeSdkRestore(string command)
    {
        string sourcePath = CreateFileBasedSource("#:package Unauthorized.Package@[1.2.3]");
        var runner = new RecordingSdkRunner();
        var builder = new FileBasedCsWorkflowBuilder(runner);

        TextWriter originalError = Console.Error;
        using var capturedError = new StringWriter();
        int exitCode;
        try
        {
            Console.SetError(capturedError);
            exitCode = Program.Run(
                [command, sourcePath, "--allow-nuget", "Devo6.WorkFlow.Engine,0.1.0"],
                hostTargetFramework: Net10Host,
                hostPackageVersion: "0.1.0",
                fileBasedBuilderFactory: () => builder);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(1, exitCode);
        Assert.Contains(WorkflowErrorCodes.ScriptReferenceNotAllowed, capturedError.ToString());
        Assert.Empty(runner.Calls);
    }
#endif

    /// <summary>
    /// SDK の基準版未満は evaluation / restore より前に失敗します。
    /// </summary>
    [Fact(DisplayName = "SDK below the minimum version is rejected before evaluation and restore")]
    public void UnsupportedSdkDoesNotInvokeEvaluationOrRestore()
    {
        string sourcePath = CreateFileBasedSource();
        string directory = Path.GetDirectoryName(sourcePath)!;
        string feed = Path.Combine(directory, "feed");
        Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"), $"<configuration><packageSources><clear/><add key=\"local\" value=\"{feed}\" /></packageSources></configuration>");
        File.WriteAllText(Path.Combine(directory, "devo6.nuget.lock.yaml"), $$"""
            version: 1
            targetFramework: net10.0
            verifyPackageSources: true
            packageSources:
              - "{{feed}}"
            directReferences:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            resolvedDependencies:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            """);
        var runner = new RecordingSdkRunner("10.0.403");
        var builder = new FileBasedCsWorkflowBuilder(runner, () => Path.Combine(directory, "sdk-output"));

        FileBasedCsBuildResult result = builder.Build(sourcePath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptSdkUnavailable, result.ErrorCode);
        Assert.Equal(["--version"], Assert.Single(runner.Calls));
    }

    [Fact(DisplayName = "unverified effective project data stops before restore")]
    public void UnsupportedEffectiveTargetDoesNotInvokeRestore()
    {
        string sourcePath = CreateFileBasedSource();
        string directory = Path.GetDirectoryName(sourcePath)!;
        string feed = Path.Combine(directory, "feed");
        Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"), $"<configuration><packageSources><clear/><add key=\"local\" value=\"{feed}\" /></packageSources></configuration>");
        File.WriteAllText(Path.Combine(directory, "devo6.nuget.lock.yaml"), $$"""
            version: 1
            targetFramework: net10.0
            verifyPackageSources: true
            packageSources:
              - "{{feed}}"
            directReferences:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            resolvedDependencies:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            """);
        string evaluationJson = $$"""
            {
              "Properties": { "TargetFramework": "net8.0", "TargetFrameworks": "", "OutputType": "Library", "PublishAot": "false", "RestorePackagesWithLockFile": "", "RestoreLockedMode": "", "ManagePackageVersionsCentrally": "", "RestoreAdditionalProjectSources": "", "RestoreConfigFile": "", "RestoreSources": "" },
              "Items": { "PackageReference": [ { "Identity": "Devo6.WorkFlow.Engine", "Version": "[0.1.0]" } ] }
            }
            """;
        var runner = new RecordingSdkRunner("10.0.404", evaluationJson);
        var builder = new FileBasedCsWorkflowBuilder(runner, () => Path.Combine(directory, "sdk-output"));

        FileBasedCsBuildResult result = builder.Build(sourcePath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, result.ErrorCode);
        Assert.Equal(2, runner.Calls.Count);
        Assert.DoesNotContain(runner.Calls, call => call.Count > 0 && call[0] == "restore");
    }

    [Fact(DisplayName = "MSBuild property environment overrides fail before SDK invocation")]
    public void EnvironmentOverrideDoesNotInvokeSdk()
    {
        string sourcePath = CreateFileBasedSource();
        var variables = new Hashtable(StringComparer.OrdinalIgnoreCase) { ["TargetFramework"] = "net8.0" };

        bool valid = CsFileInputPreflight.ValidateBuildEnvironment(sourcePath, out string error, variables);

        Assert.False(valid);
        Assert.Contains("TargetFramework", error);
    }

    [Fact(DisplayName = "inherited Directory.Build.props is rejected before SDK invocation")]
    public void InheritedMsBuildPropsDoesNotInvokeSdk()
    {
        string sourcePath = CreateFileBasedSource();
        string propsPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "Directory.Build.props");
        File.WriteAllText(propsPath, "<Project />");
        var runner = new RecordingSdkRunner();
        var builder = new FileBasedCsWorkflowBuilder(runner);

        FileBasedCsBuildResult result = builder.Build(sourcePath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, result.ErrorCode);
        Assert.Empty(runner.Calls);
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
        private readonly string sdkVersion;
        private readonly string evaluationJson;

        public RecordingSdkRunner(string sdkVersion = "10.0.404", string evaluationJson = "{}")
        {
            this.sdkVersion = sdkVersion;
            this.evaluationJson = evaluationJson;
        }
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
            if (arguments.Count == 1 && arguments[0] == "--version")
            {
                return new FileBasedSdkProcessResult(0, sdkVersion, "");
            }

            return new FileBasedSdkProcessResult(0, evaluationJson, "");
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
