using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

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
                "--configuration",
                TestBuildConfiguration.Current,
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
    /// CLI プロジェクトがツール用パッケージとして作成できる設定を持つことを検査します。
    /// </summary>
    [Fact(DisplayName = "CLI プロジェクトはツール用パッケージとして設定されている")]
    public void CliProjectIsConfiguredAsDotnetToolPackage()
    {
        XDocument project = XDocument.Load(Path.Combine(
            RepositoryRoot,
            "src/Devo6.WorkFlow.Cli/Devo6.WorkFlow.Cli.csproj"));

        Assert.Equal("Exe", GetProjectProperty(project, "OutputType"));
        Assert.Equal("true", GetProjectProperty(project, "IsPackable"));
        Assert.Equal("true", GetProjectProperty(project, "PackAsTool"));
        Assert.Equal("engine", GetProjectProperty(project, "ToolCommandName"));
        Assert.Equal("Devo6.WorkFlow.Cli", GetProjectProperty(project, "PackageId"));
        Assert.Equal("README.md", GetProjectProperty(project, "PackageReadmeFile"));
    }

    /// <summary>
    /// Engine プロジェクトが参照用パッケージとして作成できる設定を持つことを検査します。
    /// </summary>
    [Fact(DisplayName = "Engine project は参照用パッケージとして設定されている")]
    public void EngineProjectIsConfiguredAsReferencePackage()
    {
        XDocument project = LoadEngineProject();
        XElement abstractionsReference = GetProjectReference(project, @"..\Devo6.WorkFlow.Abstractions\Devo6.WorkFlow.Abstractions.csproj");

        Assert.Equal("true", GetProjectProperty(project, "IsPackable"));
        Assert.Equal("Devo6.WorkFlow.Engine", GetProjectProperty(project, "PackageId"));
        Assert.Equal("README.md", GetProjectProperty(project, "PackageReadmeFile"));
        Assert.Equal("all", abstractionsReference.Attribute("PrivateAssets")?.Value);
    }

    /// <summary>
    /// Engine パッケージに Engine と Abstractions の DLL が含まれることを検査します。
    /// </summary>
    /// <returns>非同期検査を表す task。</returns>
    [Fact(DisplayName = "Engine package は Engine と Abstractions の DLL を含む")]
    public async Task EnginePackageIncludesEngineAndAbstractionsAssemblies()
    {
        string packagePath = await PackEnginePackage();

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(packagePath);

            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/Devo6.WorkFlow.Engine.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/Devo6.WorkFlow.Abstractions.dll");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(packagePath)!, recursive: true);
        }
    }

    /// <summary>
    /// Engine パッケージが Abstractions を別 NuGet 依存として公開しないことを検査します。
    /// </summary>
    /// <returns>非同期検査を表す task。</returns>
    [Fact(DisplayName = "Engine package は Abstractions を NuGet 依存にしない")]
    public async Task EnginePackageDoesNotDeclareAbstractionsDependency()
    {
        string packagePath = await PackEnginePackage();

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(packagePath);
            ZipArchiveEntry nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            await using Stream nuspecStream = nuspecEntry.Open();
            XDocument nuspec = await XDocument.LoadAsync(nuspecStream, LoadOptions.None, CancellationToken.None);

            XNamespace ns = nuspec.Root?.Name.Namespace ?? XNamespace.None;
            IEnumerable<string> dependencyIds = nuspec
                .Descendants(ns + "dependency")
                .Select(element => element.Attribute("id")?.Value)
                .Where(id => id is not null)
                .Cast<string>();

            Assert.DoesNotContain("Devo6.WorkFlow.Abstractions", dependencyIds);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(packagePath)!, recursive: true);
        }
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

    /// <summary>
    /// Engine プロジェクトファイルを読み込みます。
    /// </summary>
    /// <returns>読み込み済みプロジェクトファイル。</returns>
    private static XDocument LoadEngineProject()
    {
        return XDocument.Load(Path.Combine(
            RepositoryRoot,
            "src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj"));
    }

    /// <summary>
    /// プロジェクトファイルから指定した MSBuild プロパティの値を取得します。
    /// </summary>
    /// <param name="project">読み込み済みプロジェクトファイル。</param>
    /// <param name="propertyName">取得するプロパティ名。</param>
    /// <returns>指定したプロパティの値。</returns>
    private static string GetProjectProperty(XDocument project, string propertyName)
    {
        return project
            .Descendants(propertyName)
            .Select(element => element.Value)
            .Single();
    }

    /// <summary>
    /// 指定したプロジェクト参照を取得します。
    /// </summary>
    /// <param name="project">読み込み済みプロジェクトファイル。</param>
    /// <param name="include">取得する参照パス。</param>
    /// <returns>指定したプロジェクト参照。</returns>
    private static XElement GetProjectReference(XDocument project, string include)
    {
        return project
            .Descendants("ProjectReference")
            .Single(element => element.Attribute("Include")?.Value == include);
    }

    /// <summary>
    /// Engine プロジェクトを一時ディレクトリへパッケージ作成します。
    /// </summary>
    /// <returns>作成されたパッケージパス。</returns>
    private static async Task<string> PackEnginePackage()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"devo6-workflow-engine-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList =
            {
                "pack",
                Path.Combine(RepositoryRoot, "src/Devo6.WorkFlow.Engine/Devo6.WorkFlow.Engine.csproj"),
                "--configuration",
                "Release",
                "--output",
                outputDirectory,
                "-p:PackageVersion=0.0.0-test"
            },
            RedirectStandardError = true,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("Engine package の作成に失敗しました。");

        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"終了コード: {process.ExitCode}{Environment.NewLine}標準出力: {standardOutput}{Environment.NewLine}標準エラー: {standardError}");

        return Directory.GetFiles(outputDirectory, "Devo6.WorkFlow.Engine.*.nupkg").Single();
    }
}
