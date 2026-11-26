#if NET10_0
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// File-based Provider execution shares host types and checks Entry identity before running Steps.
/// </summary>
public sealed class FileBasedWorkflowExecutorTests
{
    /// <summary>
    /// 互換 runtime asset は低い参照版を満たし、上位版や異なる鍵は満たさないことを確認します。
    /// </summary>
    [Fact(DisplayName = "shared framework assets may satisfy compatible reference versions")]
    public void SharedFrameworkCompatibilityAllowsLowerRequestedVersion()
    {
        AssemblyName available = typeof(object).Assembly.GetName();
        var lowerVersion = new AssemblyName
        {
            Name = available.Name,
            Version = new Version(Math.Max(0, available.Version!.Major - 1), 0, 0, 0),
            CultureName = available.CultureName,
        };
        lowerVersion.SetPublicKeyToken(available.GetPublicKeyToken() ?? []);
        var higherVersion = new AssemblyName
        {
            Name = available.Name,
            Version = new Version(available.Version.Major + 1, 0, 0, 0),
            CultureName = available.CultureName,
        };
        higherVersion.SetPublicKeyToken(available.GetPublicKeyToken() ?? []);
        var differentKey = new AssemblyName
        {
            Name = available.Name,
            Version = lowerVersion.Version,
            CultureName = available.CultureName,
        };
        differentKey.SetPublicKeyToken([1, 2, 3, 4]);

        Assert.True(FileBasedWorkflowLoadContext.IsCompatibleFrameworkAssemblyIdentity(lowerVersion, available));
        Assert.False(FileBasedWorkflowLoadContext.IsCompatibleFrameworkAssemblyIdentity(higherVersion, available));
        Assert.False(FileBasedWorkflowLoadContext.IsCompatibleFrameworkAssemblyIdentity(differentKey, available));
    }

