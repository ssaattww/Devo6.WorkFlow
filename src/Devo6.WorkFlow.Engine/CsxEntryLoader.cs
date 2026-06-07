using Devo6.WorkFlow.Abstractions;
using Dotnet.Script.Core;
using Dotnet.Script.DependencyModel.Context;
using Dotnet.Script.DependencyModel.Logging;
using Dotnet.Script.DependencyModel.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using NuGet.Configuration;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Loads trusted .csx workflow entries and executes the named CompositeStep through the engine path.
/// </summary>
public sealed class CsxEntryLoader
{
    private const string DefaultEntryName = "Main";
    private const string DefaultNuGetLockFileName = "devo6.nuget.lock.yaml";
    private static readonly ICsxNuGetDependencyGraphProvider DefaultNuGetDependencyGraphProvider = new DotnetScriptCsxNuGetDependencyGraphProvider();
    private static readonly IDeserializer NuGetLockDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
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

            WorkflowExecutionOptions? preparedOptions = PrepareExecutionOptions(resolvedEntryName, entry, options, out WorkflowResult? failure);
            if (failure is not null)
            {
                return failure;
            }

            return ExecuteEntry(entry, preparedOptions);
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

    /// <summary>
    /// 指定された entry script と参照先 file を読み込み、NuGet lock 検査済みの script source を作成します。
    /// </summary>
    /// <param name="entryPath">読み込む entry script の path。</param>
    /// <returns>compile に使う script source。</returns>
    private CsxScriptSource LoadScriptSource(string entryPath)
    {
        string entryFullPath = Path.GetFullPath(entryPath);
        string workflowRoot = ResolvePathFinalTarget(
            loaderOptions.WorkflowRoot
            ?? Path.GetDirectoryName(entryFullPath)
            ?? Directory.GetCurrentDirectory());
        var context = new CsxLoadContext(workflowRoot);

        string code = MoveUsingDirectivesToTop(LoadScriptFile(entryFullPath, context));
        CsxNuGetDependencyGraph nuGetGraph = VerifyNuGetLock(entryFullPath, code, context);
        string compilationCode = context.HasNuGetReferences ? RemoveNuGetReferenceDirectives(code) : code;

        return new CsxScriptSource(
            compilationCode,
            context.ReferenceAssemblies,
            context.ReferencePaths.Concat(nuGetGraph.ReferencePaths).ToArray());
    }

    /// <summary>
    /// script file を読み込み、許可された参照だけを source へ展開します。
    /// </summary>
    /// <param name="scriptPath">読み込む script file の path。</param>
    /// <param name="context">読み込み中の文脈。</param>
    /// <returns>読み込んだ script source。</returns>
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

    /// <summary>
    /// #r directive の参照先を検査し、compile source に directive を残すかどうかを返します。
    /// </summary>
    /// <param name="referenceValue">#r directive に書かれた参照値。</param>
    /// <param name="scriptDirectory">directive を含む script file の directory。</param>
    /// <param name="context">読み込み中の文脈。</param>
    /// <returns>directive を compile source に残す場合は true。</returns>
    private bool ValidateReference(string referenceValue, string scriptDirectory, CsxLoadContext context)
    {
        if (referenceValue.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase))
        {
            CsxNuGetReference reference = ValidateNuGetReference(referenceValue);
            context.HasNuGetReferences = true;
            context.NuGetReferences.Add(reference);

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

    /// <summary>
    /// NuGet 参照が固定 version かつ許可済みであることを確認します。
    /// </summary>
    /// <param name="referenceValue">#r directive に書かれた NuGet 参照値。</param>
    /// <returns>検査済みの NuGet 直接参照。</returns>
    private CsxNuGetReference ValidateNuGetReference(string referenceValue)
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

        return new CsxNuGetReference(parts[0], parts[1]);
    }

