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
using System.Text;
using System.Text.RegularExpressions;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Loads trusted .csx workflow entries and executes the named CompositeStep through the engine path.
/// </summary>
public sealed class CsxEntryLoader
{
    private const string DefaultEntryName = "Main";
    private readonly CsxEntryLoaderOptions loaderOptions;

    /// <summary>
    /// Creates a loader for trusted .csx workflow entry files.
    /// </summary>
    /// <param name="options">The reference validation options used while loading .csx files.</param>
    public CsxEntryLoader(CsxEntryLoaderOptions? options = null)
    {
        loaderOptions = options ?? new CsxEntryLoaderOptions();
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
            CsxScriptSource source = LoadScriptSource(entryPath);
            ScriptOptions scriptOptions = CreateScriptOptions(entryPath, source);
            Microsoft.CodeAnalysis.Scripting.Script<object> script = CSharpScript.Create<object>(
                source.Code,
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
        catch (CsxReferenceValidationException exception)
        {
            return Failure(resolvedEntryName, exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is not ArgumentException)
        {
            return Failure(resolvedEntryName, WorkflowErrorCodes.ScriptLoadFailed, exception.Message);
        }
    }

    /// <summary>
    /// Validates a trusted .csx workflow entry before executing any workflow steps.
    /// </summary>
    /// <param name="entryPath">The .csx file path to validate.</param>
    /// <param name="entryName">The named script variable to validate as the workflow entry, or null for Main.</param>
    /// <param name="validationOptions">Additional validation inputs such as config file paths.</param>
    /// <returns>The validation result containing all detected pre-execution errors.</returns>
    public WorkflowValidationResult Validate(
        string entryPath,
        string? entryName = null,
        CsxValidationOptions? validationOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);

        string resolvedEntryName = string.IsNullOrWhiteSpace(entryName) ? DefaultEntryName : entryName;
        var errors = new List<ValidationError>();

        if (!File.Exists(entryPath))
        {
            errors.Add(ToValidationError(entryPath, WorkflowErrorCodes.EntryScriptNotFound, $"Entry script was not found: {entryPath}"));

            return new WorkflowValidationResult { Errors = errors };
        }

        errors.AddRange(ValidateConfigPaths(entryPath, validationOptions));

        try
        {
            CsxScriptSource source = LoadScriptSource(entryPath);
            ScriptOptions scriptOptions = CreateScriptOptions(entryPath, source);
            Microsoft.CodeAnalysis.Scripting.Script<object> script = CSharpScript.Create<object>(
                source.Code,
                scriptOptions,
                typeof(object));
            ImmutableArray<Diagnostic> compileErrors = script.Compile()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();

            if (!compileErrors.IsEmpty)
            {
                errors.Add(ToValidationError(
                    entryPath,
                    WorkflowErrorCodes.ScriptCompileFailed,
                    string.Join(Environment.NewLine, compileErrors.Select(diagnostic => diagnostic.ToString()))));

                return new WorkflowValidationResult { Errors = errors };
            }

            ScriptState<object> state = script.RunAsync(new object()).GetAwaiter().GetResult();
            IReadOnlyList<ScriptVariable> entryVariables = state.Variables
                .Where(variable => IsCompositeStep(variable.Value))
                .ToArray();
            List<IGrouping<string, ScriptVariable>> duplicateStepNames = entryVariables
                .GroupBy(variable => GetCompositeStepName(variable.Value), StringComparer.Ordinal)
                .Where(group => group.Key.Length > 0 && group.Count() > 1)
                .ToList();

            foreach (IGrouping<string, ScriptVariable> duplicate in duplicateStepNames)
            {
                errors.Add(ToValidationError(
                    duplicate.Key,
                    WorkflowErrorCodes.DuplicateStepName,
                    $"Duplicate public step name was found: {duplicate.Key}"));
            }

            if (duplicateStepNames.Count == 0 && !entryVariables.Any(variable => variable.Name == resolvedEntryName))
            {
                errors.Add(ToValidationError(
                    resolvedEntryName,
                    WorkflowErrorCodes.EntryStepNotFound,
                    $"Entry step was not found: {resolvedEntryName}"));
            }
        }
        catch (CsxReferenceValidationException exception)
        {
            errors.Add(ToValidationError(entryPath, exception.ErrorCode, exception.Message));
        }
        catch (Exception exception)
        {
            errors.Add(ToValidationError(entryPath, WorkflowErrorCodes.ScriptLoadFailed, exception.Message));
        }

        return new WorkflowValidationResult { Errors = errors };
    }

    private CsxScriptSource LoadScriptSource(string entryPath)
    {
        string entryFullPath = Path.GetFullPath(entryPath);
        string workflowRoot = ResolvePathFinalTarget(
            loaderOptions.WorkflowRoot
            ?? Path.GetDirectoryName(entryFullPath)
            ?? Directory.GetCurrentDirectory());
        var context = new CsxLoadContext(workflowRoot);

        string code = MoveUsingDirectivesToTop(LoadScriptFile(entryFullPath, context));

        return new CsxScriptSource(code, context.ReferenceAssemblies, context.ReferencePaths, context.HasNuGetReferences);
    }

    private string LoadScriptFile(string scriptPath, CsxLoadContext context)
    {
        string fullPath = ResolvePathFinalTarget(scriptPath);

        if (!IsInsideRoot(fullPath, context.WorkflowRoot))
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptReferenceNotAllowed,
                $"Script load is outside the workflow root: {fullPath}");
        }