    /// <summary>
    /// 実在する private asset を shared framework 名の asset より先に選びます。
    /// </summary>
    [Fact(DisplayName = "private dependency resolution precedes shared framework fallback in preflight and execution")]
    public void PrivateDependencyWithSharedFrameworkNameUsesTheSameAssetDuringPreloadAndExecution()
    {
        string directory = CreateDirectory();
        string marker = Path.Combine(directory, "private-framework-name-loaded");
        string dependencyPath = Path.Combine(directory, "System.Private.Uri.dll");
        CompileFixture(
            directory,
            "namespace LocalDependency; public static class Hook { public static void Touch(string path) => System.IO.File.WriteAllText(path, \"private\"); }",
            dependencyPath,
            excludedPlatformAssemblyNames: ["System.Private.Uri"]);
        string escapedMarker = marker.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        string source = $$"""
            using Devo6.WorkFlow.Engine;
            using LocalDependency;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public MainProvider() => Hook.Touch("{{escapedMarker}}");
                public IWorkflowEntry Create() => CompositeStep.Define("Main").Run<int>("step", _ => 1);
            }
            """;
        string assemblyPath = CompileFixture(
            directory,
            source,
            Path.Combine(directory, "workflow-private-framework-name.dll"),
            [dependencyPath],
            ["System.Private.Uri"]);
        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        WorkflowResult result = FileBasedWorkflowExecutor.Execute(
            assemblyPath, providerTypeName!, qualifiedEntryName!, options: null);

        Assert.True(result.Succeeded, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.Equal("private", File.ReadAllText(marker));
    }

    [Fact(DisplayName = "file-based execution calls Create and Step through the host contract")]
    /// <summary>
    /// Provider.Create と Step が host 型を使って一度実行されることを確認します。
    /// </summary>
    public void ExecutesProviderAndStepUsingHostTypes()
    {
        string directory = CreateDirectory();
        string markers = Path.Combine(directory, "markers");
        Directory.CreateDirectory(markers);
        string assemblyPath = CompileFixture(directory, CreateFixture(markers, "Main"));

        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        WorkflowResult result = FileBasedWorkflowExecutor.Execute(
            assemblyPath, providerTypeName!, qualifiedEntryName!, options: null);

        Assert.True(result.Succeeded, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.Equal("Main", result.EntryName);
        Assert.Equal(new[] { "create", "step" }, Directory.GetFiles(markers)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Run に事前取消を渡した場合は Provider と Step の利用者コードを実行しないことを確認します。
    /// </summary>
    [Fact(DisplayName = "pre-cancelled file-based run does not execute Provider or Step code")]
    public void PreCancelledRunDoesNotExecuteProviderOrStep()
    {
        string directory = CreateDirectory();
        string markers = Path.Combine(directory, "markers");
        Directory.CreateDirectory(markers);
        string assemblyPath = CompileFixture(directory, CreateFixture(markers, "Main"));

        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        WorkflowResult result = FileBasedWorkflowExecutor.Execute(
            assemblyPath,
            providerTypeName!,
            qualifiedEntryName!,
            options: null,
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.Empty(Directory.GetFiles(markers));
    }

    /// <summary>
    /// file-based executor が開始前取消の同期 Step 互換特例を無効にすることを確認します。
    /// </summary>
    [Fact(DisplayName = "file-based execution selects strict Entry cancellation policy")]
    public void FileBasedExecutionSelectsStrictEntryCancellationPolicy()
    {
        var retry = new RetryOptions { MaxAttempts = 2 };
        WorkflowExecutionOptions options = FileBasedWorkflowExecutor.WithStrictCancellationPolicy(
            new WorkflowExecutionOptions { Retry = retry });

        Assert.Equal(WorkflowEntryCancellationPolicy.StopBeforeSynchronousStep, options.CancellationPolicy);
        Assert.Same(retry, options.Retry);
        Assert.Equal(
            WorkflowEntryCancellationPolicy.PreservePreCancelledSynchronousStep,
            new WorkflowExecutionOptions().CancellationPolicy);
    }

    /// <summary>
    /// PE metadata が選んだ非ジェネリック Create だけを実行します。
    /// </summary>
    [Fact(DisplayName = "file-based execution invokes the exact public Create validated by metadata")]
    public void ExecutorUsesMetadataValidatedCreateWhenGenericOverloadExists()
    {
        string directory = CreateDirectory();
        string marker = Path.Combine(directory, "create-called");
        string escapedMarker = marker.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        string source = $$"""
            using System.IO;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public MainProvider() { }
                public IWorkflowEntry Create()
                {
                    File.WriteAllText("{{escapedMarker}}", "created");
                    return CompositeStep.Define("Main").Run<int>("step", _ => 1);
                }
                public IWorkflowEntry Create<T>() => throw new System.InvalidOperationException("wrong Create overload");
            }
            """;
        string assemblyPath = CompileFixture(directory, source);
        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        WorkflowResult result = FileBasedWorkflowExecutor.Execute(
            assemblyPath, providerTypeName!, qualifiedEntryName!, options: null);

        Assert.True(result.Succeeded, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.Equal("created", File.ReadAllText(marker));
    }

    [Fact(DisplayName = "file-based execution rejects a mismatched Create result before the Step")]
    /// <summary>
    /// 返却 Entry 名の不一致で Step が実行されないことを確認します。
    /// </summary>
    public void MismatchedCreatedEntryDoesNotExecuteStep()
    {
        string directory = CreateDirectory();
        string markers = Path.Combine(directory, "markers");
        Directory.CreateDirectory(markers);
        string assemblyPath = CompileFixture(directory, CreateFixture(markers, "Other"));

        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        WorkflowResult result = FileBasedWorkflowExecutor.Execute(
            assemblyPath, providerTypeName!, qualifiedEntryName!, options: null);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.EntryStepNotFound, result.ErrorCode);
        Assert.Contains("create", Directory.GetFiles(markers).Select(Path.GetFileName));
        Assert.DoesNotContain("step", Directory.GetFiles(markers).Select(Path.GetFileName));
    }

    [Fact(DisplayName = "file-based execution reports a Provider.Create failure without running a Step")]
    /// <summary>
    /// Provider.Create の例外後に Step が実行されないことを確認します。
    /// </summary>
    public void ProviderFailureDoesNotExecuteStep()
    {
        string directory = CreateDirectory();
        string markers = Path.Combine(directory, "markers");
        Directory.CreateDirectory(markers);
        string assemblyPath = CompileFixture(directory, CreateFixture(markers, "Main", throwsOnCreate: true));

        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        WorkflowResult result = FileBasedWorkflowExecutor.Execute(
            assemblyPath, providerTypeName!, qualifiedEntryName!, options: null);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptEntryProviderFailed, result.ErrorCode);
        Assert.DoesNotContain("step", Directory.GetFiles(markers).Select(Path.GetFileName));
    }

    /// <summary>
    /// Run に渡した外部 cancellation が Step へ届き、後続 Step が実行されないことを確認します。
    /// </summary>
    [Fact(DisplayName = "file-based run propagates cancellation into the current Step")]
    public async Task RunCancellationReachesCurrentStepAndStopsFollowingSteps()
    {
        string directory = CreateDirectory();
        string startedPath = Path.Combine(directory, "step-started");
        string followingPath = Path.Combine(directory, "following-step-ran");
        string source = $$"""
            using System.IO;
            using System.Threading.Tasks;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public MainProvider() { }
                public IWorkflowEntry Create() => CompositeStep.Define("Main")
                    .RunAsync<int>("wait", async (_, token) =>
                    {
                        File.WriteAllText("{{startedPath}}", "started");
                        await Task.Delay(System.Threading.Timeout.Infinite, token);
                        return 1;
                    })
                    .Run<string>("after", (_, _) =>
                    {
                        File.WriteAllText("{{followingPath}}", "ran");
                        return "done";
                    });
            }
            """;
        string assemblyPath = CompileFixture(directory, source);
        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var watcher = new FileSystemWatcher(directory, Path.GetFileName(startedPath));
        watcher.Created += (_, _) => started.Set();
        watcher.EnableRaisingEvents = true;
        Task<WorkflowResult> execution = Task.Run(() => FileBasedWorkflowExecutor.Execute(
            assemblyPath,
            providerTypeName!,
            qualifiedEntryName!,
            options: null,
            cancellation.Token));

        Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "The asynchronous Step did not report entry.");
        cancellation.Cancel();
        WorkflowResult result = await execution.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.False(File.Exists(followingPath));
    }

    [Fact(DisplayName = "file-based steps can use the host ILogger contract")]
    /// <summary>
    /// file-based Step が host 共有 ILogger を使えることを確認します。
    /// </summary>
    public void StepCanUseSharedHostLoggerType()
    {
        string directory = CreateDirectory();
        string markers = Path.Combine(directory, "markers");
        Directory.CreateDirectory(markers);
        string assemblyPath = CompileFixture(directory, CreateFixture(markers, "Main", usesLogger: true));

        var loadContext = new FileBasedWorkflowLoadContext(assemblyPath);
        try
        {
            Assembly loggerAssembly = loadContext.LoadFromAssemblyName(typeof(ILogger).Assembly.GetName());
            Assert.Same(typeof(ILogger).Assembly, loggerAssembly);
        }
        finally
        {
            loadContext.Unload();
        }

        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        WorkflowResult result = FileBasedWorkflowExecutor.Execute(
            assemblyPath, providerTypeName!, qualifiedEntryName!, options: null);

        Assert.True(result.Succeeded, $"{result.ErrorCode}: {result.ErrorMessage}");
    }

    [Fact(DisplayName = "missing declared dependencies fail before Provider construction")]
    /// <summary>
    /// 宣言依存が欠落した場合に Provider ctor より先に失敗することを確認します。
    /// </summary>
    public void MissingDependencyIsReportedBeforeProviderConstructor()
    {
        string directory = CreateDirectory();
        string marker = Path.Combine(directory, "provider-created");
        string dependencyPath = Path.Combine(directory, "System.FakeFramework.dll");
        CompileFixture(directory, "namespace System.FakeFramework; public static class Hook { public static void Touch() { } }", dependencyPath);
        string escapedMarker = marker.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        string source = $$"""
            using System.IO;
            using Devo6.WorkFlow.Engine;
            using System.FakeFramework;
            [WorkflowEntry("Main")] public sealed class MissingDependencyProvider : IWorkflowEntryProvider
            {
                public MissingDependencyProvider() => File.WriteAllText("{{escapedMarker}}", "constructed");
                public IWorkflowEntry Create() { Hook.Touch(); return null!; }
            }
            """;
        string assemblyPath = CompileFixture(directory, source, Path.Combine(directory, "workflow-missing-dependency.dll"), [dependencyPath]);
        File.Delete(dependencyPath);
        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        WorkflowResult result = FileBasedWorkflowExecutor.Execute(
            assemblyPath, providerTypeName!, qualifiedEntryName!, options: null);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptLoadFailed, result.ErrorCode);
        Assert.False(File.Exists(marker));
    }

    /// <summary>
    /// Provider 実行テスト向け source fixture を作成します。
    /// </summary>
    private static string CreateFixture(string markerDirectory, string createdEntryName, bool throwsOnCreate = false, bool usesLogger = false)
    {
        string escaped = markerDirectory.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        string createBody = throwsOnCreate
            ? "Markers.Touch(\"create\"); throw new System.InvalidOperationException(\"provider failed\");"
            : $"Markers.Touch(\"create\"); return CompositeStep.Define(\"{createdEntryName}\").Run<int>(\"step\", input => {{ {(usesLogger ? "input.Context.Logger.LogInformation(\"host logger shared\");" : "")} Markers.Touch(\"step\"); return 1; }});";
        return $$"""
            using System.IO;
            using Devo6.WorkFlow.Engine;
            using Microsoft.Extensions.Logging;

            internal static class Markers
            {
                internal static void Touch(string name) => File.WriteAllText(Path.Combine("{{escaped}}", name), name);
            }

            [WorkflowEntry("Main")]
            public sealed class TestProvider : IWorkflowEntryProvider
            {
                public TestProvider() { }
                public IWorkflowEntry Create()
                {
                    {{createBody}}
                }
            }
            """;
    }

    /// <summary>
    /// C# fixture を一時 managed assembly に compile します。
    /// </summary>
    private static string CompileFixture(
        string directory,
        string source,
        string? outputPath = null,
        IReadOnlyList<string>? additionalReferences = null,
        IReadOnlyList<string>? excludedPlatformAssemblyNames = null)
    {
        string assemblyPath = outputPath ?? Path.Combine(directory, "workflow-fixture.dll");
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        IEnumerable<MetadataReference> references = trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !(excludedPlatformAssemblyNames ?? []).Contains(
                Path.GetFileNameWithoutExtension(path),
                StringComparer.OrdinalIgnoreCase))
            .Append(typeof(WorkflowEntryAttribute).Assembly.Location)
            .Append(typeof(WorkflowErrorCodes).Assembly.Location)
            .Concat(additionalReferences ?? [])
            .Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(assemblyPath),
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using FileStream output = File.Create(assemblyPath);
        EmitResult emitResult = compilation.Emit(output);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return assemblyPath;
    }

    /// <summary>
    /// 一意なテスト用一時ディレクトリを作成します。
    /// </summary>
    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "devo6-workflow-provider-execution-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// metadata validation errors を診断文字列へ整形します。
    /// </summary>
    private static string FormatErrors(WorkflowValidationResult result) =>
        string.Join(Environment.NewLine, result.Errors.Select(error => $"{error.Code}: {error.Path}: {error.Message}"));
}
#endif
