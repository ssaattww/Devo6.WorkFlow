using Devo6.WorkFlow.Abstractions;
using Dotnet.Script.Core;
using Dotnet.Script.DependencyModel.Context;
using Dotnet.Script.DependencyModel.Logging;
using Dotnet.Script.DependencyModel.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Reflection;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Loads trusted .csx workflow entries and executes the named CompositeStep through the engine path.
/// </summary>
public sealed class CsxEntryLoader
{
    private const string DefaultEntryName = "Main";

    /// <summary>
    /// Creates a loader for trusted .csx workflow entry files.
    /// </summary>
    public CsxEntryLoader()
    {
    }

    /// <summary>
    /// Loads the requested entry from a trusted .csx file and executes it as a workflow.
    /// </summary>
    /// <param name="entryPath">The .csx file path to load.</param>
    /// <param name="entryName">The named script variable to use as the workflow entry, or null for Main.</param>
    /// <param name="options">The workflow execution options passed to the loaded CompositeStep.</param>
    /// <returns>The workflow result produced by loading, resolving, and executing the requested entry.</returns>
    public WorkflowResult Execute(
        string entryPath,
        string? entryName = null,
        WorkflowExecutionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);

        string resolvedEntryName = string.IsNullOrWhiteSpace(entryName) ? DefaultEntryName : entryName;

        if (!File.Exists(entryPath))
        {
            return Failure(resolvedEntryName, WorkflowErrorCodes.ScriptLoadFailed, $"Entry script was not found: {entryPath}");
        }

        try
        {
            string code = File.ReadAllText(entryPath);
            ScriptOptions scriptOptions = CreateScriptOptions(entryPath, code);
            Microsoft.CodeAnalysis.Scripting.Script<object> script = CSharpScript.Create<object>(
                code,
                scriptOptions,
                typeof(object));

            ImmutableArray<Diagnostic> errors = script.Compile()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();

            if (!errors.IsEmpty)
            {
                return Failure(
                    resolvedEntryName,
                    WorkflowErrorCodes.ScriptCompileFailed,
                    string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
            }

            ScriptState<object> state = script.RunAsync(new object()).GetAwaiter().GetResult();
            object? entry = state.Variables
                .Where(variable => variable.Name == resolvedEntryName)
                .Select(variable => variable.Value)
                .FirstOrDefault(IsCompositeStep);

            if (entry is null)
            {
                return Failure(
                    resolvedEntryName,
                    WorkflowErrorCodes.EntryStepNotFound,
                    $"Entry step was not found: {resolvedEntryName}");
            }

            return ExecuteEntry(entry, options);
        }
        catch (Exception exception) when (exception is not ArgumentException)
        {
            return Failure(resolvedEntryName, WorkflowErrorCodes.ScriptLoadFailed, exception.Message);
        }
    }

    private static ScriptOptions CreateScriptOptions(string entryPath, string code)
    {
        LogFactory logFactory = _ => (_, _, _) => { };
        string workingDirectory = Path.GetDirectoryName(Path.GetFullPath(entryPath)) ?? Directory.GetCurrentDirectory();
        var compiler = new ScriptCompiler(logFactory, workingDirectory, true);
        var context = new ScriptContext(
            SourceText.From(code),
            workingDirectory,
            [],
            entryPath,
            OptimizationLevel.Debug,
            ScriptMode.Script,
            []);

        return compiler
            .CreateScriptOptions(context, Array.Empty<RuntimeDependency>())
            .AddReferences(typeof(IStep<>).Assembly, typeof(CompositeStep).Assembly)
            .AddImports("Devo6.WorkFlow.Abstractions", "Devo6.WorkFlow.Engine");
    }

    private static WorkflowResult ExecuteEntry(object entry, WorkflowExecutionOptions? options)
    {
        MethodInfo? method = entry.GetType().GetMethod(
            nameof(CompositeStep<Unit>.ExecuteWorkflow),
            BindingFlags.Instance | BindingFlags.Public,
            [typeof(WorkflowExecutionOptions)]);

        if (method?.Invoke(entry, [options]) is WorkflowResult result)
        {
            return result;
        }

        return Failure("", WorkflowErrorCodes.EntryStepNotFound, "Entry step could not be executed.");
    }

    private static bool IsCompositeStep(object? value)
    {
        Type? type = value?.GetType();

        return type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(CompositeStep<>);
    }

    private static WorkflowResult Failure(string entryName, string errorCode, string errorMessage)
    {
        return new WorkflowResult
        {
            EntryName = entryName,
            Succeeded = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Trace = new ExecutionTrace([]),
        };
    }
}
