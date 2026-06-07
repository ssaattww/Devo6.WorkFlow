using System.Diagnostics;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// repository の project skeleton を検査します。
/// </summary>
public sealed class ProjectSkeletonTests
{
    /// <summary>
    /// repository root path を保持します。
    /// </summary>
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// solution に必要な project が含まれることを検査します。
    /// </summary>
    [Fact(DisplayName = "solution に中核、CLI、検査 project が含まれる")]
    public void Solutionに必要なProjectが含まれる()
    {
        string solutionText = File
            .ReadAllText(Path.Combine(RepositoryRoot, "Devo6.WorkFlow.sln"))
            .Replace('\\', '/');

        Assert.Contains("src/Devo6.WorkFlow.Abstractions/Devo6.WorkFlow.Abstractions.csproj", solutionText);
        Assert.Contains("src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj", solutionText);
        Assert.Contains("src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj", solutionText);
        Assert.Contains("tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj", solutionText);
    }

    /// <summary>
    /// 検査 project から中核 project を参照できることを検査します。
    /// </summary>
    [Fact(DisplayName = "検査 project から中核 project を参照できる")]
    public void 検査Projectから中核Projectを参照できる()
    {
        string testProjectText = File.ReadAllText(
            Path.Combine(RepositoryRoot, "tests/Devo6.WorkFlow.Tests/Devo6.WorkFlow.Tests.csproj"));

        Assert.Contains(
            @"..\..\src\Devo6.WorkFlow.Engine\Devo6.WorkFlow.Engine.csproj",
            testProjectText);
    }

    /// <summary>
    /// CLI project が最小入口で起動できることを検査します。
    /// </summary>
    /// <returns>非同期検査を表す task。</returns>
    [Fact(DisplayName = "CLI project は最小入口で起動できる")]
    public async Task CliProjectは最小入口で起動できる()
    {
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList =
            {
                "run",
                "--project",
                Path.Combine(RepositoryRoot, "src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj"),
                "--no-build"
            },
            RedirectStandardError = true,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("CLI project の起動に失敗しました。");

        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"終了コード: {process.ExitCode}{Environment.NewLine}標準出力: {standardOutput}{Environment.NewLine}標準エラー: {standardError}");
        Assert.Contains("Devo6.WorkFlow", standardOutput);
    }

    /// <summary>
    /// repository root path を探索します。
    /// </summary>
    /// <returns>検出した repository root path。</returns>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Devo6.WorkFlow.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("repository root を特定できませんでした。");
    }
}
