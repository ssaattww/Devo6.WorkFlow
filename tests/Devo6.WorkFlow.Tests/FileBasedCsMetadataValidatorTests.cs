#if NET10_0
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// PE metadata validate が user code を実行しないことを検証します。
/// </summary>
public sealed class FileBasedCsMetadataValidatorTests
{
    [Fact(DisplayName = "metadata validation accepts a provider without executing any user code")]
    /// <summary>
    /// 有効な Provider metadata を検証して user code が動かないことを確認します。
    /// </summary>
    public void ValidProviderMetadataDoesNotExecuteUserCode()
    {
        string directory = CreateDirectory();
        string markerDirectory = Path.Combine(directory, "markers");
        Directory.CreateDirectory(markerDirectory);
        string assemblyPath = CompileFixture(directory, ValidFixture(markerDirectory));

        WorkflowValidationResult result = FileBasedCsMetadataValidator.Validate(assemblyPath, "Main");

        Assert.True(result.Succeeded, FormatErrors(result));
        Assert.Empty(Directory.GetFiles(markerDirectory));
        Assert.DoesNotContain(
            AssemblyLoadContext.Default.Assemblies,
            assembly => string.Equals(assembly.Location, assemblyPath, StringComparison.Ordinal));
    }

    [Theory(DisplayName = "metadata validation rejects invalid provider shapes without loading them")]
    [InlineData("internal sealed class MainWorkflow", "public MainWorkflow() { }")]
    [InlineData("public abstract class MainWorkflow", "public MainWorkflow() { }")]
    [InlineData("public sealed class MainWorkflow<T>", "public MainWorkflow() { }")]
    [InlineData("public class MainWorkflow", "public MainWorkflow() { }")]
    [InlineData("public sealed class MainWorkflow", "private MainWorkflow() { }")]
    /// <summary>
    /// 契約外 Provider の型形状が拒否されることを確認します。
    /// </summary>
    public void InvalidProviderShapeIsRejected(string declaration, string constructor)
    {
        string directory = CreateDirectory();
        string source = $$"""
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")]
            {{declaration}} : IWorkflowEntryProvider
            {
                {{constructor}}
                public IWorkflowEntry Create() => null!;
            }
            """;
        string assemblyPath = CompileFixture(directory, source);

        WorkflowValidationResult result = FileBasedCsMetadataValidator.Validate(assemblyPath, "Main");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.ScriptEntryProviderInvalid, Assert.Single(result.Errors).Code);
    }

    [Fact(DisplayName = "metadata validation diagnoses an absent requested entry")]
    /// <summary>
    /// 要求 Entry がない場合の診断を確認します。
    /// </summary>
    public void MissingEntryIsDiagnosed()
    {
        string directory = CreateDirectory();
        string assemblyPath = CompileFixture(directory, ValidFixture(Path.Combine(directory, "unused"), entryName: "Deploy"));

        WorkflowValidationResult result = FileBasedCsMetadataValidator.Validate(assemblyPath, "Missing");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.EntryStepNotFound, Assert.Single(result.Errors).Code);
    }

    [Fact(DisplayName = "metadata validation diagnoses duplicate entry names")]
    /// <summary>
    /// 重複 Entry 名の診断を確認します。
    /// </summary>
    public void DuplicateEntryNamesAreDiagnosed()
    {
        string directory = CreateDirectory();
        string source = """
            using Devo6.WorkFlow.Engine;
            [WorkflowEntry("Main")] public sealed class First : IWorkflowEntryProvider { public First() { } public IWorkflowEntry Create() => null!; }
            [WorkflowEntry("Main")] public sealed class Second : IWorkflowEntryProvider { public Second() { } public IWorkflowEntry Create() => null!; }
            """;
        string assemblyPath = CompileFixture(directory, source);

        WorkflowValidationResult result = FileBasedCsMetadataValidator.Validate(assemblyPath, "Main");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowErrorCodes.DuplicateStepName, Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// metadata-only validation 用の managed source fixture を作成します。
    /// </summary>
    private static string ValidFixture(string markerDirectory, string entryName = "Main")
    {
        string escapedMarkerDirectory = markerDirectory.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return $$"""
            using System;
            using System.IO;
            using System.Runtime.CompilerServices;
            using Devo6.WorkFlow.Abstractions;
            using Devo6.WorkFlow.Engine;

            internal static class Markers
            {
                internal static void Touch(string name) => File.WriteAllText(Path.Combine("{{escapedMarkerDirectory}}", name), name);
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MarkerAttribute : Attribute
            {
                public MarkerAttribute() => Markers.Touch("attribute");
            }

            public static class ModuleMarker
            {
                [ModuleInitializer]
                public static void Initialize() => Markers.Touch("module");
            }

            [Marker]
            [WorkflowEntry("{{entryName}}")]
            public sealed class MainWorkflow : IWorkflowEntryProvider
            {
                static MainWorkflow() => Markers.Touch("static");
                public MainWorkflow() => Markers.Touch("provider-constructor");
                public IWorkflowEntry Create()
                {
                    Markers.Touch("create");
                    return CompositeStep.Define("{{entryName}}").Run<MarkerStep, string>();
                }
            }

            public sealed class MarkerStep : IStep<string>
            {
                public MarkerStep() => Markers.Touch("step-constructor");
                public string Execute(StepInput input)
                {
                    Markers.Touch("step-execute");
                    return "ok";
                }
            }
            """;
    }

    /// <summary>
    /// C# fixture を一時 managed assembly に compile します。
    /// </summary>
    private static string CompileFixture(string directory, string source)
    {
        string assemblyPath = Path.Combine(directory, "metadata-fixture.dll");
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        IEnumerable<MetadataReference> references = trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(WorkflowEntryAttribute).Assembly.Location)
            .Append(typeof(WorkflowErrorCodes).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(assemblyPath),
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

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
        string directory = Path.Combine(Path.GetTempPath(), "devo6-workflow-metadata-validate-tests", Guid.NewGuid().ToString("N"));
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
