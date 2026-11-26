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

    /// <summary>
    /// build 完了時に取消された場合、Provider を生成せず診断 session を返します。
    /// </summary>
    [Fact(DisplayName = "run cancellation after SDK build retains diagnostics before Provider creation")]
    public void RunCancellationAfterBuildDoesNotCreateProvider()
    {
        string markerRoot = Path.Combine(Path.GetTempPath(), "devo6-provider-cancel-marker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markerRoot);
        using var markerCleanup = new TemporaryDirectory(markerRoot);
        string providerMarker = Path.Combine(markerRoot, "provider-created");
        using var cancellation = new CancellationTokenSource();
        using var fixture = new BuilderFixture($$"""
            using System.IO;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public MainProvider() => File.WriteAllText({{JsonSerializer.Serialize(providerMarker)}}, "created");
                public IWorkflowEntry Create() => CompositeStep.Define("Main").Run<int>("ok", _ => 1);
            }
            """, afterBuild: _ => cancellation.Cancel());

        WorkflowResult result = fixture.Builder.Run(fixture.EntryPath, "0.1.0", cancellationToken: cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.False(File.Exists(providerMarker));
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
    }

    /// <summary>
    /// SDK process の開始前に取り消された公開 Build が失敗結果を返します。
    /// </summary>
    [Fact(DisplayName = "Build cancellation before SDK execution returns the lock failure result")]
    public void BuildCancellationBeforeSdkExecutionReturnsFailure()
    {
        using var fixture = new BuilderFixture("public sealed class NoEntry { }");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        FileBasedCsBuildResult result = fixture.Builder.Build(
            fixture.EntryPath, "0.1.0", cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptBuildFailed, result.ErrorCode);
        Assert.Empty(fixture.Runner.Calls);
    }

    /// <summary>
    /// 出力排他の待機中に取り消された公開 Build が失敗結果を返します。
    /// </summary>
    [Fact(DisplayName = "Build cancellation while waiting for the output lock returns a failure result")]
    public async Task BuildCancellationWhileWaitingForOutputLockReturnsFailure()
    {
        using var fixture = new BuilderFixture("public sealed class NoEntry { }");
        string runFileRoot = Path.Combine(fixture.Root, "runfile");
        string artifactsDirectory = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(fixture.EntryPath, runFileRoot);
        string outputLockPath = FileBasedCsWorkflowBuilder.GetFileBasedOutputLockPath(
            runFileRoot, artifactsDirectory, OperatingSystem.IsWindows());
        Directory.CreateDirectory(Path.GetDirectoryName(outputLockPath)!);
        using var heldLock = new FileStream(outputLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var contention = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = new FileBasedCsWorkflowBuilder(
            fixture.Runner,
            () => runFileRoot,
            path => contention.TrySetResult(path),
            () => fixture.SessionRoot);
        using var cancellation = new CancellationTokenSource();
        Task<FileBasedCsBuildResult> build = Task.Run(() => builder.Build(
            fixture.EntryPath, "0.1.0", cancellation.Token));

        try
        {
            Assert.Equal(outputLockPath, await contention.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            cancellation.Cancel();
            FileBasedCsBuildResult result = await build.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(result.Succeeded);
            Assert.Equal(WorkflowErrorCodes.ScriptBuildFailed, result.ErrorCode);
            Assert.Empty(fixture.Runner.Calls);
        }
        finally
        {
            cancellation.Cancel();
            await build.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// SDK が取消を返した場合も専用 session の保存先を Run 診断へ含めます。
    /// </summary>
    [Fact(DisplayName = "SDK cancellation returns a retained diagnostic session through Run")]
    public void RunSdkCancellationRetainsSessionPath()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new BuilderFixture("public sealed class NoEntry { }",
            afterRestore: _ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });

        WorkflowResult result = fixture.Builder.Run(
            fixture.EntryPath, "0.1.0", cancellationToken: cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
    }

    /// <summary>
    /// Provider.Create 中の取消を観測し、Step 開始前に実行を止めます。
    /// </summary>
    [Fact(DisplayName = "cancellation during Provider.Create stops before the first Step")]
    public async Task RunCancellationDuringProviderCreateStopsBeforeStep()
    {
        string gateRoot = Path.Combine(Path.GetTempPath(), "devo6-provider-create-gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gateRoot);
        using var gateCleanup = new TemporaryDirectory(gateRoot);
        string createStartedPath = Path.Combine(gateRoot, "create-started");
        string releasePath = Path.Combine(gateRoot, "release-create");
        string stepStartedPath = Path.Combine(gateRoot, "step-started");
        using var fixture = new BuilderFixture($$"""
            using System.IO;
            using System.Threading;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public IWorkflowEntry Create()
                {
                    File.WriteAllText({{JsonSerializer.Serialize(createStartedPath)}}, "started");
                    long releaseDeadline = System.Environment.TickCount64 + 10000;
                    while (!File.Exists({{JsonSerializer.Serialize(releasePath)}})
                        && System.Environment.TickCount64 < releaseDeadline) Thread.Yield();
                    if (!File.Exists({{JsonSerializer.Serialize(releasePath)}})) throw new System.TimeoutException("Create gate was not released.");
                    return CompositeStep.Define("Main").Run<int>("marker", _ =>
                    {
                        File.WriteAllText({{JsonSerializer.Serialize(stepStartedPath)}}, "started");
                        return 1;
                    });
                }
            }
            """);
        using var cancellation = new CancellationTokenSource();
        WorkflowResult? result = null;
        Task run = Task.Run(() => result = fixture.Builder.Run(
            fixture.EntryPath, "0.1.0", cancellationToken: cancellation.Token));
        gateCleanup.DeferCleanupUntil(run);
        fixture.DeferCleanupUntil(run);
        try
        {
            await WaitForFileAsync(createStartedPath, run);
            cancellation.Cancel();
            File.WriteAllText(releasePath, "release");
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            File.WriteAllText(releasePath, "release");
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.False(File.Exists(stepStartedPath));
    }

    /// <summary>
    /// 公開 Run で Create 後、同期 Step の呼び出し直前に取消された場合は Step 本体を開始しません。
    /// </summary>
    [Fact(DisplayName = "run cancellation during step initialization stops before synchronous step body")]
    public void RunCancellationDuringStepInitializationStopsBeforeSynchronousStepBody()
    {
        string markerRoot = Path.Combine(Path.GetTempPath(), "devo6-step-init-cancel-marker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markerRoot);
        using var markerCleanup = new TemporaryDirectory(markerRoot);
        string createMarkerPath = Path.Combine(markerRoot, "provider-created-entry");
        string stepMarkerPath = Path.Combine(markerRoot, "step-body-started");
        using var cancellation = new CancellationTokenSource();
        using var fixture = new BuilderFixture($$"""
            using System.IO;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public IWorkflowEntry Create()
                {
                    File.WriteAllText({{JsonSerializer.Serialize(createMarkerPath)}}, "created");
                    return CompositeStep.Define("Main").Run<int>("sync", _ =>
                    {
                        File.WriteAllText({{JsonSerializer.Serialize(stepMarkerPath)}}, "started");
                        return 1;
                    });
                }
            }
            """);
        var options = new WorkflowExecutionOptions
        {
            StepTimeout = TimeSpan.FromMinutes(1),
            StepTimeoutTokenObserver = _ => cancellation.Cancel(),
        };

        WorkflowResult result = fixture.Builder.Run(
            fixture.EntryPath,
            "0.1.0",
            options: options,
            cancellationToken: cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.True(File.Exists(createMarkerPath));
        Assert.False(File.Exists(stepMarkerPath));
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
    }

    /// <summary>
    /// 公開 Run の If と Switch で、選択前取消と評価中の取消例外を区別し、通常例外の診断を維持します。
    /// </summary>
    /// <param name="registration">条件または selector を選択します。</param>
    /// <param name="behavior">分岐前取消、評価中取消例外、通常評価例外のいずれかです。</param>
    [Theory]
    [InlineData("If", "before", WorkflowErrorCodes.StepCanceled, false)]
    [InlineData("Switch", "before", WorkflowErrorCodes.StepCanceled, false)]
    [InlineData("If", "cancel-oce", WorkflowErrorCodes.StepCanceled, true)]
    [InlineData("Switch", "cancel-oce", WorkflowErrorCodes.StepCanceled, true)]
    [InlineData("If", "normal-error", WorkflowErrorCodes.ConditionEvaluationFailed, true)]
    [InlineData("Switch", "normal-error", WorkflowErrorCodes.SwitchSelectorFailed, true)]
    public void RunBranchSelectionCancellationAndFailuresKeepTheirDiagnostics(
        string registration,
        string behavior,
        string expectedErrorCode,
        bool callbackExpected)
    {
        string markerRoot = Path.Combine(Path.GetTempPath(), "devo6-branch-selection-cancel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markerRoot);
        using var markerCleanup = new TemporaryDirectory(markerRoot);
        string callbackPath = Path.Combine(markerRoot, "callback-started");
        string isIf = registration == "If" ? "true" : "false";
        using var cancellation = new CancellationTokenSource();
        var loggerFactory = new CancellationTriggerLoggerFactory(message =>
        {
            if ((behavior == "before" && message == "If condition started")
                || (behavior == "cancel-oce" && message == "user callback cancellation"))
            {
                cancellation.Cancel();
            }
        });
        string source = $$"""
            using System.IO;
            using Microsoft.Extensions.Logging;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public IWorkflowEntry Create()
                {
                    var workflow = CompositeStep.Define("Main").Run<int>("seed", _ => 0);
                    if ({{isIf}})
                    {
                        return workflow.If("branch-selection", (current, input) =>
                        {
                            File.WriteAllText({{JsonSerializer.Serialize(callbackPath)}}, "called");
                            if ({{JsonSerializer.Serialize(behavior)}} == "cancel-oce")
                            {
                                input.Context.Logger.LogInformation("user callback cancellation");
                                throw new System.OperationCanceledException("If evaluation canceled.");
                            }
                            if ({{JsonSerializer.Serialize(behavior)}} == "normal-error")
                            {
                                throw new System.InvalidOperationException("If evaluation failed.");
                            }
                            return true;
                        }, thenBranch => thenBranch.Run<int>("then", _ => 1), elseBranch => elseBranch.Run<int>("else", _ => 0));
                    }
                    return workflow.Switch<string, int>("branch-selection", (current, input) =>
                    {
                        File.WriteAllText({{JsonSerializer.Serialize(callbackPath)}}, "called");
                        if ({{JsonSerializer.Serialize(behavior)}} == "cancel-oce")
                        {
                            input.Context.Logger.LogInformation("user callback cancellation");
                            throw new System.OperationCanceledException("Switch evaluation canceled.");
                        }
                        if ({{JsonSerializer.Serialize(behavior)}} == "normal-error")
                        {
                            throw new System.InvalidOperationException("Switch evaluation failed.");
                        }
                        return "selected";
                    }, cases => cases.Case("selected", branch => branch.Run<int>("selected", _ => 1))
                        .Default(branch => branch.Run<int>("default", _ => 0)));
                }
            }
            """;
        using var fixture = new BuilderFixture(source);

        WorkflowResult result = fixture.Builder.Run(
            fixture.EntryPath,
            "0.1.0",
            options: new WorkflowExecutionOptions(loggerFactory),
            cancellationToken: cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Equal(callbackExpected, File.Exists(callbackPath));
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
        ExecutionTraceStep traceStep = Assert.Single(result.Trace!.Steps.Where(step => step.StepName == "branch-selection"));
        Assert.Equal(expectedErrorCode, traceStep.ErrorCode);
    }

    /// <summary>
    /// 公開 Run で条件評価または Step 生成中に外部取消された場合は同期本体を開始しません。
    /// </summary>
    /// <param name="registration">取消を発生させる RunIf、TapIf、または型付き Step の登録形態。</param>
    [Theory]
    [InlineData("run-if")]
    [InlineData("tap-if")]
    [InlineData("constructor")]
    public async Task RunCancellationAfterRegistrationUserCodeStopsStepBody(string registration)
    {
        string markerRoot = Path.Combine(Path.GetTempPath(), "devo6-registration-cancel-marker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markerRoot);
        using var markerCleanup = new TemporaryDirectory(markerRoot);
        string providerCreatedPath = Path.Combine(markerRoot, "provider-created-entry");
        string conditionStartedPath = Path.Combine(markerRoot, "condition-started");
        string constructorStartedPath = Path.Combine(markerRoot, "constructor-started");
        string stepStartedPath = Path.Combine(markerRoot, "step-started");
        string releasePath = Path.Combine(markerRoot, "release-registration");
        string registrationSource = registration switch
        {
            "run-if" => $$"""
                return CompositeStep.Define("Main")
                    .Run<int>("seed", _ => 0)
                    .RunIf<GateStep, int>((_, _) =>
                    {
                        File.WriteAllText({{JsonSerializer.Serialize(conditionStartedPath)}}, "started");
                        FixtureWait.WaitForRelease({{JsonSerializer.Serialize(releasePath)}});
                        return true;
                    }, (current, _) => current);
                """,
            "tap-if" => $$"""
                return CompositeStep.Define("Main")
                    .Run<int>("seed", _ => 0)
                    .TapIf<GateStep>((_, _) =>
                    {
                        File.WriteAllText({{JsonSerializer.Serialize(conditionStartedPath)}}, "started");
                        FixtureWait.WaitForRelease({{JsonSerializer.Serialize(releasePath)}});
                        return true;
                    });
                """,
            "constructor" => "return CompositeStep.Define(\"Main\").Run<GateStep, int>();",
            _ => throw new ArgumentOutOfRangeException(nameof(registration), registration, "Unknown registration fixture."),
        };
        string stepInterface = registration == "tap-if" ? "IStep<Unit>" : "IStep<int>";
        string stepReturn = registration == "tap-if" ? "Unit.Value" : "1";
        string constructorGate = registration == "constructor"
            ? $"FixtureWait.WaitForRelease({JsonSerializer.Serialize(releasePath)});"
            : string.Empty;
        string source = $$"""
            using System.IO;
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public IWorkflowEntry Create()
                {
                    File.WriteAllText({{JsonSerializer.Serialize(providerCreatedPath)}}, "created");
                    {{registrationSource}}
                }
            }
            public sealed class GateStep : {{stepInterface}}
            {
                public GateStep()
                {
                    File.WriteAllText({{JsonSerializer.Serialize(constructorStartedPath)}}, "started");
                    {{constructorGate}}
                }
                public {{(registration == "tap-if" ? "Unit" : "int")}} Execute(StepInput input)
                {
                    File.WriteAllText({{JsonSerializer.Serialize(stepStartedPath)}}, "started");
                    return {{stepReturn}};
                }
            }
            internal static class FixtureWait
            {
                public static void WaitForRelease(string path)
                {
                    long deadline = System.Environment.TickCount64 + 10000;
                    while (!File.Exists(path) && System.Environment.TickCount64 < deadline)
                    {
                        System.Threading.Thread.Yield();
                    }
                    if (!File.Exists(path)) throw new System.TimeoutException("Registration gate was not released.");
                }
            }
            """;
        using var fixture = new BuilderFixture(source);
        using var cancellation = new CancellationTokenSource();
        string enteredPath = registration == "constructor" ? constructorStartedPath : conditionStartedPath;
        WorkflowResult? result = null;
        Task run = Task.Run(() => result = fixture.Builder.Run(
            fixture.EntryPath,
            "0.1.0",
            cancellationToken: cancellation.Token));
        markerCleanup.DeferCleanupUntil(run);
        fixture.DeferCleanupUntil(run);

        try
        {
            await WaitForFileAsync(enteredPath, run);
            cancellation.Cancel();
            File.WriteAllText(releasePath, "release");
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            cancellation.Cancel();
            File.WriteAllText(releasePath, "release");
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.True(File.Exists(providerCreatedPath));
        Assert.Equal(registration == "constructor", File.Exists(constructorStartedPath));
        Assert.Equal(registration != "constructor", File.Exists(conditionStartedPath));
        Assert.False(File.Exists(stepStartedPath));
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
    }

    /// <summary>
    /// Provider constructor 中の取消を観測し、Create と Step を呼び出す前に停止します。
    /// </summary>
    [Fact(DisplayName = "cancellation during Provider construction stops before Create")]
    public async Task RunCancellationDuringProviderConstructionStopsBeforeCreate()
    {
        string gateRoot = Path.Combine(Path.GetTempPath(), "devo6-provider-constructor-gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gateRoot);
        using var gateCleanup = new TemporaryDirectory(gateRoot);
        string constructorStartedPath = Path.Combine(gateRoot, "constructor-started");
        string releasePath = Path.Combine(gateRoot, "release-constructor");
        string createPath = Path.Combine(gateRoot, "create-called");
        string stepStartedPath = Path.Combine(gateRoot, "step-started");
        using var fixture = new BuilderFixture($$"""
            using System.IO;
            using System.Threading;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public MainProvider()
                {
                    File.WriteAllText({{JsonSerializer.Serialize(constructorStartedPath)}}, "started");
                    long releaseDeadline = System.Environment.TickCount64 + 10000;
                    while (!File.Exists({{JsonSerializer.Serialize(releasePath)}})
                        && System.Environment.TickCount64 < releaseDeadline) Thread.Yield();
                    if (!File.Exists({{JsonSerializer.Serialize(releasePath)}})) throw new System.TimeoutException("Constructor gate was not released.");
                }
                public IWorkflowEntry Create()
                {
                    File.WriteAllText({{JsonSerializer.Serialize(createPath)}}, "called");
                    return CompositeStep.Define("Main").Run<int>("marker", _ =>
                    {
                        File.WriteAllText({{JsonSerializer.Serialize(stepStartedPath)}}, "started");
                        return 1;
                    });
                }
            }
            """);
        using var cancellation = new CancellationTokenSource();
        WorkflowResult? result = null;
        Task run = Task.Run(() => result = fixture.Builder.Run(
            fixture.EntryPath, "0.1.0", cancellationToken: cancellation.Token));
        gateCleanup.DeferCleanupUntil(run);
        fixture.DeferCleanupUntil(run);
        try
        {
            await WaitForFileAsync(constructorStartedPath, run);
            cancellation.Cancel();
            File.WriteAllText(releasePath, "release");
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            File.WriteAllText(releasePath, "release");
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.StepCanceled, result.ErrorCode);
        Assert.False(File.Exists(createPath));
        Assert.False(File.Exists(stepStartedPath));
    }

    /// <summary>
    /// restore 中に固定入力が変更された場合は build 前に拒否します。
    /// </summary>
    [Fact(DisplayName = "run rejects SDK configuration changes between restore and build")]
    public void RunRejectsConfigurationChangedDuringRestore()
    {
        string markerRoot = Path.Combine(Path.GetTempPath(), "devo6-restore-config-marker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markerRoot);
        using var markerCleanup = new TemporaryDirectory(markerRoot);
        string providerMarker = Path.Combine(markerRoot, "provider-created");
        using var fixture = new BuilderFixture($$"""
            using System.IO;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public MainProvider() => File.WriteAllText({{JsonSerializer.Serialize(providerMarker)}}, "created");
                public IWorkflowEntry Create() => CompositeStep.Define("Main").Run<int>("ok", _ => 1);
            }
            """, nestedEntryDirectory: true, afterRestore: owner =>
            {
                File.WriteAllText(Path.Combine(owner.Root, "Directory.Build.props"), "<Project />");
            });

        WorkflowResult result = fixture.Builder.Run(fixture.EntryPath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, result.ErrorCode);
        Assert.Null(fixture.Runner.GeneratedAssemblyPath);
        Assert.False(File.Exists(providerMarker));
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 固定 global.json の内容が同じでも SDK の latestPatch 選択が変われば停止します。
    /// </summary>
    [Theory]
    [InlineData("10.0.405", "/usr/share/dotnet/sdk/10.0.405/")]
    [InlineData("10.0.404", "/alternate/dotnet/sdk/10.0.404/")]
    public void RunRejectsSdkSelectionChangedWithUnchangedLatestPatchConfig(string version, string basePath)
    {
        using var fixture = new BuilderFixture(
            "public sealed class NoEntry { }",
            sdkSelection: call => call <= 1
                ? ("10.0.404", "/usr/share/dotnet/sdk/10.0.404/")
                : (version, basePath));
        string globalJson = File.ReadAllText(Path.Combine(fixture.Root, "global.json"));

        WorkflowResult result = fixture.Builder.Run(fixture.EntryPath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, result.ErrorCode);
        Assert.Equal(globalJson, File.ReadAllText(Path.Combine(fixture.Root, "global.json")));
        Assert.Equal(0, fixture.Runner.EvaluationCallCount);
        Assert.Equal(0, fixture.Runner.RestoreCallCount);
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 外側の SDK probe が一致していても restore/build process 自身の SDK 不一致を拒否します。
    /// </summary>
    [Theory]
    [InlineData("restore", "10.0.405", "/usr/share/dotnet/sdk/10.0.405/")]
    [InlineData("restore", "10.0.404", "/alternate/dotnet/sdk/10.0.404/")]
    [InlineData("build", "10.0.405", "/usr/share/dotnet/sdk/10.0.405/")]
    [InlineData("build", "10.0.404", "/alternate/dotnet/sdk/10.0.404/")]
    public void RunRejectsSdkMismatchReportedByRestoreOrBuildProcess(
        string phase,
        string reportedVersion,
        string reportedBasePath)
    {
        using var fixture = new BuilderFixture(
            "public sealed class NoEntry { }",
            sdkExecutionSelection: currentPhase => string.Equals(currentPhase, phase, StringComparison.Ordinal)
                ? (reportedVersion, reportedBasePath)
                : ("10.0.404", "/usr/share/dotnet/sdk/10.0.404/"));

        WorkflowResult result = fixture.Builder.Run(fixture.EntryPath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, result.ErrorCode);
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(phase == "restore" ? 0 : 1, fixture.Runner.BuildCallCount);
        Assert.Equal(1, fixture.Runner.RestoreCallCount);
        Assert.Equal(phase == "restore" ? 3 : 5, fixture.Runner.SdkVersionCallCount);
        Assert.DoesNotContain("provider-created", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 指定期限内に fixture がファイルを作成するまで待ちます。
    /// </summary>
    /// <param name="path">作成を待つファイルの path。</param>
    /// <param name="run">作成より先に完了していないことを確認する実行 Task。</param>
    /// <returns>ファイルが作成されたときに完了する Task。</returns>
    private static async Task WaitForFileAsync(string path, Task run)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path))
        {
            Assert.False(run.IsCompleted, "Run completed before its marker was created.");
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>
    /// SDK 処理中の固定入力変更後に Provider を実行せず、SDK session を返します。
    /// </summary>
    [Theory]
    [InlineData("global.json")]
    [InlineData("NuGet.Config")]
    [InlineData("devo6.nuget.lock.yaml")]
    public void RunRejectsFixedInputChangedDuringBuild(string fileName)
    {
        string markerRoot = Path.Combine(Path.GetTempPath(), "devo6-provider-change-marker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markerRoot);
        using var markerCleanup = new TemporaryDirectory(markerRoot);
        string providerMarker = Path.Combine(markerRoot, "provider-created");
        using var fixture = new BuilderFixture($$"""
            using System.IO;
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class MainProvider : IWorkflowEntryProvider
            {
                public MainProvider() => File.WriteAllText({{JsonSerializer.Serialize(providerMarker)}}, "created");
                public IWorkflowEntry Create() => CompositeStep.Define("Main").Run<int>("ok", _ => 1);
            }
            """, afterBuild: fixture => File.AppendAllText(Path.Combine(fixture.Root, fileName), " "));

        WorkflowResult result = fixture.Builder.Run(fixture.EntryPath, "0.1.0");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, result.ErrorCode);
        Assert.False(File.Exists(providerMarker));
        Assert.Contains(fixture.Runner.RunContext!.SessionDirectory, result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Runner.RunContext.SessionDirectory));
    }

    private sealed class BuilderFixture : IDisposable
    {
        private Task? deferredCleanup;

        /// <summary>
        /// 指定した Provider ソースを使い、session の保持と回収を検証する fixture を作成します。
        /// </summary>
        /// <param name="providerSource">fixture の生成物へコンパイルする Provider ソース。</param>
        public BuilderFixture(
            string providerSource,
            Action<BuilderFixture>? afterBuild = null,
            Action<BuilderFixture>? afterRestore = null,
            bool nestedEntryDirectory = false,
            Func<int, (string Version, string BasePath)>? sdkSelection = null,
            Func<string, (string Version, string BasePath)>? sdkExecutionSelection = null)
        {
            Root = Path.Combine(Path.GetTempPath(), "devo6-file-based-lifecycle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SessionRoot = Root + "-sessions";
            string feed = Path.Combine(Root, "feed");
            Directory.CreateDirectory(feed);
            EntryDirectory = nestedEntryDirectory ? Path.Combine(Root, "workflow") : Root;
            Directory.CreateDirectory(EntryDirectory);
            EntryPath = Path.Combine(EntryDirectory, "workflow.cs");
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
            File.WriteAllText(Path.Combine(Root, "global.json"), "{\"sdk\":{\"version\":\"10.0.404\",\"rollForward\":\"latestPatch\"}}\n");
            LockPath = Path.Combine(EntryDirectory, "devo6.nuget.lock.yaml");
            File.WriteAllText(LockPath, $$"""
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
            Runner = new LifecycleSdkRunner(
                EntryPath, feed, fixtureAssembly, () => afterBuild?.Invoke(this), () => afterRestore?.Invoke(this),
                sdkSelection, sdkExecutionSelection);
            Builder = new FileBasedCsWorkflowBuilder(
                Runner,
                () => Path.Combine(Root, "runfile"),
                sdkSessionRootProvider: () => SessionRoot);
        }

        /// <summary>入力、設定、fixture 資材を置く一時ディレクトリを取得します。</summary>
        public string Root { get; }

        /// <summary>SDK session の保持動作を検査する一時ディレクトリを取得します。</summary>
        public string SessionRoot { get; }

        /// <summary>fixture の workflow 入力ファイルの path を取得します。</summary>
        public string EntryPath { get; }

        /// <summary>fixture の workflow 入力 directory の path。</summary>
        public string EntryDirectory { get; }

        /// <summary>入力に対応する固定 NuGet lock file の path。</summary>
        public string LockPath { get; }

        /// <summary>SDK の応答と生成物を模擬する runner を取得します。</summary>
        public LifecycleSdkRunner Runner { get; }

        /// <summary>fixture の入力を検証または実行する builder を取得します。</summary>
        public FileBasedCsWorkflowBuilder Builder { get; }

        /// <summary>SDK を利用する実行 Task が完了してから所有 directory を回収します。</summary>
        public void DeferCleanupUntil(Task completion) => deferredCleanup = completion;

        /// <summary>fixture が所有する一時ディレクトリを削除します。</summary>
        public void Dispose()
        {
            if (deferredCleanup is { IsCompleted: false } pending)
            {
                _ = pending.ContinueWith(_ => Cleanup(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return;
            }

            Cleanup();
        }

        /// <summary>fixture が所有する入力と SDK session directory を回収します。</summary>
        private void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            if (Directory.Exists(SessionRoot))
            {
                Directory.Delete(SessionRoot, recursive: true);
            }
        }

        /// <summary>Provider ソースをテスト用のメタデータ fixture assembly にコンパイルします。</summary>
        /// <param name="providerSource">コンパイルする Provider の C# ソース。</param>
        /// <param name="outputPath">生成する fixture assembly の path。</param>
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

    /// <summary>指定した一時ディレクトリの削除を管理します。</summary>
    /// <param name="path">この管理対象となるディレクトリの path。</param>
    private sealed class TemporaryDirectory(string path) : IDisposable
    {
        private Task? deferredCleanup;

        /// <summary>関連 Task の完了後に管理対象 directory を削除します。</summary>
        /// <param name="completion">終了を待ってから削除する実行 Task。</param>
        public void DeferCleanupUntil(Task completion) => deferredCleanup = completion;

        /// <summary>管理対象の一時ディレクトリがあれば再帰的に削除します。</summary>
        public void Dispose()
        {
            if (deferredCleanup is { IsCompleted: false } pending)
            {
                _ = pending.ContinueWith(_ => Delete(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return;
            }

            Delete();
        }

        /// <summary>管理対象として指定された一時 directory を削除します。</summary>
        private void Delete()
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    /// <summary>入力、lock、fixture assembly を使う SDK 応答 runner を作成します。</summary>
    /// <param name="entryPath">builder が処理する workflow 入力の path。</param>
    /// <param name="feedPath">fixture の NuGet source の path。</param>
    /// <param name="fixtureAssemblyPath">build 時に配置するメタデータ fixture assembly の path。</param>
    /// <param name="afterBuild">build 完了後に呼び出す任意の fixture callback。</param>
    /// <param name="afterRestore">restore 完了後に呼び出す任意の fixture callback。</param>
    /// <param name="sdkSelection">呼出しごとの SDK 版と配置先を返す任意 fixture。</param>
    /// <param name="sdkExecutionSelection">restore/build process の SDK 記録に使う任意の SDK。</param>
    private sealed class LifecycleSdkRunner(
        string entryPath,
        string feedPath,
        string fixtureAssemblyPath,
        Action? afterBuild,
        Action? afterRestore,
        Func<int, (string Version, string BasePath)>? sdkSelection,
        Func<string, (string Version, string BasePath)>? sdkExecutionSelection) : IFileBasedSdkRunner
    {
        private int sdkVersionCallCount;
        /// <summary>現在の SDK 応答として返す版と配置先を保持します。</summary>
        private (string Version, string BasePath) currentSdk = ("10.0.404", "/usr/share/dotnet/sdk/10.0.404/");
        /// <summary>SDK runner が受け取った呼出しの引数を記録します。</summary>
        public List<IReadOnlyList<string>> Calls { get; } = [];

        /// <summary>直近の SDK 呼び出しに渡された session context を取得します。</summary>
        public FileBasedSdkRunContext? RunContext { get; private set; }

        /// <summary>build 呼び出しで配置した assembly の path を取得します。</summary>
        public string? GeneratedAssemblyPath { get; private set; }

        /// <summary>SDK evaluation 専用の呼出し回数を取得します。</summary>
        public int EvaluationCallCount { get; private set; }

        /// <summary>restore 呼出しの回数を取得します。</summary>
        public int RestoreCallCount { get; private set; }

        /// <summary>build 呼び出しの回数を取得します。</summary>
        public int BuildCallCount { get; private set; }

        /// <summary>選択 SDK 版を確認する外側 probe の回数を取得します。</summary>
        public int SdkVersionCallCount => Volatile.Read(ref sdkVersionCallCount);

        /// <summary>想定した SDK 呼び出しへ応答し、fixture 成果物を配置します。</summary>
        /// <param name="arguments">SDK に渡されたコマンドライン引数。</param>
        /// <param name="workingDirectory">SDK process の作業ディレクトリ。</param>
        /// <param name="cancellationToken">SDK 呼び出しを取り消す token。</param>
        /// <param name="runContext">SDK session と出力先を示す任意 context。</param>
        /// <returns>SDK 呼び出しを模擬した終了コード、標準出力、標準エラー。</returns>
        public FileBasedSdkProcessResult Run(
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            FileBasedSdkRunContext? runContext = null)
        {
            RunContext = runContext;
            Calls.Add(arguments.ToArray());
            if (arguments.SequenceEqual(["--version"], StringComparer.Ordinal))
            {
                int call = Interlocked.Increment(ref sdkVersionCallCount);
                currentSdk = sdkSelection?.Invoke(call) ?? currentSdk;
                return new FileBasedSdkProcessResult(0, currentSdk.Version, "");
            }

            if (arguments.SequenceEqual(["--info"], StringComparer.Ordinal))
            {
                return new FileBasedSdkProcessResult(0,
                    $".NET SDK:\n Version: {currentSdk.Version}\n Base Path: {currentSdk.BasePath}", "");
            }

            string artifacts = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(entryPath, runContext!.RunFileRoot);
            if (arguments.Any(argument => argument.StartsWith("-getProperty:", StringComparison.Ordinal)))
            {
                EvaluationCallCount++;
                return new FileBasedSdkProcessResult(0, EvaluationJson(artifacts, runContext.NuGetPackagesDirectory), "");
            }

            if (arguments.FirstOrDefault() == "restore")
            {
                RestoreCallCount++;
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
                WriteSdkIdentityRecord(arguments, sdkExecutionSelection?.Invoke("restore") ?? currentSdk);
                afterRestore?.Invoke();
                return new FileBasedSdkProcessResult(0, "", "");
            }

            if (arguments.FirstOrDefault() == "build")
            {
                BuildCallCount++;
                string assemblyPath = FileBasedCsWorkflowBuilder.GetSdkOutputAssemblyPath(entryPath, runContext.RunFileRoot);
                GeneratedAssemblyPath = assemblyPath;
                Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
                File.Copy(fixtureAssemblyPath, assemblyPath, overwrite: true);
                WriteSdkIdentityRecord(arguments, sdkExecutionSelection?.Invoke("build") ?? currentSdk);
                if (!arguments.Any(argument => argument.StartsWith("-getProperty:", StringComparison.Ordinal)))
                {
                    afterBuild?.Invoke();
                }
                return new FileBasedSdkProcessResult(0, "", "");
            }

            throw new InvalidOperationException($"Unexpected SDK fixture invocation: {string.Join(' ', arguments)}");
        }

        /// <summary>SDK の評価結果として返す実効 property と package 参照を JSON 化します。</summary>
        /// <param name="artifacts">SDK が使用する成果物の基準ディレクトリ。</param>
        /// <param name="packagesDirectory">restore 対象の NuGet packages directory。</param>
        /// <returns>builder が評価結果として解析する JSON。</returns>
        private string EvaluationJson(string artifacts, string packagesDirectory)
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
                    ["MSBuildSDKsPath"] = currentSdk.BasePath + "Sdks",
                    ["MSBuildExtensionsPath"] = currentSdk.BasePath,
                    ["NETCoreSdkVersion"] = currentSdk.Version,
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

        /// <summary>SDK process が実際に使用した版と配置先の検証記録を作成します。</summary>
        /// <param name="arguments">SDK process に渡された記録先 property を含む引数。</param>
        /// <param name="sdkIdentity">その process で使用したものとして記録する SDK。</param>
        private static void WriteSdkIdentityRecord(
            IReadOnlyList<string> arguments,
            (string Version, string BasePath) sdkIdentity)
        {
            const string prefix = "-p:Devo6SdkIdentityRecordPath=";
            string recordPath = arguments.Single(argument => argument.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
            Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
            File.WriteAllText(recordPath + ".version", sdkIdentity.Version);
            File.WriteAllText(recordPath + ".sdks", Path.Combine(sdkIdentity.BasePath, "Sdks"));
            File.WriteAllText(recordPath + ".extensions", Path.TrimEndingDirectorySeparator(sdkIdentity.BasePath));
        }

        private const string SdkInfo = ".NET SDK:\n Version: 10.0.404\n Base Path: /usr/share/dotnet/sdk/10.0.404/";
    }
}
#endif
