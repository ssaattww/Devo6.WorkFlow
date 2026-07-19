using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using System.Text.Json;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// dotnet-script と同じ nullable 診断境界、キャッシュ境界、補完設定で csx を処理することを確認します。
/// </summary>
public sealed class DotnetScriptCompatibilityTests
{
    /// <summary>
    /// nullable context 外の nullable annotation を実行時コンパイル失敗として扱うことを確認します。
    /// </summary>
    [Fact(DisplayName = "Execute は nullable context 外の nullable annotation をコンパイル失敗にする")]
    public void ExecuteTreatsNullableAnnotationOutsideContextAsCompileFailure()
    {
        string scriptPath = CreateScript(CreateNullableScript(nullableEnabled: false));

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptCompileFailed, result.ErrorCode);
        Assert.Contains("CS8632", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// nullable context 外の nullable annotation を実行前検証でも同じコンパイル失敗として扱うことを確認します。
    /// </summary>
    [Fact(DisplayName = "Validate は nullable context 外の nullable annotation をコンパイル失敗にする")]
    public void ValidateTreatsNullableAnnotationOutsideContextAsCompileFailure()
    {
        string scriptPath = CreateScript(CreateNullableScript(nullableEnabled: false));

        WorkflowValidationResult result = new CsxEntryLoader().Validate(scriptPath);

        Assert.False(result.Succeeded);
        ValidationError error = Assert.Single(result.Errors);
        Assert.Equal(WorkflowErrorCodes.ScriptCompileFailed, error.Code);
        Assert.Contains("CS8632", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// nullable context を有効にした同等 script は実行できることを確認します。
    /// </summary>
    [Fact(DisplayName = "#nullable enable がある nullable annotation は実行できる")]
    public void ExecuteAllowsNullableAnnotationWhenContextIsEnabled()
    {
        string scriptPath = CreateScript(CreateNullableScript(nullableEnabled: true));

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("Main", result.EntryName);
    }

    /// <summary>
    /// dotnet-script がエラーへ昇格しない通常 warning は実行を妨げないことを確認します。
    /// </summary>
    [Fact(DisplayName = "nullable 以外の通常 warning はコンパイル失敗にしない")]
    public void ExecuteDoesNotPromoteUnrelatedWarning()
    {
        string scriptPath = CreateScript(
            """
            #warning expected ordinary warning
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class WarningStep : IStep<string>
            {
                public string Execute(StepInput input) => "ok";
            }

            var Main = CompositeStep.Define("Main")
                .Run<WarningStep, string>()
                    .StoreAs();
            """);

        WorkflowResult result = new CsxEntryLoader().Execute(scriptPath);

        Assert.True(result.Succeeded, result.ErrorMessage);
    }

    /// <summary>
    /// 明示した cache path が Execute と Validate の依存解決 request に伝達されることを確認します。
    /// </summary>
    [Fact(DisplayName = "明示 DotnetScriptCachePath は Execute と Validate の provider request に伝達される")]
    public void ExplicitCachePathIsPropagatedToExecuteAndValidateProviderRequests()
    {
        string scriptPath = CreateScript(CreateNuGetWorkflowScript());
        string cachePath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "custom-cache");
        var provider = new CapturingNuGetDependencyGraphProvider();
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            DotnetScriptCachePath = cachePath,
            NuGetDependencyGraphProvider = provider,
        });

        WorkflowValidationResult validationResult = loader.Validate(scriptPath);
        WorkflowResult executionResult = loader.Execute(scriptPath);

        Assert.True(
            validationResult.Succeeded,
            string.Join(Environment.NewLine, validationResult.Errors.Select(error => error.Message)));
        Assert.True(executionResult.Succeeded, executionResult.ErrorMessage);
        Assert.Equal(2, provider.ResolveCallCount);
        Assert.All(provider.Requests, request => Assert.Equal(cachePath, request.DotnetScriptCachePath));
    }