    /// <summary>
    /// NuGet lock file の欠落、不一致、解決済み依存関係の差分を検査します。
    /// </summary>
    /// <param name="entryPath">entry script の full path。</param>
    /// <param name="sourceCode">NuGet directive を含む source code。</param>
    /// <param name="context">読み込み中の文脈。</param>
    /// <returns>lock と一致した解決済み dependency graph。</returns>
    private CsxNuGetDependencyGraph VerifyNuGetLock(string entryPath, string sourceCode, CsxLoadContext context)
    {
        if (!context.HasNuGetReferences)
        {
            return new CsxNuGetDependencyGraph([]);
        }

        string lockPath = ResolveNuGetLockPath(context.WorkflowRoot);
        if (!File.Exists(lockPath))
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptNugetLockMissing,
                $"NuGet lock file was not found: {lockPath}");
        }

        CsxNuGetLockFile lockFile = ReadNuGetLockFile(lockPath);
        EnsureDirectReferencesMatch(context.NuGetReferences, lockFile.DirectReferences);
        EnsureLockMetadataIsComplete(lockFile);

        CsxNuGetDependencyGraph graph;
        try
        {
            graph = (loaderOptions.NuGetDependencyGraphProvider ?? DefaultNuGetDependencyGraphProvider).Resolve(
                context.NuGetReferences,
                new CsxNuGetDependencyGraphRequest(entryPath, context.WorkflowRoot, lockPath, sourceCode));
        }
        catch (CsxReferenceValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptNugetRestoreFailed,
                $"NuGet dependencies could not be restored: {exception.Message}");
        }

        EnsureResolutionMetadataMatches(graph.ResolutionMetadata, lockFile);
        EnsureResolvedDependenciesMatch(graph.Dependencies, lockFile.ResolvedDependencies);

        return graph;
    }

    /// <summary>
    /// option または workflow root から NuGet lock file path を解決します。
    /// </summary>
    /// <param name="workflowRoot">workflow root の path。</param>
    /// <returns>解決済み NuGet lock file path。</returns>
    private string ResolveNuGetLockPath(string workflowRoot)
    {
        string lockPath = loaderOptions.NuGetLockFilePath ?? DefaultNuGetLockFileName;

        return Path.GetFullPath(Path.IsPathRooted(lockPath) ? lockPath : Path.Combine(workflowRoot, lockPath));
    }

    /// <summary>
    /// NuGet lock file を YAML から読み込みます。
    /// </summary>
    /// <param name="lockPath">NuGet lock file の path。</param>
    /// <returns>読み込んだ NuGet lock file。</returns>
    private static CsxNuGetLockFile ReadNuGetLockFile(string lockPath)
    {
        try
        {
            return NuGetLockDeserializer.Deserialize<CsxNuGetLockFile>(File.ReadAllText(lockPath))
                ?? new CsxNuGetLockFile();
        }
        catch (Exception exception)
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptNugetLockMismatch,
                $"NuGet lock file could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// script の直接 NuGet 参照と lock file の直接参照が一致することを確認します。
    /// </summary>
    /// <param name="actualReferences">script から読んだ直接参照。</param>
    /// <param name="lockedReferences">lock file に記録された直接参照。</param>
    private static void EnsureDirectReferencesMatch(
        IReadOnlyList<CsxNuGetReference> actualReferences,
        IReadOnlyList<CsxNuGetReference> lockedReferences)
    {
        if (!ReferencesEqual(actualReferences, lockedReferences))
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptNugetLockMismatch,
                "NuGet direct references do not match the lock file.");
        }
    }

    /// <summary>
    /// 解決済み NuGet 依存関係と lock file の解決済み依存関係が一致することを確認します。
    /// </summary>
    /// <param name="actualDependencies">provider が返した解決済み依存関係。</param>
    /// <param name="lockedDependencies">lock file に記録された解決済み依存関係。</param>
    private static void EnsureResolvedDependenciesMatch(
        IReadOnlyList<CsxResolvedNuGetDependency> actualDependencies,
        IReadOnlyList<CsxResolvedNuGetDependency> lockedDependencies)
    {
        if (!ResolvedDependenciesEqual(actualDependencies, lockedDependencies))
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptNugetLockMismatch,
                "Resolved NuGet dependencies do not match the lock file.");
        }
    }

    /// <summary>
    /// lock file に再現性 metadata がすべて記録されていることを確認します。
    /// </summary>
    /// <param name="lockFile">読み込んだ NuGet lock file。</param>
    private static void EnsureLockMetadataIsComplete(CsxNuGetLockFile lockFile)
    {
        if (string.IsNullOrWhiteSpace(lockFile.TargetFramework)
            || string.IsNullOrWhiteSpace(lockFile.RuntimeIdentifier)
            || string.IsNullOrWhiteSpace(lockFile.DotnetScriptCoreVersion)
            || lockFile.PackageSources is null
            || lockFile.PackageSources.Count == 0
            || lockFile.PackageSources.Any(string.IsNullOrWhiteSpace))
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptNugetLockMismatch,
                "NuGet lock file is missing reproducibility metadata.");
        }
    }

    /// <summary>
    /// 解決時 metadata と lock file の再現性 metadata が一致することを確認します。
    /// </summary>
    /// <param name="actualMetadata">provider が返した NuGet 解決 metadata。</param>
    /// <param name="lockFile">読み込んだ NuGet lock file。</param>
    private static void EnsureResolutionMetadataMatches(CsxNuGetResolutionMetadata actualMetadata, CsxNuGetLockFile lockFile)
    {
        if (!string.Equals(actualMetadata.TargetFramework, lockFile.TargetFramework, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(actualMetadata.RuntimeIdentifier, lockFile.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(actualMetadata.DotnetScriptCoreVersion, lockFile.DotnetScriptCoreVersion, StringComparison.OrdinalIgnoreCase)
            || !PackageSourcesEqual(actualMetadata.PackageSources, lockFile.PackageSources))
        {
            throw new CsxReferenceValidationException(
                WorkflowErrorCodes.ScriptNugetLockMismatch,
                "NuGet resolution metadata does not match the lock file.");
        }
    }

    /// <summary>
    /// NuGet 直接参照の集合が package id と version で一致するかどうかを返します。
    /// </summary>
    /// <param name="left">比較する左辺の参照集合。</param>
    /// <param name="right">比較する右辺の参照集合。</param>
    /// <returns>集合が一致する場合は true。</returns>
    private static bool ReferencesEqual(IReadOnlyList<CsxNuGetReference> left, IReadOnlyList<CsxNuGetReference> right)
    {
        return left.Count == right.Count
            && left
                .OrderBy(reference => reference.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reference => reference.Version, StringComparer.OrdinalIgnoreCase)
                .Zip(
                    right
                        .OrderBy(reference => reference.PackageId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(reference => reference.Version, StringComparer.OrdinalIgnoreCase))
                .All(pair => string.Equals(pair.First.PackageId, pair.Second.PackageId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pair.First.Version, pair.Second.Version, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// NuGet 解決済み依存関係の集合が package id、version、直接参照 flag で一致するかどうかを返します。
    /// </summary>
    /// <param name="left">比較する左辺の依存関係集合。</param>
    /// <param name="right">比較する右辺の依存関係集合。</param>
    /// <returns>集合が一致する場合は true。</returns>
    private static bool ResolvedDependenciesEqual(
        IReadOnlyList<CsxResolvedNuGetDependency> left,
        IReadOnlyList<CsxResolvedNuGetDependency> right)
    {
        return left.Count == right.Count
            && left
                .OrderBy(dependency => dependency.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(dependency => dependency.Version, StringComparer.OrdinalIgnoreCase)
                .ThenBy(dependency => dependency.IsDirect)
                .Zip(
                    right
                        .OrderBy(dependency => dependency.PackageId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(dependency => dependency.Version, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(dependency => dependency.IsDirect))
                .All(pair => string.Equals(pair.First.PackageId, pair.Second.PackageId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pair.First.Version, pair.Second.Version, StringComparison.OrdinalIgnoreCase)
                    && pair.First.IsDirect == pair.Second.IsDirect);
    }

    /// <summary>
    /// package source 集合が順序に依存せず一致するかどうかを返します。
    /// </summary>
    /// <param name="left">比較する左辺の package source 集合。</param>
    /// <param name="right">比較する右辺の package source 集合。</param>
    /// <returns>集合が一致する場合は true。</returns>
    private static bool PackageSourcesEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count
            && left
                .Order(StringComparer.Ordinal)
                .Zip(right.Order(StringComparer.Ordinal))
                .All(pair => string.Equals(pair.First, pair.Second, StringComparison.Ordinal));
    }

    /// <summary>
    /// Roslyn script 実行に使う参照と import を組み立てます。
    /// </summary>
    /// <param name="entryPath">entry script の path。</param>
    /// <param name="source">読み込み済み script source。</param>
    /// <returns>script compile と実行に使う option。</returns>
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
        ScriptOptions scriptOptions = compiler.CreateScriptOptions(context, Array.Empty<RuntimeDependency>());

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

    /// <summary>
    /// Entry metadata に基づいて標準 Config を読み込み、実行 option を準備します。
    /// </summary>
    private static WorkflowExecutionOptions? PrepareExecutionOptions(
        string entryName,
        object entry,
        WorkflowExecutionOptions? options,
        out WorkflowResult? failure)
    {
        failure = null;

        Type? configType = GetCompositeStepConfigType(entry);
        if (configType is null)
        {
            return options;
        }

        string configPath = options?.EngineArguments?.ConfigPath ?? "";
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            failure = Failure(
                entryName,
                WorkflowErrorCodes.ConfigNotFound,
                string.IsNullOrWhiteSpace(configPath)
                    ? "Config file was not specified."
                    : $"Config file was not found: {configPath}");

            return null;
        }

        try
        {
            object standardConfig = StandardConfigLoader.Load(configPath, configType, options?.EngineArguments?.Settings);

            return (options ?? new WorkflowExecutionOptions()).WithStandardConfig(standardConfig);
        }
        catch (Exception exception) when (exception is not ArgumentException)
        {
            failure = Failure(
                entryName,
                WorkflowErrorCodes.ConfigLoadFailed,
                $"Config file could not be loaded: {configPath}. {exception.Message}");

            return null;
        }
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

    /// <summary>
    /// CompositeStep の標準 Config 型 metadata を取得します。
    /// </summary>
    private static Type? GetCompositeStepConfigType(object value)
    {
        return value.GetType().GetProperty(nameof(CompositeStep<Unit>.ConfigType))?.GetValue(value) as Type;
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

    /// <summary>
    /// lock 検査後の compile source から NuGet #r directive を取り除きます。
    /// </summary>
    /// <param name="code">変換前の script source。</param>
    /// <returns>NuGet #r directive を取り除いた script source。</returns>
    private static string RemoveNuGetReferenceDirectives(string code)
    {
        return string.Join(
            Environment.NewLine,
            code.Split(Environment.NewLine)
                .Where(line => !TryReadDirective(line, "r", out string referenceValue)
                    || !referenceValue.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase)));
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

    /// <summary>
    /// 現在の Dotnet.Script 解決環境に対応する metadata を作成します。
    /// </summary>
    /// <param name="workingDirectory">NuGet 設定を解決する基準 directory。</param>
    /// <returns>NuGet 解決 metadata。</returns>
    private static CsxNuGetResolutionMetadata CreateResolutionMetadata(string workingDirectory)
    {
        return new CsxNuGetResolutionMetadata(
            GetCurrentTargetFramework(),
            RuntimeInformation.RuntimeIdentifier,
            GetEnabledPackageSources(workingDirectory),
            GetDotnetScriptCoreVersion());
    }

    /// <summary>
    /// 現在実行中の engine assembly の target framework moniker を取得します。
    /// </summary>
    /// <returns>target framework moniker。</returns>
    private static string GetCurrentTargetFramework()
    {
        string? frameworkName = typeof(CsxEntryLoader).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()
            ?.FrameworkName;

        if (string.IsNullOrWhiteSpace(frameworkName))
        {
            return "";
        }

        var framework = new FrameworkName(frameworkName);

        return framework.Identifier switch
        {
            ".NETCoreApp" => $"net{framework.Version.Major}.{framework.Version.Minor}",
            ".NETFramework" => $"net{framework.Version.Major}{framework.Version.Minor}",
            _ => frameworkName,
        };
    }

    /// <summary>
    /// Dotnet.Script.Core assembly の informational version を取得します。
    /// </summary>
    /// <returns>Dotnet.Script.Core version。</returns>
    private static string GetDotnetScriptCoreVersion()
    {
        Assembly assembly = typeof(ScriptCompiler).Assembly;
        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "";
        int metadataIndex = version.IndexOf('+', StringComparison.Ordinal);

        return metadataIndex < 0 ? version : version[..metadataIndex];
    }

    /// <summary>
    /// NuGet configuration から有効な package source を取得します。
    /// </summary>
    /// <param name="workingDirectory">NuGet 設定を解決する基準 directory。</param>
    /// <returns>有効な package source 一覧。</returns>
    private static IReadOnlyList<string> GetEnabledPackageSources(string workingDirectory)
    {
        ISettings settings = NuGet.Configuration.Settings.LoadDefaultSettings(workingDirectory);
        var provider = new PackageSourceProvider(settings);

        return provider.LoadPackageSources()
            .Where(source => source.IsEnabled)
            .Select(source => source.Source)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
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

    /// <summary>
    /// .csx file 読み込み中の状態を保持します。
    /// </summary>
    /// <param name="workflowRoot">workflow root の path。</param>
    private sealed class CsxLoadContext(string workflowRoot)
    {
        /// <summary>
        /// workflow root の path を取得します。
        /// </summary>
        public string WorkflowRoot { get; } = workflowRoot;

        /// <summary>
        /// 読み込み済み file の集合を取得します。
        /// </summary>
        public HashSet<string> LoadedFiles { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 現在の load stack を取得します。
        /// </summary>
        public HashSet<string> LoadStack { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// assembly 名から読み込んだ参照 assembly を取得します。
        /// </summary>
        public List<Assembly> ReferenceAssemblies { get; } = new();

        /// <summary>
        /// file path で読み込む参照 assembly path を取得します。
        /// </summary>
        public List<string> ReferencePaths { get; } = new();

        /// <summary>
        /// script に含まれる NuGet 直接参照を取得します。
        /// </summary>
        public List<CsxNuGetReference> NuGetReferences { get; } = new();

        /// <summary>
        /// NuGet 参照が見つかったかどうかを取得または設定します。
        /// </summary>
        public bool HasNuGetReferences { get; set; }
    }

    /// <summary>
    /// compile に渡す script source と解決済み参照を保持します。
    /// </summary>
    private sealed class CsxScriptSource
    {
        /// <summary>
        /// compile に渡す script source と解決済み参照を作成します。
        /// </summary>
        /// <param name="code">compile に使う script source。</param>
        /// <param name="referenceAssemblies">assembly 名から読み込んだ参照 assembly。</param>
        /// <param name="referencePaths">file path で追加する参照 assembly path。</param>
        public CsxScriptSource(
            string code,
            IReadOnlyList<Assembly> referenceAssemblies,
            IReadOnlyList<string> referencePaths)
        {
            Code = code;
            ReferenceAssemblies = referenceAssemblies;
            ReferencePaths = referencePaths;
        }

        /// <summary>
        /// compile に使う script source を取得します。
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// assembly 名から読み込んだ参照 assembly を取得します。
        /// </summary>
        public IReadOnlyList<Assembly> ReferenceAssemblies { get; }

        /// <summary>
        /// file path で追加する参照 assembly path を取得します。
        /// </summary>
        public IReadOnlyList<string> ReferencePaths { get; }
    }

    /// <summary>
    /// script 参照検査で検出した失敗を stable error code とともに表します。
    /// </summary>
    /// <param name="errorCode">workflow error code。</param>
    /// <param name="message">利用者向け message。</param>
    private sealed class CsxReferenceValidationException(string errorCode, string message) : Exception(message)
    {
        /// <summary>
        /// workflow error code を取得します。
        /// </summary>
        public string ErrorCode { get; } = errorCode;
    }

    /// <summary>
    /// NuGet lock file の YAML schema を読み取るための DTO です。
    /// </summary>
    private sealed class CsxNuGetLockFile
    {
        /// <summary>
        /// lock file format version を取得または設定します。
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// entry script の表示用 path を取得または設定します。
        /// </summary>
        public string Entry { get; set; } = "";

        /// <summary>
        /// lock file に記録された直接 NuGet 参照を取得または設定します。
        /// </summary>
        public List<CsxNuGetReference> DirectReferences { get; set; } = [];

        /// <summary>
        /// lock file に記録された解決済み NuGet 依存関係を取得または設定します。
        /// </summary>
        public List<CsxResolvedNuGetDependency> ResolvedDependencies { get; set; } = [];

        /// <summary>
        /// lock file に記録された target framework を取得または設定します。
        /// </summary>
        public string TargetFramework { get; set; } = "";

        /// <summary>
        /// lock file に記録された runtime identifier を取得または設定します。
        /// </summary>
        public string RuntimeIdentifier { get; set; } = "";

        /// <summary>
        /// lock file に記録された package source 一覧を取得または設定します。
        /// </summary>
        public List<string> PackageSources { get; set; } = [];

        /// <summary>
        /// lock file に記録された Dotnet.Script.Core version を取得または設定します。
        /// </summary>
        public string DotnetScriptCoreVersion { get; set; } = "";
    }

    /// <summary>
    /// Dotnet.Script の既存依存解決で NuGet dependency graph を作成します。
    /// </summary>
    private sealed class DotnetScriptCsxNuGetDependencyGraphProvider : ICsxNuGetDependencyGraphProvider
    {
        /// <summary>
        /// Dotnet.Script.Core の compilation context から package と runtime assembly path を取得します。
        /// </summary>
        /// <param name="directReferences">script から読んだ直接 NuGet 参照。</param>
        /// <param name="request">dependency graph 解決 request。</param>
        /// <returns>解決済み NuGet dependency graph。</returns>
        public CsxNuGetDependencyGraph Resolve(
            IReadOnlyList<CsxNuGetReference> directReferences,
            CsxNuGetDependencyGraphRequest request)
        {
            LogFactory logFactory = _ => (_, _, _) => { };
            string workingDirectory = Path.GetDirectoryName(Path.GetFullPath(request.EntryPath)) ?? request.WorkflowRoot;
            var compiler = new ScriptCompiler(logFactory, workingDirectory, true);
            var context = new ScriptContext(
                SourceText.From(request.SourceCode),
                workingDirectory,
                [],
                request.EntryPath,
                OptimizationLevel.Debug,
                ScriptMode.Script,
                []);
            ScriptCompilationContext<object> compilationContext = compiler.CreateCompilationContext<object, object>(context);
            HashSet<string> directReferenceKeys = directReferences
                .Select(reference => reference.PackageId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            CsxResolvedNuGetDependency[] dependencies = compilationContext.RuntimeDependencies
                .Select(dependency => new CsxResolvedNuGetDependency(
                    dependency.Name,
                    dependency.Version,
                    directReferenceKeys.Contains(dependency.Name)))
                .OrderBy(dependency => dependency.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(dependency => dependency.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] referencePaths = compilationContext.RuntimeDependencies
                .SelectMany(dependency => dependency.Assemblies)
                .Select(assembly => assembly.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new CsxNuGetDependencyGraph(dependencies, referencePaths, CreateResolutionMetadata(workingDirectory));
        }
    }
}

/// <summary>
/// trusted .csx entry loading に使う workflow root、参照許可一覧、NuGet lock 設定を表します。
/// </summary>
public sealed class CsxEntryLoaderOptions
{
    /// <summary>
    /// local #load path を制限する workflow root を取得または設定します。null の場合は entry .csx directory を使います。
    /// </summary>
    public string? WorkflowRoot { get; init; }

    /// <summary>
    /// #r directive で参照できる assembly name を取得します。
    /// </summary>
    public IReadOnlyList<string> AllowedAssemblyReferences { get; init; } = ["System.ComponentModel.Annotations"];

    /// <summary>
    /// #r file directive で参照できる directory を取得します。
    /// </summary>
    public IReadOnlyList<string> AllowedReferenceDirectories { get; init; } = [];

    /// <summary>
    /// #r nuget directive で許可する固定 NuGet package id と version の組を取得します。
    /// </summary>
    public IReadOnlyList<CsxNuGetReference> AllowedNuGetReferences { get; init; } = [];

    /// <summary>
    /// NuGet lock file の path を取得または設定します。null の場合は workflow root の既定 file を使います。
    /// </summary>
    public string? NuGetLockFilePath { get; init; }

    /// <summary>
    /// NuGet dependency graph を解決する provider を取得または設定します。null の場合は Dotnet.Script の既定 provider を使います。
    /// </summary>
    public ICsxNuGetDependencyGraphProvider? NuGetDependencyGraphProvider { get; init; }
}

/// <summary>
/// .csx file が参照できる固定 NuGet package id と version を表します。
/// </summary>
public sealed class CsxNuGetReference
{
    /// <summary>
    /// YAML deserialize 用の空の NuGet 直接参照を作成します。
    /// </summary>
    public CsxNuGetReference()
    {
    }

    /// <summary>
    /// 固定 NuGet package id と version の参照を作成します。
    /// </summary>
    /// <param name="packageId">NuGet package id。</param>
    /// <param name="version">固定 NuGet package version。</param>
    public CsxNuGetReference(string packageId, string version)
    {
        PackageId = packageId;
        Version = version;
    }

    /// <summary>
    /// NuGet package id を取得または設定します。
    /// </summary>
    public string PackageId { get; set; } = "";

    /// <summary>
    /// 固定 NuGet package version を取得または設定します。
    /// </summary>
    public string Version { get; set; } = "";
}

/// <summary>
/// NuGet dependency graph provider へ渡す解決 request を表します。
/// </summary>
public sealed class CsxNuGetDependencyGraphRequest
{
    /// <summary>
    /// NuGet dependency graph 解決 request を作成します。
    /// </summary>
    /// <param name="entryPath">entry script の path。</param>
    /// <param name="workflowRoot">workflow root の path。</param>
    /// <param name="lockFilePath">NuGet lock file の path。</param>
    /// <param name="sourceCode">NuGet directive を含む script source。</param>
    public CsxNuGetDependencyGraphRequest(
        string entryPath,
        string workflowRoot,
        string lockFilePath,
        string sourceCode)
    {
        EntryPath = entryPath;
        WorkflowRoot = workflowRoot;
        LockFilePath = lockFilePath;
        SourceCode = sourceCode;
    }

    /// <summary>
    /// entry script の path を取得します。
    /// </summary>
    public string EntryPath { get; }

    /// <summary>
    /// workflow root の path を取得します。
    /// </summary>
    public string WorkflowRoot { get; }

    /// <summary>
    /// NuGet lock file の path を取得します。
    /// </summary>
    public string LockFilePath { get; }

    /// <summary>
    /// NuGet directive を含む script source を取得します。
    /// </summary>
    public string SourceCode { get; }
}

/// <summary>
/// 解決済み NuGet dependency graph を提供する contract です。
/// </summary>
public interface ICsxNuGetDependencyGraphProvider
{
    /// <summary>
    /// 直接 NuGet 参照を解決済み dependency graph に変換します。
    /// </summary>
    /// <param name="directReferences">script から読んだ直接 NuGet 参照。</param>
    /// <param name="request">dependency graph 解決 request。</param>
    /// <returns>解決済み NuGet dependency graph。</returns>
    CsxNuGetDependencyGraph Resolve(
        IReadOnlyList<CsxNuGetReference> directReferences,
        CsxNuGetDependencyGraphRequest request);
}

/// <summary>
/// 解決済み NuGet dependency graph を表します。
/// </summary>
public sealed class CsxNuGetDependencyGraph
{
    /// <summary>
    /// 解決済み NuGet dependency graph を作成します。
    /// </summary>
    /// <param name="dependencies">解決済み NuGet 依存関係。</param>
    /// <param name="referencePaths">compile に追加する runtime assembly path。</param>
    /// <param name="resolutionMetadata">NuGet 解決時の再現性 metadata。</param>
    public CsxNuGetDependencyGraph(
        IReadOnlyList<CsxResolvedNuGetDependency> dependencies,
        IReadOnlyList<string>? referencePaths = null,
        CsxNuGetResolutionMetadata? resolutionMetadata = null)
    {
        Dependencies = dependencies;
        ReferencePaths = referencePaths ?? [];
        ResolutionMetadata = resolutionMetadata ?? new CsxNuGetResolutionMetadata("", "", [], "");
    }

    /// <summary>
    /// 解決済み NuGet 依存関係を取得します。
    /// </summary>
    public IReadOnlyList<CsxResolvedNuGetDependency> Dependencies { get; }

    /// <summary>
    /// compile に追加する runtime assembly path を取得します。
    /// </summary>
    public IReadOnlyList<string> ReferencePaths { get; }

    /// <summary>
    /// NuGet 解決時の再現性 metadata を取得します。
    /// </summary>
    public CsxNuGetResolutionMetadata ResolutionMetadata { get; }
}

/// <summary>
/// NuGet 解決時の target framework、runtime、source、Dotnet.Script.Core version を表します。
/// </summary>
public sealed class CsxNuGetResolutionMetadata
{
    /// <summary>
    /// NuGet 解決時の再現性 metadata を作成します。
    /// </summary>
    /// <param name="targetFramework">解決に使った target framework。</param>
    /// <param name="runtimeIdentifier">解決に使った runtime identifier。</param>
    /// <param name="packageSources">解決に使った package source 一覧。</param>
    /// <param name="dotnetScriptCoreVersion">解決に使った Dotnet.Script.Core version。</param>
    public CsxNuGetResolutionMetadata(
        string targetFramework,
        string runtimeIdentifier,
        IReadOnlyList<string> packageSources,
        string dotnetScriptCoreVersion)
    {
        TargetFramework = targetFramework;
        RuntimeIdentifier = runtimeIdentifier;
        PackageSources = packageSources;
        DotnetScriptCoreVersion = dotnetScriptCoreVersion;
    }

    /// <summary>
    /// 解決に使った target framework を取得します。
    /// </summary>
    public string TargetFramework { get; }

    /// <summary>
    /// 解決に使った runtime identifier を取得します。
    /// </summary>
    public string RuntimeIdentifier { get; }

    /// <summary>
    /// 解決に使った package source 一覧を取得します。
    /// </summary>
    public IReadOnlyList<string> PackageSources { get; }

    /// <summary>
    /// 解決に使った Dotnet.Script.Core version を取得します。
    /// </summary>
    public string DotnetScriptCoreVersion { get; }
}

/// <summary>
/// 解決済み NuGet 依存関係の package id、version、直接参照 flag を表します。
/// </summary>
public sealed class CsxResolvedNuGetDependency
{
    /// <summary>
    /// YAML deserialize 用の空の解決済み NuGet 依存関係を作成します。
    /// </summary>
    public CsxResolvedNuGetDependency()
    {
    }

    /// <summary>
    /// 解決済み NuGet 依存関係を作成します。
    /// </summary>
    /// <param name="packageId">NuGet package id。</param>
    /// <param name="version">解決済み NuGet package version。</param>
    /// <param name="isDirect">script の直接参照である場合は true。</param>
    public CsxResolvedNuGetDependency(string packageId, string version, bool isDirect)
    {
        PackageId = packageId;
        Version = version;
        IsDirect = isDirect;
    }

    /// <summary>
    /// NuGet package id を取得または設定します。
    /// </summary>
    public string PackageId { get; set; } = "";

    /// <summary>
    /// 解決済み NuGet package version を取得または設定します。
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// script の直接参照であるかどうかを取得または設定します。
    /// </summary>
    [YamlMember(Alias = "direct")]
    public bool IsDirect { get; set; }
}
