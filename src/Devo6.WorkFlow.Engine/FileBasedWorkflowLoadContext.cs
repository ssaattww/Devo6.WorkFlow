#if NET10_0
using System.Reflection;
using System.Runtime.Loader;
using Devo6.WorkFlow.Abstractions;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// File-based workflow の private dependency を解決し、共有 API は host assembly に結び付ける load context です。
/// </summary>
internal sealed class FileBasedWorkflowLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver dependencyResolver;
    private readonly Assembly hostEngineAssembly = typeof(WorkflowEntryAttribute).Assembly;
    private readonly Assembly hostAbstractionsAssembly = typeof(WorkflowErrorCodes).Assembly;

    /// <summary>
    /// host の API 型を共有する collectible load context を構成します。
    /// </summary>
    internal FileBasedWorkflowLoadContext(string workflowAssemblyPath)
        : base($"Devo6.WorkFlow.FileBased.{Guid.NewGuid():N}", isCollectible: true)
    {
        dependencyResolver = new AssemblyDependencyResolver(Path.GetFullPath(workflowAssemblyPath));
    }

    /// <summary>
    /// host API は共有し、それ以外の workflow dependency を解決します。
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        AssemblyName engineName = hostEngineAssembly.GetName();
        if (string.Equals(assemblyName.Name, engineName.Name, StringComparison.Ordinal))
        {
            return ResolveSharedAssembly(assemblyName, engineName, hostEngineAssembly);
        }

        AssemblyName abstractionsName = hostAbstractionsAssembly.GetName();
        if (string.Equals(assemblyName.Name, abstractionsName.Name, StringComparison.Ordinal))
        {
            return ResolveSharedAssembly(assemblyName, abstractionsName, hostAbstractionsAssembly);
        }

        string? dependencyPath = dependencyResolver.ResolveAssemblyToPath(assemblyName);
        if (dependencyPath is not null)
        {
            if (!File.Exists(dependencyPath))
            {
                throw new FileNotFoundException($"Workflow dependency was missing: {dependencyPath}", dependencyPath);
            }

            return LoadFromAssemblyPath(dependencyPath);
        }

        if (IsSharedFrameworkAssembly(assemblyName.Name))
        {
            return null;
        }

        throw new FileNotFoundException($"Workflow dependency could not be resolved: {assemblyName.FullName}");
    }

    /// <summary>
    /// workflow の unmanaged dependency を解決します。
    /// </summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? dependencyPath = dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return dependencyPath is not null && File.Exists(dependencyPath)
            ? LoadUnmanagedDllFromPath(dependencyPath)
            : IntPtr.Zero;
    }

    /// <summary>
    /// 要求された API assembly identity を host と照合します。
    /// </summary>
    private static Assembly ResolveSharedAssembly(AssemblyName requested, AssemblyName host, Assembly hostAssembly)
    {
        if (!AssemblyIdentityEquals(requested, host))
        {
            throw new FileLoadException(
                $"Workflow API assembly identity differs from the host: requested {requested.FullName}; host {host.FullName}.");
        }

        return hostAssembly;
    }

    /// <summary>
    /// CLR assembly の完全な identity を比較します。
    /// </summary>
    private static bool AssemblyIdentityEquals(AssemblyName left, AssemblyName right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && Equals(left.Version, right.Version)
            && string.Equals(left.CultureName ?? "", right.CultureName ?? "", StringComparison.OrdinalIgnoreCase)
            && (left.GetPublicKeyToken() ?? []).SequenceEqual(right.GetPublicKeyToken() ?? []);
    }

    /// <summary>
    /// default load context に委ねる shared framework assembly か判定します。
    /// </summary>
    private static bool IsSharedFrameworkAssembly(string? assemblyName)
    {
        return assemblyName is "mscorlib" or "netstandard" or "System"
            || assemblyName?.StartsWith("System.", StringComparison.Ordinal) == true
            || assemblyName?.StartsWith("Microsoft.NETCore.", StringComparison.Ordinal) == true
            || assemblyName?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true
            || assemblyName?.StartsWith("Microsoft.Win32.", StringComparison.Ordinal) == true;
    }
}
#endif
