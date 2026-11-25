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
        WorkflowExecutionOptions? options)
    {
        FileBasedWorkflowLoadContext? loadContext = null;
        Assembly assembly;
        Type providerType;
        try
        {
            loadContext = new FileBasedWorkflowLoadContext(assemblyPath);
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
            object? providerObject = Activator.CreateInstance(providerType);
            if (providerObject is not IWorkflowEntryProvider provider)
            {
                return Failure(
                    expectedQualifiedEntryName,
                    WorkflowErrorCodes.ScriptApiIdentityMismatch,
                    "The loaded Provider does not implement the host IWorkflowEntryProvider type.");
            }

            IWorkflowEntry? entry = provider.Create();
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

            return CsxEntryLoader.ExecuteEntry(entry, preparedOptions);
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
}
#endif