    /// <summary>
    /// cache path 未指定時は provider request へ null が伝達されることを確認します。
    /// </summary>
    [Fact(DisplayName = "未指定 DotnetScriptCachePath は provider request で null を維持する")]
    public void DefaultCachePathRemainsNullInProviderRequest()
    {
        string scriptPath = CreateScript(CreateNuGetWorkflowScript());
        var provider = new CapturingNuGetDependencyGraphProvider();
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            NuGetDependencyGraphProvider = provider,
        });

        WorkflowValidationResult result = loader.Validate(scriptPath);

        Assert.True(
            result.Succeeded,
            string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        CsxNuGetDependencyGraphRequest request = Assert.Single(provider.Requests);
        Assert.Null(request.DotnetScriptCachePath);
    }

    /// <summary>
    /// 複数フォルダサンプルが OmniSharp の script NuGet 参照を有効にすることを確認します。
    /// </summary>
    [Fact(DisplayName = "sample の omnisharp.json は script NuGet 参照と net8.0 を有効にする")]
    public void SampleOmniSharpConfigurationEnablesScriptNuGetReferences()
    {
        string repositoryRoot = FindRepositoryRoot();
        string configurationPath = Path.Combine(
            repositoryRoot,
            "samples",
            "multi-folder-composite",
            "omnisharp.json");

        Assert.True(File.Exists(configurationPath), $"OmniSharp configuration was not found: {configurationPath}");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configurationPath));
        JsonElement script = document.RootElement.GetProperty("script");

        Assert.True(script.GetProperty("enableScriptNuGetReferences").GetBoolean());
        Assert.Equal("net8.0", script.GetProperty("defaultTargetFramework").GetString());
    }

    /// <summary>
    /// nullable context の有無を切り替えた検査用 script を作成します。
    /// </summary>
    /// <param name="nullableEnabled">nullable context を有効にする場合は true。</param>
    /// <returns>検査用 csx 本文。</returns>
    private static string CreateNullableScript(bool nullableEnabled)
    {
        string nullableDirective = nullableEnabled ? "#nullable enable" : "";

        return $$"""
            {{nullableDirective}}
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class NullableStep : IStep<string>
            {
                public string Execute(StepInput input)
                {
                    string? value = "ok";
                    return value;
                }
            }

            var Main = CompositeStep.Define("Main")
                .Run<NullableStep, string>()
                    .StoreAs();
            """;
    }

    /// <summary>
    /// 固定版 NuGet 参照を含む検査用 script を作成します。
    /// </summary>
    /// <returns>NuGet 参照付き csx 本文。</returns>
    private static string CreateNuGetWorkflowScript()
    {
        return """
            #r "nuget: Example.Package, 1.0.0"
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            public sealed class MainStep : IStep<string>
            {
                public string Execute(StepInput input) => "ok";
            }

            var Main = CompositeStep.Define("Main")
                .Run<MainStep, string>()
                    .StoreAs();
            """;
    }

    /// <summary>
    /// 一時 directory に検査用 csx を作成します。
    /// </summary>
    /// <param name="source">書き込む csx 本文。</param>
    /// <returns>作成した csx の絶対 path。</returns>
    private static string CreateScript(string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"devo6-dotnet-script-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "main.csx");
        File.WriteAllText(scriptPath, source);

        return scriptPath;
    }

    /// <summary>
    /// solution file を持つ repository root を探索します。
    /// </summary>
    /// <returns>repository root の絶対 path。</returns>
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

        throw new DirectoryNotFoundException("Repository root containing Devo6.WorkFlow.sln was not found.");
    }

    /// <summary>
    /// provider request を記録し、外部通信なしで固定 dependency graph を返します。
    /// </summary>
    private sealed class CapturingNuGetDependencyGraphProvider : ICsxNuGetDependencyGraphProvider
    {
        /// <summary>
        /// 記録した provider request を取得します。
        /// </summary>
        public List<CsxNuGetDependencyGraphRequest> Requests { get; } = [];

        /// <summary>
        /// Resolve 呼び出し回数を取得します。
        /// </summary>
        public int ResolveCallCount => Requests.Count;

        /// <summary>
        /// request を記録し、固定 dependency graph を返します。
        /// </summary>
        /// <param name="directReferences">script から読んだ直接参照。</param>
        /// <param name="request">dependency graph request。</param>
        /// <returns>固定 dependency graph。</returns>
        public CsxNuGetDependencyGraph Resolve(
            IReadOnlyList<CsxNuGetReference> directReferences,
            CsxNuGetDependencyGraphRequest request)
        {
            Requests.Add(request);

            return new CsxNuGetDependencyGraph(
                [new CsxResolvedNuGetDependency("Example.Package", "1.0.0", isDirect: true)]);
        }
    }
}
