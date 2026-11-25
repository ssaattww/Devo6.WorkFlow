#if NET10_0
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Devo6.WorkFlow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// File-based workflow の private dependency を解決し、共有 API は host assembly に結び付ける load context です。
/// </summary>
internal sealed class FileBasedWorkflowLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver dependencyResolver;
    private readonly Dictionary<string, Assembly> sharedAssemblies;

    /// <summary>
    /// host の API 型を共有する collectible load context を構成します。
    /// </summary>
    internal FileBasedWorkflowLoadContext(string workflowAssemblyPath)
        : base($"Devo6.WorkFlow.FileBased.{Guid.NewGuid():N}", isCollectible: true)
    {
        dependencyResolver = new AssemblyDependencyResolver(Path.GetFullPath(workflowAssemblyPath));
        sharedAssemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal)
        {
            [typeof(WorkflowEntryAttribute).Assembly.GetName().Name!] = typeof(WorkflowEntryAttribute).Assembly,
            [typeof(WorkflowErrorCodes).Assembly.GetName().Name!] = typeof(WorkflowErrorCodes).Assembly,
            [typeof(ILogger).Assembly.GetName().Name!] = typeof(ILogger).Assembly,
        };
    }

    /// <summary>
    /// Provider を生成する前に入力と推移依存から参照される private assemblies を解決します。
    /// </summary>
    internal void PreloadDeclaredDependencies(string workflowAssemblyPath)
    {
        var pendingPaths = new Queue<string>();
        var inspectedPaths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        pendingPaths.Enqueue(Path.GetFullPath(workflowAssemblyPath));

        while (pendingPaths.TryDequeue(out string? assemblyPath))
        {
            if (!inspectedPaths.Add(assemblyPath))
            {
                continue;
            }

            using FileStream stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                throw new BadImageFormatException($"A declared workflow dependency has no metadata: {assemblyPath}");
            }

            MetadataReader metadata = peReader.GetMetadataReader();
            foreach (AssemblyReferenceHandle referenceHandle in metadata.AssemblyReferences)
            {
                AssemblyName requested = GetAssemblyName(metadata, referenceHandle);
                if (sharedAssemblies.TryGetValue(requested.Name ?? "", out Assembly? hostAssembly))
                {
                    ResolveSharedAssembly(requested, hostAssembly.GetName(), hostAssembly);
                    continue;
                }

                if (IsSharedFrameworkAssembly(requested.Name))
                {
                    continue;
                }

                string? dependencyPath = dependencyResolver.ResolveAssemblyToPath(requested);
                if (dependencyPath is null || !File.Exists(dependencyPath))
                {
                    throw new FileNotFoundException(
                        $"A declared workflow dependency could not be resolved before Provider creation: {requested.FullName}",
                        dependencyPath);
                }

                AssemblyName resolvedIdentity = AssemblyName.GetAssemblyName(dependencyPath);
                if (!AssemblyIdentityEquals(requested, resolvedIdentity))
                {
                    throw new FileLoadException(
                        $"A declared workflow dependency has a different assembly identity: requested {requested.FullName}; resolved {resolvedIdentity.FullName}.");
                }

                LoadFromAssemblyPath(Path.GetFullPath(dependencyPath));
                pendingPaths.Enqueue(Path.GetFullPath(dependencyPath));
            }
        }
    }

    /// <summary>
    /// host API は共有し、それ以外の workflow dependency を解決します。
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (sharedAssemblies.TryGetValue(assemblyName.Name ?? "", out Assembly? hostAssembly))
        {
            return ResolveSharedAssembly(assemblyName, hostAssembly.GetName(), hostAssembly);
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
    /// PE metadata の AssemblyReference を CLR identity へ変換します。
    /// </summary>
    private static AssemblyName GetAssemblyName(MetadataReader reader, AssemblyReferenceHandle handle)
    {
        AssemblyReference reference = reader.GetAssemblyReference(handle);
        var name = new AssemblyName
        {
            Name = reader.GetString(reference.Name),
            Version = reference.Version,
            CultureName = reference.Culture.IsNil ? "" : reader.GetString(reference.Culture),
        };
        byte[] keyOrToken = reader.GetBlobBytes(reference.PublicKeyOrToken);
        if ((reference.Flags & AssemblyFlags.PublicKey) != 0)
        {
            name.SetPublicKey(keyOrToken);
        }
        else if (keyOrToken.Length > 0)
        {
            name.SetPublicKeyToken(keyOrToken);
        }

        return name;
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
