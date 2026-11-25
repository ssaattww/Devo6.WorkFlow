#if NET10_0
using System.Security;
using System.Text.Json;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// SDK build 後の診断保持と正常終了時の session 回収を確認します。
/// </summary>
public sealed class FileBasedCsWorkflowBuilderLifecycleTests
{
    /// <summary>
    /// metadata 検証が失敗した場合に生成物と session を保持し、診断に保存先を含めます。
    /// </summary>
    [Fact(DisplayName = "failed metadata validation retains its SDK session and diagnostic path")]
    public void ValidateFailureRetainsSdkSession()
    {
        using var fixture = new BuilderFixture("public sealed class NoEntry { }");

        WorkflowValidationResult result = fixture.Builder.Validate(fixture.EntryPath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory,
            string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)),
            StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
        string assemblyPath = FileBasedCsWorkflowBuilder.GetSdkOutputAssemblyPath(
            fixture.EntryPath, fixture.Runner.RunContext.RunFileRoot);
        Assert.True(File.Exists(assemblyPath));
    }

    /// <summary>
    /// Step の失敗後は session を保持し、診断に保存先を含めます。
    /// </summary>
    [Fact(DisplayName = "failed Step execution retains its SDK session and diagnostic path")]
    public void RunStepFailureRetainsSdkSession()
    {
        using var fixture = new BuilderFixture("""
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public IWorkflowEntry Create() => CompositeStep.Define("Main")
                    .Run<int>("fail", _ => throw new System.InvalidOperationException("fixture failure"));
            }
            """);

        WorkflowResult result = fixture.Builder.Run(fixture.EntryPath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
    }

    /// <summary>
    /// 実行中 Step に取消を伝え、session と診断場所を保持します。
    /// </summary>
    [Fact(DisplayName = "cancelled Step execution retains its SDK session and diagnostic path")]
    public async Task RunCancellationRetainsSdkSession()
    {
        string root = Path.Combine(Path.GetTempPath(), "devo6-file-based-cancel-marker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var markerDirectory = new TemporaryDirectory(root);
        string startedPath = Path.Combine(root, "step-started");
        using var fixture = new BuilderFixture($$"""
            using System.IO;
            using System.Threading.Tasks;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public IWorkflowEntry Create() => CompositeStep.Define("Main")
                    .RunAsync<int>("wait", async (_, token) =>
                    {
                        File.WriteAllText({{System.Text.Json.JsonSerializer.Serialize(startedPath)}}, "started");
                        await Task.Delay(System.Threading.Timeout.Infinite, token);
                        return 1;
                    });
            }
            """);
        using var cancellation = new CancellationTokenSource();
        WorkflowResult? result = null;
        using var started = new ManualResetEventSlim();
        using var watcher = new FileSystemWatcher(root, Path.GetFileName(startedPath));
        watcher.Created += (_, _) => started.Set();
        watcher.EnableRaisingEvents = true;

        Task execution = Task.Run(() => result = fixture.Builder.Run(
            fixture.EntryPath,
            "0.1.0",
            cancellationToken: cancellation.Token));
        bool stepStarted = started.Wait(TimeSpan.FromSeconds(10));
        Assert.True(stepStarted, execution.IsCompleted
            ? $"The workflow returned before its Step began: {result?.ErrorCode}: {result?.ErrorMessage}"
            : "The workflow Step did not begin.");
        cancellation.Cancel();
        await execution.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
    }

    /// <summary>
    /// 正常な Run の後にだけ SDK session を削除します。
    /// </summary>
    [Fact(DisplayName = "successful workflow execution removes its SDK session")]
    public void SuccessfulRunRemovesSdkSession()
    {
        using var fixture = new BuilderFixture("""
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public IWorkflowEntry Create() => CompositeStep.Define("Main")
                    .Run<int>("ok", _ => 1);
            }
            """);

        WorkflowResult result = fixture.Builder.Run(fixture.EntryPath, "0.1.0");

        Assert.True(result.Succeeded,
            $"{result.ErrorCode}: {result.ErrorMessage}; generated={fixture.Runner.GeneratedAssemblyPath}; exists={File.Exists(fixture.Runner.GeneratedAssemblyPath)}");
        Assert.NotNull(fixture.Runner.RunContext);
        Assert.False(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
    }

    private sealed class BuilderFixture : IDisposable
    {
        private const string SdkInfo = ".NET SDK:\n Version: 10.0.404\n Base Path: /usr/share/dotnet/sdk/10.0.404/";

        public BuilderFixture(string providerSource)
        {
            Root = Path.Combine(Path.GetTempPath(), "devo6-file-based-lifecycle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SessionRoot = Root + "-sessions";
            string feed = Path.Combine(Root, "feed");
            Directory.CreateDirectory(feed);
            EntryPath = Path.Combine(Root, "workflow.cs");
            File.WriteAllText(EntryPath, string.Join(Environment.NewLine,
            [
                "#:property OutputType=Library",
                "#:property TargetFramework=net10.0",
                "#:property PublishAot=false",
                "#:package Devo6.WorkFlow.Engine@[0.1.0]",
                "using Devo6.WorkFlow.Engine;",
                providerSource,
            ]));
            File.WriteAllText(Path.Combine(Root, "NuGet.Config"),
                $"<configuration><packageSources><clear/><add key=\"fixture\" value=\"{SecurityElement.Escape(feed)}\" /></packageSources></configuration>");
            File.WriteAllText(Path.Combine(Root, "devo6.nuget.lock.yaml"), $$"""
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

            string fixtureAssembly = Path.Combine(Root, "sdk-fixture.dll");
            CompileFixture(providerSource, fixtureAssembly);
            Runner = new LifecycleSdkRunner(EntryPath, feed, fixtureAssembly);
            Builder = new FileBasedCsWorkflowBuilder(
                Runner,
                () => Path.Combine(Root, "runfile"),
                sdkSessionRootProvider: () => SessionRoot);
        }

        public string Root { get; }
        public string SessionRoot { get; }
        public string EntryPath { get; }
        public LifecycleSdkRunner Runner { get; }
        public FileBasedCsWorkflowBuilder Builder { get; }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
            if (Directory.Exists(SessionRoot))
            {
                Directory.Delete(SessionRoot, recursive: true);
            }
        }

        private static void CompileFixture(string providerSource, string outputPath)
        {
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(providerSource,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
            string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
            IEnumerable<MetadataReference> references = trustedAssemblies
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Append(typeof(WorkflowEntryAttribute).Assembly.Location)
                .Append(typeof(WorkflowErrorCodes).Assembly.Location)
                .Distinct(StringComparer.Ordinal)
                .Select(path => MetadataReference.CreateFromFile(path));
            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(outputPath),
                [syntaxTree],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using FileStream output = File.Create(outputPath);
            EmitResult emit = compilation.Emit(output);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        }
    }

    private sealed class TemporaryDirectory(string path) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private sealed class LifecycleSdkRunner(string entryPath, string feedPath, string fixtureAssemblyPath) : IFileBasedSdkRunner
    {
        public FileBasedSdkRunContext? RunContext { get; private set; }
        public string? GeneratedAssemblyPath { get; private set; }

        public FileBasedSdkProcessResult Run(
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            FileBasedSdkRunContext? runContext = null)
        {
            RunContext = runContext;
            if (arguments.SequenceEqual(["--version"], StringComparer.Ordinal))
            {
                return new FileBasedSdkProcessResult(0, "10.0.404", "");
            }

            if (arguments.SequenceEqual(["--info"], StringComparer.Ordinal))
            {
                return new FileBasedSdkProcessResult(0, SdkInfo, "");
            }

            string artifacts = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(entryPath, runContext!.RunFileRoot);
            if (arguments.Any(argument => argument.StartsWith("-getProperty:", StringComparison.Ordinal)))
            {
                return new FileBasedSdkProcessResult(0, EvaluationJson(artifacts, runContext.NuGetPackagesDirectory), "");
            }

            if (arguments.FirstOrDefault() == "restore")
            {
                string assetsPath = Path.Combine(artifacts, "obj", "project.assets.json");
                Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
                File.WriteAllText(assetsPath, JsonSerializer.Serialize(new
                {
                    targets = new Dictionary<string, object> { ["net10.0"] = new Dictionary<string, object>() },
                    packageFolders = new Dictionary<string, object> { [runContext.NuGetPackagesDirectory] = new { } },
                    libraries = new Dictionary<string, object> { ["Devo6.WorkFlow.Engine/0.1.0"] = new { type = "package" } },
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
                            packagesPath = runContext.NuGetPackagesDirectory,
                            sources = new Dictionary<string, object> { [feedPath] = new { } },
                        },
                    },
                }));
                return new FileBasedSdkProcessResult(0, "", "");
            }

            if (arguments.FirstOrDefault() == "build")
            {
                string assemblyPath = FileBasedCsWorkflowBuilder.GetSdkOutputAssemblyPath(entryPath, runContext.RunFileRoot);
                GeneratedAssemblyPath = assemblyPath;
                Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
                File.Copy(fixtureAssemblyPath, assemblyPath, overwrite: true);
                return new FileBasedSdkProcessResult(0, "", "");
            }

            throw new InvalidOperationException($"Unexpected SDK fixture invocation: {string.Join(' ', arguments)}");
        }

        private static string EvaluationJson(string artifacts, string packagesDirectory)
        {
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
                    ["RestorePackagesWithLockFile"] = "false",
                    ["RestoreLockedMode"] = "false",
                    ["ManagePackageVersionsCentrally"] = "false",
                    ["RestoreAdditionalProjectSources"] = "",
                    ["RestoreConfigFile"] = "",
                    ["RestoreSources"] = "",
                    ["RestorePackagesPath"] = packagesDirectory,
                    ["MSBuildSDKsPath"] = "/usr/share/dotnet/sdk/10.0.404/Sdks",
                    ["MSBuildExtensionsPath"] = "/usr/share/dotnet/sdk/10.0.404/",
                    ["BaseOutputPath"] = bin + Path.DirectorySeparatorChar,
                    ["OutputPath"] = output + Path.DirectorySeparatorChar,
                    ["OutDir"] = output + Path.DirectorySeparatorChar,
                    ["BaseIntermediateOutputPath"] = obj + Path.DirectorySeparatorChar,
                    ["IntermediateOutputPath"] = Path.Combine(obj, "debug") + Path.DirectorySeparatorChar,
                    ["MSBuildProjectExtensionsPath"] = obj + Path.DirectorySeparatorChar,
                    ["TargetPath"] = Path.Combine(output, Path.GetFileNameWithoutExtension("workflow.cs") + ".dll"),
                    ["ArtifactsPath"] = artifacts,
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

        private const string SdkInfo = ".NET SDK:\n Version: 10.0.404\n Base Path: /usr/share/dotnet/sdk/10.0.404/";
    }
}
#endif
