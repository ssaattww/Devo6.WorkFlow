#if NET10_0
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// File-based Provider execution shares host types and checks Entry identity before running Steps.
/// </summary>
public sealed class FileBasedWorkflowExecutorTests
{
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

    [Fact(DisplayName = "file-based steps can use the host ILogger contract")]
    public void StepCanUseSharedHostLoggerType()
    {
        string directory = CreateDirectory();
        string markers = Path.Combine(directory, "markers");
        Directory.CreateDirectory(markers);
        string assemblyPath = CompileFixture(directory, CreateFixture(markers, "Main", usesLogger: true));
        WorkflowValidationResult validation = FileBasedCsMetadataValidator.Validate(
            assemblyPath, "Main", out string? providerTypeName, out string? qualifiedEntryName);
        Assert.True(validation.Succeeded, FormatErrors(validation));

        WorkflowResult result = FileBasedWorkflowExecutor.Execute(
            assemblyPath, providerTypeName!, qualifiedEntryName!, options: null);

        Assert.True(result.Succeeded, $"{result.ErrorCode}: {result.ErrorMessage}");
    }

    [Fact(DisplayName = "missing declared dependencies fail before Provider construction")]
    public void MissingDependencyIsReportedBeforeProviderConstructor()
    {
        string directory = CreateDirectory();
        string marker = Path.Combine(directory, "provider-created");
        string dependencyPath = Path.Combine(directory, "MissingPrivateDependency.dll");
        CompileFixture(directory, "namespace PrivateDependency; public static class Hook { public static void Touch() { } }", dependencyPath);
        string escapedMarker = marker.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        string source = $$"""
            using System.IO;
            using Devo6.WorkFlow.Engine;
            using PrivateDependency;
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
    private static string CompileFixture(string directory, string source, string? outputPath = null, IReadOnlyList<string>? additionalReferences = null)
    {
        string assemblyPath = outputPath ?? Path.Combine(directory, "workflow-fixture.dll");
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        IEnumerable<MetadataReference> references = trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
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
