#if NET10_0
using System.Reflection;
using Devo6.WorkFlow.Abstractions;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// Metadata 検証済みの file-based workflow Provider を host API に結び付けて実行します。
/// </summary>
internal static class FileBasedWorkflowExecutor
{
    /// <summary>
    /// metadata で選択した Provider を host API 契約で実行します。
    /// </summary>
    internal static WorkflowResult Execute(
        string assemblyPath,
        string providerTypeName,
        string expectedQualifiedEntryName,
        WorkflowExecutionOptions? options,
        CancellationToken cancellationToken = default)
    {
        FileBasedWorkflowLoadContext? loadContext = null;
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(expectedQualifiedEntryName);
        }

        Assembly assembly;
        Type providerType;
        try
        {
            loadContext = new FileBasedWorkflowLoadContext(assemblyPath);
            loadContext.PreloadDeclaredDependencies(assemblyPath);
            assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            providerType = assembly.GetType(providerTypeName, throwOnError: true, ignoreCase: false)!;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            loadContext?.Unload();
            return Failure(
                expectedQualifiedEntryName,
                WorkflowErrorCodes.ScriptLoadFailed,
                Unwrap(exception).Message);
        }

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(expectedQualifiedEntryName);
            }

            object? providerObject;
            try
            {
                providerObject = Activator.CreateInstance(providerType);
            }
            catch (Exception exception)
                when (cancellationToken.IsCancellationRequested && Unwrap(exception) is OperationCanceledException)
            {
                return Cancelled(expectedQualifiedEntryName);
            }

            if (providerObject is not IWorkflowEntryProvider provider)
            {
                return Failure(
                    expectedQualifiedEntryName,
                    WorkflowErrorCodes.ScriptApiIdentityMismatch,
                    "The loaded Provider does not implement the host IWorkflowEntryProvider type.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(expectedQualifiedEntryName);
            }

            MethodInfo[] createMethods = providerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == nameof(IWorkflowEntryProvider.Create)
                    && !method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 0
                    && method.ReturnType == typeof(IWorkflowEntry))
                .ToArray();
            if (createMethods.Length != 1)
            {
                return Failure(
                    expectedQualifiedEntryName,
                    WorkflowErrorCodes.ScriptEntryProviderInvalid,
                    "The loaded Provider does not declare the public non-generic Create() method validated from metadata.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(expectedQualifiedEntryName);
            }

            IWorkflowEntry? entry;
            try
            {
                entry = createMethods[0].Invoke(providerObject, null) as IWorkflowEntry;
            }
            catch (Exception exception)
                when (cancellationToken.IsCancellationRequested && Unwrap(exception) is OperationCanceledException)
            {
                return Cancelled(expectedQualifiedEntryName);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(expectedQualifiedEntryName);
            }

            if (entry is null || !IsHostCompositeStep(entry.GetType()))
            {
                return Failure(
                    expectedQualifiedEntryName,
                    WorkflowErrorCodes.ScriptEntryProviderInvalid,
                    "Provider.Create() must return a host CompositeStep<T> instance.");
            }

            if (!string.Equals(entry.QualifiedName, expectedQualifiedEntryName, StringComparison.Ordinal))
            {
                return Failure(
                    expectedQualifiedEntryName,
                    WorkflowErrorCodes.EntryStepNotFound,
                    $"Provider.Create() returned Entry '{entry.QualifiedName}', but metadata declares '{expectedQualifiedEntryName}'.");
            }

            WorkflowExecutionOptions? preparedOptions = CsxEntryLoader.PrepareExecutionOptions(
                entry.QualifiedName,
                entry,
                options,
                out WorkflowResult? configFailure);
            if (configFailure is not null)
            {
                return configFailure;
            }

            return ExecuteEntryWithCancellation(entry, preparedOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Failure(
                expectedQualifiedEntryName,
                WorkflowErrorCodes.ScriptEntryProviderFailed,
                Unwrap(exception).Message);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    /// <summary>
    /// 既存の公開 asynchronous workflow execution 契約に外部取消を渡します。
    /// </summary>
    private static WorkflowResult ExecuteEntryWithCancellation(
        object entry,
        WorkflowExecutionOptions? options,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(((IWorkflowEntry)entry).QualifiedName);
        }

        MethodInfo? method = entry.GetType().GetMethod(
            nameof(CompositeStep<Unit>.ExecuteWorkflowAsync),
            BindingFlags.Instance | BindingFlags.Public,
            [typeof(WorkflowExecutionOptions), typeof(CancellationToken)]);

        if (method?.Invoke(entry, [options, cancellationToken]) is Task<WorkflowResult> execution)
        {
            return execution.GetAwaiter().GetResult();
        }

        return Failure("", WorkflowErrorCodes.EntryStepNotFound, "Entry step could not be executed.");
    }

    /// <summary>
    /// Entry が host CompositeStep<T> であるか確認します。
    /// </summary>
    private static bool IsHostCompositeStep(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CompositeStep<>);
    }

    /// <summary>
    /// reflection invocation exception の inner exception を取り出します。
    /// </summary>
    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null } invocationException)
        {
            exception = invocationException.InnerException!;
        }

        return exception;
    }

    /// <summary>
    /// workflow 検証または実行の失敗結果を作成します。
    /// </summary>
    private static WorkflowResult Failure(string entryName, string errorCode, string errorMessage)
    {
        return new WorkflowResult
        {
            EntryName = entryName,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
    }

    /// <summary>
    /// Provider 構築または Step 開始前に要求された取消を結果へ変換します。
    /// </summary>
    private static WorkflowResult Cancelled(string entryName) => Failure(
        entryName,
        WorkflowErrorCodes.StepCanceled,
        "Workflow execution was cancelled before the next execution boundary.");
}
#endif
