#if NET10_0
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// SDK runner cancellation does not release its output lock while child processes can still write.
/// </summary>
public sealed class FileBasedSdkRunnerCancellationTests
{
    [Fact(DisplayName = "builder keeps the same output locked until a cancelled restore child closes its pipes")]
    /// <summary>
    /// 先行する復元の子 process が出力 pipe を保持する間、builder 経由の次要求が待機します。
    /// </summary>
    public async Task BuilderWaitsForCancelledRestoreChildBeforeStartingTheNextRestore()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), "devo6-builder-cancel-lock", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "workflow.cs");
        string feedPath = Path.Combine(directory, "feed");
        string scriptPath = Path.Combine(directory, "sdk-mock.sh");
        string childStartedPath = Path.Combine(directory, "child-started");
        string parentPidPath = Path.Combine(directory, "parent-pid");
        string releasePath = Path.Combine(directory, "release-child");
        string childPendingPath = Path.Combine(directory, "child-output.pending");
        string childCompletedPath = Path.Combine(directory, "child-completed");
        const string childPayload = "restore-child-finished-and-closed";
        string outputRoot = Path.Combine(directory, "sdk-output");
        Directory.CreateDirectory(feedPath);
        File.WriteAllText(sourcePath, string.Join(Environment.NewLine,
        [
            "#:property OutputType=Library",
            "#:property TargetFramework=net10.0",
            "#:property PublishAot=false",
            "#:package Devo6.WorkFlow.Engine@[0.1.0]",
            "public sealed class MainWorkflow { }",
        ]));
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
        File.WriteAllText(scriptPath,
            $"#!/bin/sh\n(echo started > '{childStartedPath}'; while [ ! -f '{releasePath}' ]; do sleep 0.01; done; printf '%s' '{childPayload}' > '{childPendingPath}' && mv '{childPendingPath}' '{childCompletedPath}') &\necho $$ > '{parentPidPath}'\nexit 0\n");
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var firstRunner = new BuilderLockTestSdkRunner(scriptPath, childCompletedPath, outputRoot, blockFirstRestore: true);
        var secondRunner = new BuilderLockTestSdkRunner(scriptPath, childCompletedPath, outputRoot, blockFirstRestore: false);
        var firstBuilder = new FileBasedCsWorkflowBuilder(firstRunner, () => outputRoot);
        var lockContentionObserved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBuilder = new FileBasedCsWorkflowBuilder(secondRunner, () => outputRoot, path => lockContentionObserved.TrySetResult(path));
        using var cancellation = new CancellationTokenSource();
        Task<FileBasedCsBuildResult> firstRequest = Task.Run(() => firstBuilder.Build(sourcePath, "0.1.0", cancellation.Token));
        Task<FileBasedCsBuildResult>? secondRequest = null;

        try
        {
            await WaitForFile(parentPidPath);
            await WaitForFile(childStartedPath);
            await WaitForProcessExit(int.Parse(File.ReadAllText(parentPidPath).Trim()));
            await cancellation.CancelAsync();
            Assert.False(firstRequest.IsCompleted, "The cancelled builder must wait while the restore child keeps its output pipes open.");

            secondRequest = Task.Run(() => secondBuilder.Build(sourcePath, "0.1.0"));
            string contendedLockPath = await lockContentionObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            string artifactsDirectory = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(sourcePath, outputRoot);
            string expectedLockPath = FileBasedCsWorkflowBuilder.GetFileBasedOutputLockPath(outputRoot, artifactsDirectory, OperatingSystem.IsWindows());
            Assert.Equal(expectedLockPath, contendedLockPath);
            Assert.False(firstRequest.IsCompleted, "The first builder must still own the output lock when the second builder reports contention.");
            Assert.Empty(secondRunner.Calls);
            Assert.False(secondRunner.RestoreStarted.Task.IsCompleted, "The next restore started before the cancelled child completed.");

            File.WriteAllText(releasePath, "release");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstRequest.WaitAsync(TimeSpan.FromSeconds(10)));
            await secondRunner.RestoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(childPayload, secondRunner.RestoreObservedContent);
            FileBasedCsBuildResult secondResult = await secondRequest.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(secondResult.Succeeded);
            Assert.Equal(WorkflowErrorCodes.ScriptNugetRestoreFailed, secondResult.ErrorCode);
        }
        finally
        {
            File.WriteAllText(releasePath, "release");
            await cancellation.CancelAsync();
            if (!firstRequest.IsCompleted)
            {
                try
                {
                    await firstRequest.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (secondRequest is not null && !secondRequest.IsCompleted)
            {
                await secondRequest.WaitAsync(TimeSpan.FromSeconds(10));
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// pipe を継承せず残った子 process の遅延書込みが後続 build の成果物へ届かないことを確認します。
    /// </summary>
    [Fact(DisplayName = "a detached SDK writer remains inside its request output after cancellation")]
    public async Task DetachedWriterCannotReachTheNextBuildOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), "devo6-isolated-sdk-writer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "workflow.cs");
        string feedPath = Path.Combine(directory, "feed");
        string scriptPath = Path.Combine(directory, "sdk-mock.sh");
        string childStartedPath = Path.Combine(directory, "child-started");
        string childCompletedPath = Path.Combine(directory, "child-completed");
        string releasePath = Path.Combine(directory, "release-child");
        string outputRoot = Path.Combine(Path.GetTempPath(), "devo6-isolated-sdk-output", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(feedPath);
        File.WriteAllText(sourcePath, string.Join(Environment.NewLine,
        [
            "#:property OutputType=Library",
            "#:property TargetFramework=net10.0",
            "#:property PublishAot=false",
            "#:package Devo6.WorkFlow.Engine@[0.1.0]",
            "public sealed class MainWorkflow { }",
        ]));
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
        string artifactsName = Path.GetFileName(FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(sourcePath, outputRoot));
        string lateMarkerPath = Path.Combine("$XDG_DATA_HOME", "dotnet", "runfile", artifactsName, "bin", "debug", "late-writer.marker");
        File.WriteAllText(scriptPath, "#!/bin/sh" + Environment.NewLine + $$"""
setsid sh -c 'echo started > "{{childStartedPath}}"; while [ ! -f "{{releasePath}}" ]; do sleep 0.01; done; mkdir -p "$(dirname "{{lateMarkerPath}}")"; printf old-run > "{{lateMarkerPath}}"; echo completed > "{{childCompletedPath}}"' </dev/null >/dev/null 2>&1 &
while [ ! -f "{{childStartedPath}}" ]; do sleep 0.01; done
echo $$
exit 0
""");
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using var cancellation = new CancellationTokenSource();
        var detachedChildReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRunner = new BuilderLockTestSdkRunner(
            scriptPath,
            childCompletedPath,
            outputRoot,
            blockFirstRestore: true,
            afterFirstRestoreProcess: () =>
            {
                detachedChildReady.TrySetResult();
                cancellation.Cancel();
            });
        var firstBuilder = new FileBasedCsWorkflowBuilder(firstRunner, () => outputRoot);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(
                () => firstBuilder.Build(sourcePath, "0.1.0", cancellation.Token)));
            await detachedChildReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
            string firstRunRoot = Assert.Single(firstRunner.RunContexts.Values).RunFileRoot;

            var secondRunner = new BuilderLockTestSdkRunner(
                scriptPath,
                childCompletedPath,
                outputRoot,
                blockFirstRestore: false,
                succeedBuild: true);
            var secondBuilder = new FileBasedCsWorkflowBuilder(secondRunner, () => outputRoot);
            FileBasedCsBuildResult secondResult = secondBuilder.Build(sourcePath, "0.1.0");

            Assert.True(secondResult.Succeeded, $"{secondResult.ErrorCode}: {secondResult.ErrorMessage}");
            Assert.NotNull(secondResult.AssemblyPath);
            string secondRunRoot = Assert.Single(secondRunner.RunContexts.Values).RunFileRoot;
            string secondOutput = Path.Combine(secondRunRoot, "second-build-marker");
            Assert.NotEqual(firstRunRoot, secondRunRoot);
            Assert.False(File.Exists(childCompletedPath));
            Assert.Equal("new-run", File.ReadAllText(secondOutput));
            Assert.False(File.Exists(Path.Combine(secondRunRoot, "bin", "debug", "late-writer.marker")));

            File.WriteAllText(releasePath, "release");
            await WaitForFile(childCompletedPath);
            Assert.Equal("old-run", File.ReadAllText(Path.Combine(
                firstRunRoot,
                artifactsName,
                "bin",
                "debug",
                "late-writer.marker")));
            Assert.Equal("new-run", File.ReadAllText(secondOutput));
        }
        finally
        {
            File.WriteAllText(releasePath, "release");
            if (File.Exists(childCompletedPath))
            {
                await WaitForFile(childCompletedPath);
            }

            Directory.Delete(directory, recursive: true);
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "SDK child receives fixed CLI UI language without changing the caller")]
    /// <summary>
    /// SDK の表示言語だけを子 process に固定し、親の環境変数を変更しません。
    /// </summary>
    public void CliUiLanguageIsPinnedForSdkChildOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), "devo6-sdk-ui-language", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "sdk-mock.sh");
        File.WriteAllText(scriptPath, "#!/bin/sh\nprintf '%s' \"$DOTNET_CLI_UI_LANGUAGE\"\n");
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string? callerLanguage = Environment.GetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE");

        var runner = new DotnetFileBasedSdkRunner(scriptPath);
        FileBasedSdkProcessResult infoResult = runner.Run(["--info"], directory, CancellationToken.None);
        FileBasedSdkProcessResult buildResult = runner.Run(["build"], directory, CancellationToken.None);

        Assert.Equal("en-US", infoResult.StandardOutput);
        Assert.Equal(callerLanguage ?? "", buildResult.StandardOutput);
        Assert.Equal(callerLanguage, Environment.GetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE"));
    }

    [Fact(DisplayName = "cancelling an SDK mock terminates its child before the runner returns")]
    /// <summary>
    /// cancellation が子 process tree と pipe の終了を待つことを fixture で確認します。
    /// </summary>
    public async Task CancellationWaitsForProcessTreeAndOutputPipes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), "devo6-sdk-runner-cancel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "sdk-mock.sh");
        string entryPath = Path.Combine(directory, "workflow.cs");
        string rootPath = Path.Combine(directory, "sdk-root");
        string startedPath = Path.Combine(directory, "child-started");
        string parentPidPath = Path.Combine(directory, "parent-pid");
        string releasePath = Path.Combine(directory, "release-child");
        string completedPath = Path.Combine(directory, "child-completed");
        File.WriteAllText(entryPath, "class Fixture { }");
        File.WriteAllText(scriptPath, $"#!/bin/sh\n(sleep 0.01; echo started > '{startedPath}'; while [ ! -f '{releasePath}' ]; do sleep 0.01; done; echo completed > '{completedPath}') &\necho $$ > '{parentPidPath}'\nexit 0\n");
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var runner = new DotnetFileBasedSdkRunner(scriptPath);
        using var cancellation = new CancellationTokenSource();
        string artifactsDirectory = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(entryPath, rootPath);
        string lockPath = FileBasedCsWorkflowBuilder.GetFileBasedOutputLockPath(rootPath, artifactsDirectory, OperatingSystem.IsWindows());
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        FileStream outputLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Task runnerTask = Task.Run(() => runner.Run(["restore"], directory, cancellation.Token));

        await WaitForFile(parentPidPath);
        await WaitForFile(startedPath);
        await WaitForProcessExit(int.Parse(File.ReadAllText(parentPidPath).Trim()));
        TaskCompletionSource nextRequestAcquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task nextRequest = Task.Run(async () =>
        {
            using FileStream nextLock = await WaitForOutputLock(lockPath);
            Assert.True(File.Exists(completedPath), "The next request acquired the output lock before the child finished writing.");
            nextRequestAcquired.TrySetResult();
        });
        await cancellation.CancelAsync();
        Assert.False(runnerTask.IsCompleted, "The cancelled request must retain its output lock while the child holds the redirected pipes.");
        Assert.False(nextRequestAcquired.Task.IsCompleted, "A same-output request must wait for the first output lock.");

        File.WriteAllText(releasePath, "release");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runnerTask.WaitAsync(TimeSpan.FromSeconds(10)));
        outputLock.Dispose();
        await nextRequestAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await nextRequest.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("completed", File.ReadAllText(completedPath).Trim());
    }

    /// <summary>
    /// 指定ファイルが作成されるまで短い間隔で確認します。
    /// </summary>
    /// <param name="path">作成を待つファイルのパス。</param>
    /// <returns>ファイルを確認した時点で完了するタスク。</returns>
    private static async Task WaitForFile(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    /// <summary>
    /// 指定した子プロセスが終了するまで待ちます。
    /// </summary>
    /// <param name="processId">終了を待つプロセスの ID。</param>
    /// <returns>プロセスの終了を確認した時点で完了するタスク。</returns>
    private static async Task WaitForProcessExit(int processId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    /// <summary>
    /// 排他中の出力ロックを開けるまで再試行します。
    /// </summary>
    /// <param name="lockPath">取得するロックファイルのパス。</param>
    /// <returns>排他ロックを取得したファイルストリーム。</returns>
    private static async Task<FileStream> WaitForOutputLock(string lockPath)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }
        }
    }

    private sealed class BuilderLockTestSdkRunner : IFileBasedSdkRunner
    {
        private const string SdkInfo = ".NET SDK:\n Version: 10.0.404\n Base Path: /usr/share/dotnet/sdk/10.0.404/";
        private readonly DotnetFileBasedSdkRunner processRunner;
        private readonly string childCompletedPath;
        private readonly string outputRoot;
        private readonly bool blockFirstRestore;
        private readonly bool succeedBuild;
        private readonly Action? afterFirstRestoreProcess;
        private int restoreCount;

        /// <summary>
        /// 子プロセス fixture を呼び出す SDK runner のテスト用モックを作成します。
        /// </summary>
        /// <param name="scriptPath">SDK 呼出しとして起動する fixture のパス。</param>
        /// <param name="childCompletedPath">子プロセスが完了時に公開するファイルのパス。</param>
        /// <param name="blockFirstRestore">最初の restore を fixture に委ねる場合は true。</param>
        public BuilderLockTestSdkRunner(
            string scriptPath,
            string childCompletedPath,
            string outputRoot,
            bool blockFirstRestore,
            bool succeedBuild = false,
            Action? afterFirstRestoreProcess = null)
        {
            processRunner = new DotnetFileBasedSdkRunner(scriptPath);
            this.childCompletedPath = childCompletedPath;
            this.outputRoot = outputRoot;
            this.blockFirstRestore = blockFirstRestore;
            this.succeedBuild = succeedBuild;
            this.afterFirstRestoreProcess = afterFirstRestoreProcess;
        }

        /// <summary>
        /// モックへ渡された SDK 引数を呼出し順に保持します。
        /// </summary>
        public ConcurrentQueue<IReadOnlyList<string>> Calls { get; } = new();

        /// <summary>
        /// SDK 要求間で異なる専用 runfile root を保持します。
        /// </summary>
        public ConcurrentDictionary<string, FileBasedSdkRunContext> RunContexts { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// restore の開始を通知する同期用タスクです。
        /// </summary>
        public TaskCompletionSource RestoreStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// restore 開始時に読み取った子プロセス完了ファイルの内容です。
        /// </summary>
        public string? RestoreObservedContent { get; private set; }

        /// <summary>
        /// SDK 呼出しを記録し、検査用の応答または fixture の実行結果を返します。
        /// </summary>
        /// <param name="arguments">SDK に渡す引数。</param>
        /// <param name="workingDirectory">SDK の作業ディレクトリ。</param>
        /// <param name="cancellationToken">SDK 呼出しのキャンセル通知。</param>
        /// <returns>モックまたは fixture の終了コードと出力を含む結果。</returns>
        public FileBasedSdkProcessResult Run(
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            FileBasedSdkRunContext? runContext = null)
        {
            Calls.Enqueue(arguments.ToArray());
            if (runContext is not null)
            {
                RunContexts.TryAdd(runContext.RunFileRoot, runContext);
            }
            if (arguments.Count == 1 && arguments[0] == "--version")
            {
                return new FileBasedSdkProcessResult(0, "10.0.404", "");
            }

            if (arguments.Count == 1 && arguments[0] == "--info")
            {
                return new FileBasedSdkProcessResult(0, SdkInfo, "");
            }

            if (arguments.Any(argument => argument.StartsWith("-getProperty:", StringComparison.Ordinal)))
            {
                return new FileBasedSdkProcessResult(0, CreateEvaluationJson(
                    arguments[1],
                    runContext?.RunFileRoot ?? outputRoot,
                    runContext?.NuGetPackagesDirectory ?? Path.Combine(outputRoot, "nuget-packages")), "");
            }

            if (arguments.Count > 0 && arguments[0] == "restore")
            {
                int currentRestore = Interlocked.Increment(ref restoreCount);
                if (blockFirstRestore && currentRestore == 1)
                {
                    FileBasedSdkProcessResult processResult = processRunner.Run(arguments, workingDirectory, cancellationToken, runContext);
                    afterFirstRestoreProcess?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                    return processResult;
                }

                if (succeedBuild)
                {
                    string artifacts = FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(arguments[1], runContext!.RunFileRoot);
                    string assetsPath = Path.Combine(artifacts, "obj", "project.assets.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
                    File.WriteAllText(assetsPath, CreateAssetsJson(
                        arguments[1],
                        Path.Combine(workingDirectory, "feed"),
                        runContext.NuGetPackagesDirectory));
                    WriteSdkIdentityRecord(arguments, "10.0.404", "/usr/share/dotnet/sdk/10.0.404/");
                    RestoreStarted.TrySetResult();
                    return new FileBasedSdkProcessResult(0, "", "");
                }

                try
                {
                    RestoreObservedContent = File.ReadAllText(childCompletedPath);
                }
                catch (IOException)
                {
                    RestoreObservedContent = null;
                }
                RestoreStarted.TrySetResult();
                return new FileBasedSdkProcessResult(1, "controlled restore failure", "");
            }

            if (arguments.Count > 0 && arguments[0] == "build" && succeedBuild)
            {
                string assemblyPath = FileBasedCsWorkflowBuilder.GetSdkOutputAssemblyPath(arguments[1], runContext!.RunFileRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
                File.WriteAllText(assemblyPath, "new-run");
                File.WriteAllText(Path.Combine(runContext.RunFileRoot, "second-build-marker"), "new-run");
                WriteSdkIdentityRecord(arguments, "10.0.404", "/usr/share/dotnet/sdk/10.0.404/");
                return new FileBasedSdkProcessResult(0, "", "");
            }

            return new FileBasedSdkProcessResult(0, "", "");
        }

        /// <summary>
        /// SDK の実効 output directory を反映した評価 JSON を生成します。
        /// </summary>
        /// <param name="entryPath">SDK が評価する file-based source path。</param>
        /// <returns>評価した project property と package reference の JSON。</returns>
        private string CreateEvaluationJson(string entryPath, string sdkRunFileRoot, string nugetPackagesDirectory)
        {
            string artifacts = Path.GetFullPath(FileBasedCsWorkflowBuilder.GetSdkArtifactsDirectory(
                entryPath,
                sdkRunFileRoot));
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
                    ["RestoreSources"] = Path.Combine(Path.GetDirectoryName(entryPath)!, "feed"),
                    ["RestorePackagesPath"] = nugetPackagesDirectory,
                    ["NETCoreSdkVersion"] = "10.0.404",
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

        /// <summary>SDK process の SDK 版と配置先をテスト記録へ出力します。</summary>
        /// <param name="arguments">SDK process に渡された記録先を含む引数。</param>
        /// <param name="version">SDK process の実行版。</param>
        /// <param name="basePath">SDK process の base path。</param>
        private static void WriteSdkIdentityRecord(IReadOnlyList<string> arguments, string version, string basePath)
        {
            const string prefix = "-p:Devo6SdkIdentityRecordPath=";
            string recordPath = arguments.Single(argument => argument.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
            Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
            File.WriteAllText(recordPath + ".version", version);
            File.WriteAllText(recordPath + ".sdks", Path.Combine(basePath, "Sdks"));
            File.WriteAllText(recordPath + ".extensions", Path.TrimEndingDirectorySeparator(basePath));
        }

        /// <summary>
        /// 一点版 Engine dependency を含む restore assets fixture を生成します。
        /// </summary>
        /// <param name="entryPath">assets の対象となる file-based source。</param>
        /// <param name="feedPath">lock に記録したローカル feed。</param>
        /// <returns>SDK restore 後に出力する assets JSON。</returns>
        private static string CreateAssetsJson(string entryPath, string feedPath, string nugetPackagesDirectory) => JsonSerializer.Serialize(new
        {
            targets = new Dictionary<string, object> { ["net10.0"] = new Dictionary<string, object>() },
            packageFolders = new Dictionary<string, object> { [nugetPackagesDirectory] = new { } },
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
                    packagesPath = nugetPackagesDirectory,
                    sources = new Dictionary<string, object> { [feedPath] = new { } },
                },
            },
        });
    }
}
#endif
