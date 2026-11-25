#if NET10_0
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// SDK runner cancellation does not release its output lock while child processes can still write.
/// </summary>
public sealed class FileBasedSdkRunnerCancellationTests
{
    [Fact(DisplayName = "cancelling an SDK mock terminates its child before the runner returns")]
    public async Task CancellationWaitsForProcessTreeAndOutputPipes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), "devo6-sdk-runner-cancel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "sdk-mock.sh");
        string markerPath = Path.Combine(directory, "marker");
        File.WriteAllText(scriptPath, $"#!/bin/sh\n(sleep 1; echo late >> '{markerPath}') &\necho started > '{markerPath}'\nwait\n");
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var runner = new DotnetFileBasedSdkRunner(scriptPath);
        using var cancellation = new CancellationTokenSource();
        Task runnerTask = Task.Run(() => runner.Run(["restore"], directory, cancellation.Token));

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(markerPath) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(File.Exists(markerPath), "The harmless local process fixture did not start.");
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runnerTask.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        Assert.Equal("started", File.ReadAllText(markerPath).Trim());
    }
}
#endif
