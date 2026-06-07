using System.Diagnostics;
using System.Text;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// T31 の C# coding standard を機械的に確認します。
/// </summary>
public sealed class CodingStandardsContractTests
{
    /// <summary>
    /// source、test、tool の通常宣言が T31 の関数とプロパティの文書注釈標準を満たすことを確認します。
    /// </summary>
    [Fact(DisplayName = "C# declarations follow T31 coding standards")]
    public async Task CSharpDeclarationsFollowT31CodingStandards()
    {
        string repositoryRoot = FindRepositoryRoot();
        string toolProjectPath = Path.Combine(repositoryRoot, "tools", "csharp-xml-doc-checker", "CSharpXmlDocChecker.csproj");
        using Process process = StartCheckerProcess(repositoryRoot, repositoryRoot, toolProjectPath);

        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            "T31 coding standard violations were found:"
            + Environment.NewLine
            + BuildFailureMessage(standardOutput, standardError));
    }

    /// <summary>
    /// tools 配下の未注釈プロパティが違反として検出されることを確認します。
    /// </summary>
    [Fact(DisplayName = "C# checker inspects tool source declarations")]
    public async Task CSharpCheckerInspectsToolSourceDeclarations()
    {
        string repositoryRoot = FindRepositoryRoot();
        string toolProjectPath = Path.Combine(repositoryRoot, "tools", "csharp-xml-doc-checker", "CSharpXmlDocChecker.csproj");
        using TemporaryRepository temporaryRepository = new();
        string sourceDirectory = Path.Combine(temporaryRepository.RootPath, "tools", "demo");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "Foo.cs"),
            """
            public sealed class Foo
            {
                public string Name { get; } = "demo";
            }
            """);

        using Process process = StartCheckerProcess(repositoryRoot, temporaryRepository.RootPath, toolProjectPath);
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 1,
            "tools 配下の未注釈 property が検出されませんでした。"
            + Environment.NewLine
            + BuildFailureMessage(standardOutput, standardError));
        Assert.Contains("tools/demo/Foo.cs", standardOutput);
        Assert.Contains("プロパティ `Name` に XML コメントがありません。", standardOutput);
    }

    /// <summary>
    /// 複数行 property の未注釈宣言が違反として検出されることを確認します。
    /// </summary>
    [Fact(DisplayName = "C# checker detects multi-line property declarations")]
    public async Task CSharpCheckerDetectsMultiLinePropertyDeclarations()
    {
        string repositoryRoot = FindRepositoryRoot();
        string toolProjectPath = Path.Combine(repositoryRoot, "tools", "csharp-xml-doc-checker", "CSharpXmlDocChecker.csproj");
        using TemporaryRepository temporaryRepository = new();
        string sourceDirectory = Path.Combine(temporaryRepository.RootPath, "src");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "Foo.cs"),
            """
            public sealed class Foo
            {
                public string Name
                {
                    get;
                }
            }
            """);

        using Process process = StartCheckerProcess(repositoryRoot, temporaryRepository.RootPath, toolProjectPath);
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 1,
            "複数行 property の未注釈宣言が検出されませんでした。"
            + Environment.NewLine
            + BuildFailureMessage(standardOutput, standardError));
        Assert.Contains("src/Foo.cs", standardOutput);
        Assert.Contains("プロパティ `Name` に XML コメントがありません。", standardOutput);
    }

    /// <summary>
    /// XML コメント検査ツールを別 process として起動します。
    /// </summary>
    private static Process StartCheckerProcess(string workingDirectory, string targetRepositoryRoot, string toolProjectPath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(toolProjectPath);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(targetRepositoryRoot);

        return Process.Start(startInfo) ?? throw new InvalidOperationException("XML コメント検査ツールを起動できませんでした。");
    }

    /// <summary>
    /// 標準出力と標準エラーを xUnit の失敗 message にまとめます。
    /// </summary>
    private static string BuildFailureMessage(string standardOutput, string standardError)
    {
        StringBuilder builder = new();
        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            builder.AppendLine(standardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            builder.AppendLine(standardError.TrimEnd());
        }

        return builder.ToString();
    }

    /// <summary>
    /// solution file を基準に repository root を探します。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Devo6.WorkFlow.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    /// <summary>
    /// 検査用の一時 repository directory を管理します。
    /// </summary>
    private sealed class TemporaryRepository : IDisposable
    {
        /// <summary>
        /// 一時 repository directory を作成します。
        /// </summary>
        public TemporaryRepository()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "devo6-workflow-standards-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        /// <summary>
        /// 一時 repository の root path を保持します。
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// 一時 repository directory を削除します。
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
