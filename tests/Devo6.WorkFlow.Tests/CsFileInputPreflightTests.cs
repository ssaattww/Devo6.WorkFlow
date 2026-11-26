using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Cli;
using Devo6.WorkFlow.Engine;
using System.Collections;
using System.Text.Json;

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

#if NET10_0
    /// <summary>
    /// .cs CLI の validate / run は --allow-nuget の外にある直接依存を SDK 起動前に拒否します。
    /// </summary>
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

    /// <summary>
    /// 配布 .nuspec の列挙に失敗したとき run と validate を PACKAGE_VERSION_UNKNOWN に統一します。
    /// </summary>
    [Theory(DisplayName = "package version enumeration failure has one diagnostic for run and validate")]
    [InlineData("run")]
    [InlineData("validate")]
    public void PackageVersionEnumerationFailureUsesUnknownDiagnostic(string command)
    {
        string sourcePath = CreateFileBasedSource();
        string directory = Path.GetDirectoryName(sourcePath)!;
        string feedPath = Path.Combine(directory, "feed");
        Directory.CreateDirectory(feedPath);
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
            $"<configuration><packageSources><clear/><add key=\"local\" value=\"{feedPath}\" /></packageSources></configuration>");
        File.WriteAllText(Path.Combine(directory, "devo6.nuget.lock.yaml"), $$"""
            version: 1
            targetFramework: net10.0
            verifyPackageSources: true
            packageSources:
              - "{{feedPath}}"
            directReferences:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            resolvedDependencies:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            """);

        var runner = new RecordingSdkRunner();
        var builder = new FileBasedCsWorkflowBuilder(runner);
        TextWriter originalError = Console.Error;
        using var capturedError = new StringWriter();
        int exitCode;
        try
        {
            Console.SetError(capturedError);
            exitCode = Program.Run(
                [command, sourcePath],
                hostTargetFramework: Net10Host,
                packageVersionReader: _ => throw new UnauthorizedAccessException("fixture enumeration denied"),
                fileBasedBuilderFactory: () => builder);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(1, exitCode);
        Assert.Contains(WorkflowErrorCodes.PackageVersionUnknown, capturedError.ToString(), StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    /// <summary>
    /// nuspec directory enumeration failures are returned as an unknown version.
    /// </summary>
    [Fact(DisplayName = "tool package version reader contains directory enumeration failures")]
    public void PackageVersionReaderReturnsUnknownWhenEnumerationFails()
    {
        string version = ToolPackageVersionReader.ReadInstalledPackageVersion(
            Path.GetTempPath(),
            _ => throw new IOException("fixture enumeration failed")) ?? "";

        Assert.Equal("", version);
    }

    /// <summary>
    /// build の実行中に元入力が変化した場合、既に出力 DLL があっても成果物を返しません。
    /// </summary>
    [Fact(DisplayName = "builder rejects a generated assembly when its input changes during build")]
    public void BuilderDiscardsAssemblyAfterInputMutationDuringBuild()
    {
        string sourcePath = CreateFileBasedSource();
        string directory = Path.GetDirectoryName(sourcePath)!;
        string feedPath = Path.Combine(directory, "feed");
        Directory.CreateDirectory(feedPath);
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
            $"<configuration><packageSources><clear/><add key=\"local\" value=\"{feedPath}\" /></packageSources></configuration>");
        File.WriteAllText(Path.Combine(directory, "devo6.nuget.lock.yaml"), $$"""
            version: 1
            targetFramework: net10.0
            verifyPackageSources: true
            packageSources:
              - "{{feedPath}}"
            directReferences:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            resolvedDependencies:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            """);
        string outputRoot = Path.Combine(directory, "sdk-output");
        var runner = new InputChangingSdkRunner(sourcePath, feedPath, outputRoot);
        var builder = new FileBasedCsWorkflowBuilder(runner, () => outputRoot);

        FileBasedCsBuildResult result = builder.Build(sourcePath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptFileDirectiveInvalid, result.ErrorCode);
        Assert.Null(result.AssemblyPath);
        Assert.NotNull(runner.GeneratedAssemblyPath);
        Assert.True(File.Exists(runner.GeneratedAssemblyPath));
        Assert.Contains(runner.Calls, call => call.Contains("restore", StringComparer.Ordinal));
        Assert.Contains(runner.Calls, call => call.Contains("--no-restore", StringComparer.Ordinal));
        Assert.NotNull(runner.RunContext);
        Assert.Contains(runner.Calls, call => call.Contains(
            $"-p:RestorePackagesPath={runner.RunContext.NuGetPackagesDirectory}", StringComparer.Ordinal));
        Assert.Equal(runner.RunContext.SessionDirectory, result.TemporaryDirectory);
        Assert.True(Directory.Exists(result.TemporaryDirectory));
        Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// build 後に入力を再読込できない場合は診断し、既存 DLL を返しません。
    /// </summary>
    [Fact(DisplayName = "builder discards the assembly when the input cannot be reread after build")]
    public void BuilderDiscardsAssemblyWhenSourceDisappearsDuringBuild()
    {
        string sourcePath = CreateFileBasedSource();
        string directory = Path.GetDirectoryName(sourcePath)!;
        string feedPath = Path.Combine(directory, "feed");
        Directory.CreateDirectory(feedPath);
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
            $"<configuration><packageSources><clear/><add key=\"local\" value=\"{feedPath}\" /></packageSources></configuration>");
        File.WriteAllText(Path.Combine(directory, "devo6.nuget.lock.yaml"), $$"""
            version: 1
            targetFramework: net10.0
            verifyPackageSources: true
            packageSources:
              - "{{feedPath}}"
            directReferences:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            resolvedDependencies:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            """);
        string outputRoot = Path.Combine(directory, "sdk-output");
        var runner = new InputChangingSdkRunner(sourcePath, feedPath, outputRoot, deleteSourceDuringBuild: true);
        var builder = new FileBasedCsWorkflowBuilder(runner, () => outputRoot);

        FileBasedCsBuildResult result = builder.Build(sourcePath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptFileDirectiveInvalid, result.ErrorCode);
        Assert.Null(result.AssemblyPath);
        Assert.NotNull(runner.GeneratedAssemblyPath);
        Assert.True(File.Exists(runner.GeneratedAssemblyPath));
        Assert.False(File.Exists(sourcePath));
        Assert.NotNull(result.TemporaryDirectory);
        Assert.True(Directory.Exists(result.TemporaryDirectory));
        Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// 復元 assets が別の package cache を記録している場合は生成前に拒否します。
    /// </summary>
    [Fact(DisplayName = "builder rejects restore assets outside the request-owned NuGet cache")]
    public void BuilderRejectsAssetsUsingDifferentNuGetCache()
    {
        string sourcePath = CreateFileBasedSource();
        string directory = Path.GetDirectoryName(sourcePath)!;
        string feedPath = Path.Combine(directory, "feed");
        Directory.CreateDirectory(feedPath);
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
            $"<configuration><packageSources><clear/><add key=\"local\" value=\"{feedPath}\" /></packageSources></configuration>");
        File.WriteAllText(Path.Combine(directory, "devo6.nuget.lock.yaml"), $$"""
            version: 1
            targetFramework: net10.0
            verifyPackageSources: true
            packageSources:
              - "{{feedPath}}"
            directReferences:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            resolvedDependencies:
              - packageId: Devo6.WorkFlow.Engine
                version: 0.1.0
                direct: true
            """);
        string outputRoot = Path.Combine(directory, "sdk-output");
        var runner = new InputChangingSdkRunner(sourcePath, feedPath, outputRoot, useForeignNuGetCacheInAssets: true);
        var builder = new FileBasedCsWorkflowBuilder(runner, () => outputRoot);

        WorkflowResult result = builder.Run(sourcePath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptNugetLockMismatch, result.ErrorCode);
        Assert.NotNull(runner.RunContext);
        Assert.Contains(runner.RunContext.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(Directory.Exists(runner.RunContext.SessionDirectory));
        Assert.DoesNotContain(runner.Calls, call => call.FirstOrDefault() == "build"
            && !call.Any(argument => argument.StartsWith("-getProperty:", StringComparison.Ordinal)));
        Directory.Delete(directory, recursive: true);
    }
#endif

#if NET10_0
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

    /// <summary>
    /// 評価済み TFM が契約外なら restore 前に停止します。
    /// </summary>
    [Fact(DisplayName = "an unapproved MSBuild SDK path stops before restore")]
    public void UnapprovedSdkPathDoesNotInvokeRestore()
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
              "Properties": { "TargetFramework": "net10.0", "TargetFrameworks": "", "OutputType": "Library", "PublishAot": "false", "NETCoreSdkVersion": "10.0.404", "RestorePackagesWithLockFile": "", "RestoreLockedMode": "", "ManagePackageVersionsCentrally": "", "RestoreAdditionalProjectSources": "", "RestoreConfigFile": "", "RestoreSources": "", "MSBuildSDKsPath": "/opt/override/sdk/10.0.404/Sdks", "MSBuildExtensionsPath": "/usr/share/dotnet/sdk/10.0.404/" },
              "Items": { "PackageReference": [ { "Identity": "Devo6.WorkFlow.Engine", "Version": "[0.1.0]" } ] }
            }
            """;
        var runner = new RecordingSdkRunner("10.0.404", evaluationJson);
        int outputRootProviderCalls = 0;
        var builder = new FileBasedCsWorkflowBuilder(runner, () =>
        {
            outputRootProviderCalls++;
            return Path.Combine(directory, "sdk-output");
        });

        FileBasedCsBuildResult result = builder.Build(sourcePath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, result.ErrorCode);
        Assert.Equal(5, runner.Calls.Count);
        Assert.Equal(["--version"], runner.Calls[0]);
        Assert.Equal(["--info"], runner.Calls[1]);
        Assert.Equal(1, outputRootProviderCalls);
        Assert.DoesNotContain(runner.Calls, call => call.Count > 0 && call[0] == "restore");
    }

    /// <summary>
    /// カスタム import を指す環境変数を復元前に拒否します。
    /// </summary>
    [Fact(DisplayName = "MSBuild property environment overrides fail before SDK invocation")]
    public void EnvironmentOverrideDoesNotInvokeSdk()
    {
        string sourcePath = CreateFileBasedSource();
        var variables = new Hashtable(StringComparer.OrdinalIgnoreCase) { ["CustomBeforeMicrosoftCommonProps"] = "custom.props" };

        bool valid = CsFileInputPreflight.ValidateBuildEnvironment(sourcePath, out string error, variables);

        Assert.False(valid);
        Assert.Contains("CustomBeforeMicrosoftCommonProps", error);

        var selectedSdkEnvironment = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildSDKsPath"] = "/usr/share/dotnet/sdk/10.0.404/Sdks",
        };
        Assert.True(CsFileInputPreflight.ValidateBuildEnvironment(sourcePath, out _, selectedSdkEnvironment));
    }

    /// <summary>
    /// 継承した Directory.Build.props は SDK 起動前に拒否します。
    /// </summary>
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
#endif

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

    /// <summary>
    /// SDK command arguments and results for preflight tests.
    /// </summary>
    private sealed class RecordingSdkRunner : IFileBasedSdkRunner
    {
        private readonly string sdkVersion;
        private readonly string evaluationJson;
        private const string SdkInfo = ".NET SDK:\n Version: 10.0.404\n Base Path: /usr/share/dotnet/sdk/10.0.404/";

        /// <summary>
        /// SDK version and evaluation-only JSON output を指定します。
        /// </summary>
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
            CancellationToken cancellationToken,
            FileBasedSdkRunContext? runContext = null)
        {
            Calls.Add(arguments);
            if (arguments.Count == 1 && arguments[0] == "--version")
            {
                return new FileBasedSdkProcessResult(0, sdkVersion, "");
            }

            if (arguments.Count == 1 && arguments[0] == "--info")
            {
                return new FileBasedSdkProcessResult(0, SdkInfo, "");
            }

            return new FileBasedSdkProcessResult(0, evaluationJson, "");
        }
    }

    /// <summary>
    /// SDK build 中に入力を変更し、stale assembly が破棄されることを検査します。
    /// </summary>
    /// <param name="entryPath">検査対象の workflow source。</param>
    /// <param name="feedPath">SDK fixture の package source。</param>
    /// <param name="outputRoot">SDK runfile の分離 output root。</param>
    private sealed class InputChangingSdkRunner(
        string entryPath,
        string feedPath,
        string outputRoot,
        bool deleteSourceDuringBuild = false,
        bool useForeignNuGetCacheInAssets = false) : IFileBasedSdkRunner
    {
        private const string SdkVersion = "10.0.404";
        /// <summary>
        /// SDK fixture が処理した引数列を取得します。
        /// </summary>
        public List<IReadOnlyList<string>> Calls { get; } = [];

        /// <summary>
        /// SDK が build 時に出力した fixture assembly path。
        /// </summary>
        public string? GeneratedAssemblyPath { get; private set; }

        /// <summary>
        /// SDK process に渡された要求専用 cache context。
        /// </summary>
        public FileBasedSdkRunContext? RunContext { get; private set; }

        /// <summary>
        /// SDK の復元・評価・生成を模擬し、生成中の入力変更を発生させます。
        /// </summary>
        public FileBasedSdkProcessResult Run(
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            FileBasedSdkRunContext? runContext = null)
        {
            Calls.Add(arguments.ToArray());
            RunContext = runContext;
            if (arguments.SequenceEqual(["--version"], StringComparer.Ordinal))
            {
                return new FileBasedSdkProcessResult(0, SdkVersion, "");
            }

            if (arguments.SequenceEqual(["--info"], StringComparer.Ordinal))
            {
                return new FileBasedSdkProcessResult(0, ".NET SDK:\n Version: 10.0.404\n Base Path: /usr/share/dotnet/sdk/10.0.404/", "");
            }

            string artifactsDirectory = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(entryPath, runContext?.RunFileRoot ?? outputRoot);
            if (arguments.Any(argument => argument.StartsWith("-getProperty:", StringComparison.Ordinal)))
            {
                return new FileBasedSdkProcessResult(0, EvaluationJson(
                    runContext?.RunFileRoot ?? outputRoot,
                    runContext?.NuGetPackagesDirectory ?? Path.Combine(outputRoot, "nuget-packages")), "");
            }

            if (arguments.FirstOrDefault() == "restore")
            {
                string packagesDirectory = runContext!.NuGetPackagesDirectory;
                if (useForeignNuGetCacheInAssets)
                {
                    packagesDirectory = Path.Combine(Path.GetDirectoryName(packagesDirectory)!, "other-packages");
                }
                string assetsPath = Path.Combine(artifactsDirectory, "obj", "project.assets.json");
                Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
                File.WriteAllText(assetsPath, JsonSerializer.Serialize(new
                {
                    targets = new Dictionary<string, object> { ["net10.0"] = new Dictionary<string, object>() },
                    packageFolders = new Dictionary<string, object>
                    {
                        [packagesDirectory] = new { },
                    },
                    libraries = new Dictionary<string, object>
                    {
                        ["Devo6.WorkFlow.Engine/0.1.0"] = new { type = "package" },
                    },
                    project = new
                    {
                        frameworks = new Dictionary<string, object>
                        {
                            ["net10.0"] = new
                            {
                                dependencies = new Dictionary<string, object>
                                {
                                    ["Devo6.WorkFlow.Engine"] = new { target = "Package", version = "[0.1.0, 0.1.0]" },
                                },
                            },
                        },
                        restore = new
                        {
                            projectPath = Path.GetFullPath(entryPath) + ".csproj",
                            packagesPath = packagesDirectory,
                            sources = new Dictionary<string, object> { [feedPath] = new { } },
                        },
                    },
                }));
                return new FileBasedSdkProcessResult(0, "", "");
            }

            if (arguments.FirstOrDefault() == "build")
            {
                string assemblyPath = FileBasedCsWorkflowBuilder.GetSdkOutputAssemblyPath(entryPath, runContext?.RunFileRoot ?? outputRoot);
                GeneratedAssemblyPath = assemblyPath;
                Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
                File.WriteAllText(assemblyPath, "stale fixture assembly");
                if (deleteSourceDuringBuild)
                {
                    File.Delete(entryPath);
                }
                else
                {
                    File.AppendAllText(entryPath, Environment.NewLine + "// changed while building");
                }
                return new FileBasedSdkProcessResult(0, "", "");
            }

            throw new InvalidOperationException($"Unexpected SDK fixture invocation: {string.Join(' ', arguments)}");
        }

        /// <summary>
        /// SDK が返す実効 build path と PackageReference を表す JSON を作成します。
        /// </summary>
        /// <returns>有効な file-based project evaluation JSON。</returns>
        private string EvaluationJson(string sdkRunFileRoot, string nugetPackagesDirectory)
        {
            string artifacts = Path.GetFullPath(FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(entryPath, sdkRunFileRoot));
            string bin = Path.Combine(artifacts, "bin");
            string output = Path.Combine(bin, "debug");
            string obj = Path.Combine(artifacts, "obj");
            return JsonSerializer.Serialize(new
            {
                Properties = new Dictionary<string, string>
                {
                    ["TargetFramework"] = "net10.0",
                    ["TargetFrameworks"] = "",
                    ["OutputType"] = "Library",
                    ["PublishAot"] = "false",
                    ["NETCoreSdkVersion"] = SdkVersion,
                    ["RestorePackagesWithLockFile"] = "false",
                    ["RestoreLockedMode"] = "false",
                    ["ManagePackageVersionsCentrally"] = "false",
                    ["RestoreAdditionalProjectSources"] = "",
                    ["RestoreConfigFile"] = "",
                    ["RestoreSources"] = feedPath,
                    ["RestorePackagesPath"] = nugetPackagesDirectory,
                    ["MSBuildSDKsPath"] = "/usr/share/dotnet/sdk/10.0.404/Sdks",
                    ["MSBuildExtensionsPath"] = "/usr/share/dotnet/sdk/10.0.404/",
                    ["BaseOutputPath"] = bin + Path.DirectorySeparatorChar,
                    ["OutputPath"] = output + Path.DirectorySeparatorChar,
                    ["OutDir"] = output + Path.DirectorySeparatorChar,
                    ["BaseIntermediateOutputPath"] = obj + Path.DirectorySeparatorChar,
                    ["IntermediateOutputPath"] = Path.Combine(obj, "debug") + Path.DirectorySeparatorChar,
                    ["MSBuildProjectExtensionsPath"] = obj + Path.DirectorySeparatorChar,
                    ["TargetPath"] = Path.Combine(output, Path.GetFileNameWithoutExtension(entryPath) + ".dll"),
                    ["ArtifactsPath"] = artifacts,
                    ["RuntimeIdentifier"] = "",
                    ["RuntimeIdentifiers"] = "",
                },
                Items = new
                {
                    PackageReference = new[]
                    {
                        new { Identity = "Devo6.WorkFlow.Engine", Version = "[0.1.0]" },
                    },
                },
            });
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