        if (context.LoadStack.Contains(fullPath))
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptLoadCycleDetected,
                $"Script load cycle was detected: {fullPath}");
        }

        if (context.LoadedFiles.Contains(fullPath))
        {
            return "";
        }

        context.LoadStack.Add(fullPath);

        try
        {
            string directory = Path.GetDirectoryName(fullPath) ?? context.WorkflowRoot;
            var source = new StringBuilder();

            foreach (string line in File.ReadLines(fullPath))
            {
                if (TryReadDirective(line, "load", out string loadValue))
                {
                    if (loadValue.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CsxReferenceValidationException(
                            WorkflowErrorCodes.ScriptReferenceNotAllowed,
                            "#load with nuget references is not supported.");
                    }

                    string loadedPath = Path.GetFullPath(Path.Combine(directory, loadValue));
                    source.AppendLine(LoadScriptFile(loadedPath, context));
                    continue;
                }

                if (TryReadDirective(line, "r", out string referenceValue))
                {
                    bool keepReferenceDirective = ValidateReference(referenceValue, directory, context);
                    if (keepReferenceDirective)
                    {
                        source.AppendLine(line);
                    }

                    continue;
                }

                source.AppendLine(line);
            }

            context.LoadedFiles.Add(fullPath);

            return source.ToString();
        }
        finally
        {
            context.LoadStack.Remove(fullPath);
        }
    }

    private bool ValidateReference(string referenceValue, string scriptDirectory, CsxLoadContext context)
    {
        if (referenceValue.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase))
        {
            ValidateNuGetReference(referenceValue);
            context.HasNuGetReferences = true;

            return true;
        }

        if (LooksLikeFileReference(referenceValue))
        {
            string referencePath = ResolvePathFinalTarget(Path.Combine(scriptDirectory, referenceValue));

            if (!loaderOptions.AllowedReferenceDirectories
                .Select(ResolvePathFinalTarget)
                .Any(directory => IsInsideRoot(referencePath, directory)))
            {
                throw new CsxReferenceValidationException(
                    WorkflowErrorCodes.ScriptReferenceNotAllowed,
                    $"File reference is not allowed: {referenceValue}");
            }

            ValidateApiAssemblyIdentity(referencePath);

            context.ReferencePaths.Add(referencePath);
            return false;
        }

        if (!loaderOptions.AllowedAssemblyReferences.Contains(referenceValue, StringComparer.Ordinal))
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptReferenceNotAllowed,
                $"Assembly reference is not allowed: {referenceValue}");
        }

        context.ReferenceAssemblies.Add(Assembly.Load(new AssemblyName(referenceValue)));

        return false;
    }

    private void ValidateNuGetReference(string referenceValue)
    {
        string value = referenceValue["nuget:".Length..].Trim();
        string[] parts = value.Split(',', 2, StringSplitOptions.TrimEntries);

        if (parts.Length != 2 || IsFloatingNuGetVersion(parts[1]))
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptReferenceNotAllowed,
                $"NuGet reference version is not allowed: {referenceValue}");
        }

        bool allowed = loaderOptions.AllowedNuGetReferences.Any(
            reference => string.Equals(reference.PackageId, parts[0], StringComparison.OrdinalIgnoreCase)
                && string.Equals(reference.Version, parts[1], StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptReferenceNotAllowed,
                $"NuGet reference is not allowed: {referenceValue}");
        }
    }

    private static ScriptOptions CreateScriptOptions(string entryPath, CsxScriptSource source)
    {
        LogFactory logFactory = _ => (_, _, _) => { };
        string workingDirectory = Path.GetDirectoryName(Path.GetFullPath(entryPath)) ?? Directory.GetCurrentDirectory();
        var compiler = new ScriptCompiler(logFactory, workingDirectory, true);
        var context = new ScriptContext(
            SourceText.From(source.Code),
            workingDirectory,
            [],
            entryPath,
            OptimizationLevel.Debug,
            ScriptMode.Script,
            []);
        ScriptOptions scriptOptions = source.HasNuGetReferences
            ? compiler.CreateCompilationContext<object, object>(context).ScriptOptions
            : compiler.CreateScriptOptions(context, Array.Empty<RuntimeDependency>());

        return scriptOptions
            .AddReferences(typeof(IStep<>).Assembly, typeof(CompositeStep).Assembly)
            .AddReferences(source.ReferenceAssemblies)
            .AddReferences(source.ReferencePaths)
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

    private static string GetCompositeStepName(object? value)
    {
        return value?.GetType().GetProperty(nameof(CompositeStep<Unit>.Name))?.GetValue(value) as string ?? "";
    }

    private static IEnumerable<ValidationError> ValidateConfigPaths(string entryPath, CsxValidationOptions? validationOptions)
    {
        if (validationOptions is null)
        {
            yield break;
        }

        string entryDirectory = Path.GetDirectoryName(Path.GetFullPath(entryPath)) ?? Directory.GetCurrentDirectory();

        foreach (string configPath in validationOptions.ConfigPaths)
        {
            string resolvedPath = Path.IsPathRooted(configPath)
                ? configPath
                : Path.Combine(entryDirectory, configPath);

            if (!File.Exists(resolvedPath))
            {
                yield return ToValidationError(
                    configPath,
                    WorkflowErrorCodes.ConfigNotFound,
                    $"Config file was not found: {configPath}");
            }
        }
    }

    private static void ValidateApiAssemblyIdentity(string referencePath)
    {
        AssemblyName referenceAssemblyName;

        try
        {
            referenceAssemblyName = AssemblyName.GetAssemblyName(referencePath);
        }
        catch (BadImageFormatException)
        {
            return;
        }

        RejectCopiedApiAssembly(referencePath, referenceAssemblyName, typeof(IStep<>).Assembly);
        RejectCopiedApiAssembly(referencePath, referenceAssemblyName, typeof(CompositeStep).Assembly);
    }

    private static void RejectCopiedApiAssembly(string referencePath, AssemblyName referenceAssemblyName, Assembly hostAssembly)
    {
        AssemblyName hostAssemblyName = hostAssembly.GetName();

        if (!string.Equals(referenceAssemblyName.Name, hostAssemblyName.Name, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(
            ResolvePathFinalTarget(referencePath),
            ResolvePathFinalTarget(hostAssembly.Location),
            StringComparison.Ordinal))
        {
            return;
        }

        throw new CsxReferenceValidationException(
            WorkflowErrorCodes.ScriptApiIdentityMismatch,
            $"Script references a different copy of public API assembly: {referencePath}");
    }

    private static bool TryReadDirective(string line, string directiveName, out string value)
    {
        Match match = Regex.Match(
            line,
            $@"^\s*#{directiveName}\s+""([^""]+)""\s*$",
            RegexOptions.CultureInvariant);

        value = match.Success ? match.Groups[1].Value : "";

        return match.Success;
    }

    private static string MoveUsingDirectivesToTop(string code)
    {
        var referenceDirectives = new List<string>();
        var usings = new List<string>();
        var body = new List<string>();

        foreach (string line in code.Split(Environment.NewLine))
        {
            if (Regex.IsMatch(line, @"^\s*#r\s+""[^""]+""\s*$", RegexOptions.CultureInvariant))
            {
                if (!referenceDirectives.Contains(line, StringComparer.Ordinal))
                {
                    referenceDirectives.Add(line);
                }

                continue;
            }

            if (Regex.IsMatch(line, @"^\s*using\s+[^;]+;\s*$", RegexOptions.CultureInvariant))
            {
                if (!usings.Contains(line, StringComparer.Ordinal))
                {
                    usings.Add(line);
                }

                continue;
            }

            body.Add(line);
        }

        return string.Join(Environment.NewLine, referenceDirectives.Concat(usings).Concat(body));
    }

    private static bool LooksLikeFileReference(string referenceValue)
    {
        return referenceValue.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || referenceValue.StartsWith("./", StringComparison.Ordinal)
            || referenceValue.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(referenceValue);
    }

    private static bool IsFloatingNuGetVersion(string version)
    {
        return string.IsNullOrWhiteSpace(version)
            || version.Contains('*', StringComparison.Ordinal)
            || version.Contains('[', StringComparison.Ordinal)
            || version.Contains(']', StringComparison.Ordinal)
            || version.Contains('(', StringComparison.Ordinal)
            || version.Contains(')', StringComparison.Ordinal);
    }

    private static bool IsInsideRoot(string path, string root)
    {
        string fullPath = ResolvePathFinalTarget(path);
        string fullRoot = ResolvePathFinalTarget(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(fullPath, fullRoot, StringComparison.Ordinal)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ResolvePathFinalTarget(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (string.IsNullOrEmpty(root))
        {
            return fullPath;
        }

        string current = root;
        string relativePath = fullPath[root.Length..];
        string[] parts = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string part in parts)
        {
            string candidate = Path.Combine(current, part);
            FileSystemInfo? info = GetExistingFileSystemInfo(candidate);
            FileSystemInfo? target = info?.LinkTarget is null ? null : info.ResolveLinkTarget(returnFinalTarget: true);

            current = target?.FullName ?? candidate;
        }

        return Path.GetFullPath(current);
    }

    private static FileSystemInfo? GetExistingFileSystemInfo(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        if (File.Exists(path))
        {
            return new FileInfo(path);
        }

        return null;
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

    private static ValidationError ToValidationError(string path, string code, string message)
    {
        return new ValidationError
        {
            Path = path,
            Code = code,
            Message = message,
        };
    }

    private sealed class CsxLoadContext(string workflowRoot)
    {
        public string WorkflowRoot { get; } = workflowRoot;

        public HashSet<string> LoadedFiles { get; } = new(StringComparer.Ordinal);

        public HashSet<string> LoadStack { get; } = new(StringComparer.Ordinal);

        public List<Assembly> ReferenceAssemblies { get; } = new();

        public List<string> ReferencePaths { get; } = new();

        public bool HasNuGetReferences { get; set; }
    }

    private sealed record CsxScriptSource(
        string Code,
        IReadOnlyList<Assembly> ReferenceAssemblies,
        IReadOnlyList<string> ReferencePaths,
        bool HasNuGetReferences);

    private sealed class CsxReferenceValidationException(string errorCode, string message) : Exception(message)
    {
        public string ErrorCode { get; } = errorCode;
    }
}

/// <summary>
/// Configures workflow root and explicit reference allow lists for trusted .csx entry loading.
/// </summary>
public sealed class CsxEntryLoaderOptions
{
    /// <summary>
    /// Gets the workflow root that bounds local #load paths, or null to use the entry .csx directory.
    /// </summary>
    public string? WorkflowRoot { get; init; }

    /// <summary>
    /// Gets assembly names that may be referenced by #r directives.
    /// </summary>
    public IReadOnlyList<string> AllowedAssemblyReferences { get; init; } = [];

    /// <summary>
    /// Gets directories whose files may be referenced by #r file directives.
    /// </summary>
    public IReadOnlyList<string> AllowedReferenceDirectories { get; init; } = [];

    /// <summary>
    /// Gets exact NuGet package id and version pairs allowed by #r nuget directives.
    /// </summary>
    public IReadOnlyList<CsxNuGetReference> AllowedNuGetReferences { get; init; } = [];
}

/// <summary>
/// Represents one exact NuGet package id and version that a .csx file may reference.
/// </summary>
/// <param name="PackageId">The allowed NuGet package id.</param>
/// <param name="Version">The allowed exact NuGet package version.</param>
public sealed record CsxNuGetReference(string PackageId, string Version);
