#if NET10_0
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Devo6.WorkFlow.Abstractions;
using NuGet.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// .NET 10 file-based C# source を class-only Library として restore / build します。
/// </summary>
public sealed class FileBasedCsWorkflowBuilder
{
    private const string EffectiveProjectProperties =
        "TargetFramework,TargetFrameworks,OutputType,PublishAot,RestoreSources,RestoreAdditionalProjectSources,RestoreConfigFile,RestorePackagesWithLockFile,RestoreLockedMode,ManagePackageVersionsCentrally,MSBuildSDKsPath,MSBuildExtensionsPath,BaseOutputPath,OutputPath,OutDir,BaseIntermediateOutputPath,IntermediateOutputPath,MSBuildProjectExtensionsPath,TargetPath,ArtifactsPath,RuntimeIdentifier,RuntimeIdentifiers";
    private static readonly IDeserializer LockDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
    private readonly IFileBasedSdkRunner sdkRunner;
    private readonly Func<string> sdkRunFileRootProvider;
    private readonly Func<string>? sdkSessionRootProvider;
    private readonly bool sessionRootUsesRunFileRoot;
    private readonly Action<string>? outputLockContentionObserver;

    /// <summary>
    /// .NET 10 file-based C# builder を作成します。
    /// </summary>
    public FileBasedCsWorkflowBuilder()
        : this(new DotnetFileBasedSdkRunner())
    {
    }

    /// <summary>
    /// file-based C# workflow の restore、build、検証、実行を構成します。
    /// </summary>
    internal FileBasedCsWorkflowBuilder(IFileBasedSdkRunner sdkRunner)
        : this(sdkRunner, sdkRunFileRootProvider: null)
    {
    }

    /// <summary>
    /// SDK runner と isolatable artifact root を指定して builder を作成します。
    /// </summary>
    /// <param name="sdkRunner">SDK process の実行を担当する runner。</param>
    /// <param name="sdkRunFileRootProvider">SDK runfile 出力の調整用 lock root を返す関数。</param>
    /// <param name="outputLockContentionObserver">排他の競合を観測する任意 callback。</param>
    /// <param name="sdkSessionRootProvider">要求専用 SDK session を作る一時 root を返す関数。</param>
    internal FileBasedCsWorkflowBuilder(
        IFileBasedSdkRunner sdkRunner,
        Func<string>? sdkRunFileRootProvider,
        Action<string>? outputLockContentionObserver = null,
        Func<string>? sdkSessionRootProvider = null)
    {
        this.sdkRunner = sdkRunner;
        this.sdkRunFileRootProvider = sdkRunFileRootProvider ?? GetCurrentSdkRunFileRoot;
        this.sdkSessionRootProvider = sdkSessionRootProvider;
        sessionRootUsesRunFileRoot = sdkRunFileRootProvider is not null && sdkSessionRootProvider is null;
        this.outputLockContentionObserver = outputLockContentionObserver;
    }

    /// <summary>
    /// 信頼済み `.cs` file を SDK 10.0.4xx で一度 restore し、assets を lock と照合後に build します。
    /// </summary>
    /// <param name="entryPath">class-only file-based workflow source。</param>
    /// <param name="hostPackageVersion">呼出元が実際に配布する Engine / CLI の NuGet package version。</param>
    /// <param name="cancellationToken">処理のキャンセル。</param>
    /// <returns>build 結果と SDK が生成した分離成果物 path。</returns>
    public FileBasedCsBuildResult Build(
        string entryPath,
        string hostPackageVersion,
        CancellationToken cancellationToken = default,
        IReadOnlyList<CsxNuGetReference>? allowedNuGetReferences = null)
    {
        return BuildCore(entryPath, hostPackageVersion, cancellationToken, afterBuild: null, allowedNuGetReferences);
    }

    /// <summary>
    /// File-based Library を生成し、実行せずに Entry metadata を検証します。
    /// </summary>
    /// <param name="entryPath">class-only file-based workflow source。</param>
    /// <param name="hostPackageVersion">呼出元が実際に配布する Engine / CLI の NuGet package version。</param>
    /// <param name="entryName">検証する Entry 名。未指定時は Main。</param>
    /// <param name="configPaths">存在を事前確認する workflow / engine config path。</param>
    /// <param name="cancellationToken">処理のキャンセル。</param>
    /// <returns>検証で見つかった error。成果物の build / metadata 検証中は出力 lock を保持します。</returns>
    public WorkflowValidationResult Validate(
        string entryPath,
        string hostPackageVersion,
        string? entryName = null,
        IReadOnlyList<string>? configPaths = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<CsxNuGetReference>? allowedNuGetReferences = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        string entryDirectory = Path.GetDirectoryName(Path.GetFullPath(entryPath))!;
        foreach (string configPath in configPaths ?? [])
        {
            string fullConfigPath = Path.GetFullPath(Path.IsPathRooted(configPath)
                ? configPath
                : Path.Combine(entryDirectory, configPath));
            if (!File.Exists(fullConfigPath))
            {
                return Failure(
                    fullConfigPath,
                    WorkflowErrorCodes.ConfigNotFound,
                    $"Workflow config file was not found: {fullConfigPath}");
            }
        }

        WorkflowValidationResult? metadataResult = null;
        FileBasedCsBuildResult buildResult = BuildCore(
            entryPath,
            hostPackageVersion,
            cancellationToken,
            assemblyPath => metadataResult = FileBasedCsMetadataValidator.Validate(assemblyPath, entryName),
            allowedNuGetReferences);
        if (!buildResult.Succeeded)
        {
            return Failure(entryPath, buildResult.ErrorCode, buildResult.ErrorMessage);
        }

        return metadataResult ?? Failure(
            entryPath,
            WorkflowErrorCodes.ScriptLoadFailed,
            "Metadata validation did not produce a result.");
    }

    /// <summary>
    /// File-based Library を生成し、metadata-only 検査に合格した Provider だけを同じ出力 lock 内で実行します。
    /// </summary>
    public WorkflowResult Run(
        string entryPath,
        string hostPackageVersion,
        string? entryName = null,
        WorkflowExecutionOptions? options = null,
        IReadOnlyList<string>? configPaths = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<CsxNuGetReference>? allowedNuGetReferences = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        string entryDirectory = Path.GetDirectoryName(Path.GetFullPath(entryPath))!;
        foreach (string configPath in configPaths ?? [])
        {
            string fullConfigPath = Path.GetFullPath(Path.IsPathRooted(configPath)
                ? configPath
                : Path.Combine(entryDirectory, configPath));
            if (!File.Exists(fullConfigPath))
            {
                return ExecutionFailure(fullConfigPath, WorkflowErrorCodes.ConfigNotFound,
                    $"Workflow config file was not found: {fullConfigPath}");
            }
        }

        WorkflowResult? executionResult = null;
        FileBasedCsBuildResult buildResult = BuildCore(
            entryPath,
            hostPackageVersion,
            cancellationToken,
            assemblyPath =>
            {
                WorkflowValidationResult metadataResult = FileBasedCsMetadataValidator.Validate(
                    assemblyPath, entryName, out string? providerTypeName, out string? qualifiedEntryName);
                if (!metadataResult.Succeeded || providerTypeName is null || qualifiedEntryName is null)
                {
                    ValidationError? error = metadataResult.Errors.FirstOrDefault();
                    executionResult = ExecutionFailure(entryPath,
                        error?.Code ?? WorkflowErrorCodes.ScriptLoadFailed,
                        error?.Message ?? "Metadata validation did not produce an Entry Provider.");
                    return;
                }

                executionResult = FileBasedWorkflowExecutor.Execute(
                    assemblyPath, providerTypeName, qualifiedEntryName, options, cancellationToken);
            },
            allowedNuGetReferences);
        if (!buildResult.Succeeded)
        {
            return ExecutionFailure(entryPath, buildResult.ErrorCode, buildResult.ErrorMessage);
        }

        return executionResult ?? ExecutionFailure(entryPath, WorkflowErrorCodes.ScriptLoadFailed,
            "Provider execution did not produce a result.");
    }

    /// <summary>
    /// file-based workflow の実行失敗を作成します。
    /// </summary>
    private static WorkflowResult ExecutionFailure(string entryName, string code, string message) => new()
    {
        EntryName = entryName,
        ErrorCode = code,
        ErrorMessage = message,
    };

    /// <summary>
    /// 入力を前提検査し、SDK restore と build を行って callback を実行します。
    /// </summary>
    private FileBasedCsBuildResult BuildCore(
        string entryPath,
        string hostPackageVersion,
        CancellationToken cancellationToken,
        Action<string>? afterBuild,
        IReadOnlyList<CsxNuGetReference>? allowedNuGetReferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);

        string fullEntryPath = Path.GetFullPath(entryPath);
        CsFileInputPreflight.PreflightResult preflight = CsFileInputPreflight.Validate(
            fullEntryPath,
            CsFileInputPreflight.CurrentHostTargetFramework,
            allowedNuGetReferences);
        if (!preflight.Succeeded)
        {
            return FileBasedCsBuildResult.Failure(preflight.ErrorCode, preflight.ErrorMessage);
        }

        if (!IsExactPackageVersion(hostPackageVersion))
        {
            return FileBasedCsBuildResult.Failure(
                WorkflowErrorCodes.PackageVersionUnknown,
                "The host NuGet package version is missing or invalid.");
        }

        if (!preflight.PackageReferences.TryGetValue("Devo6.WorkFlow.Engine", out string? requestedEngineVersion)
            || !string.Equals(requestedEngineVersion, hostPackageVersion, StringComparison.OrdinalIgnoreCase))
        {
            return FileBasedCsBuildResult.Failure(
                WorkflowErrorCodes.ScriptApiIdentityMismatch,
                $"The Devo6.WorkFlow.Engine package version must exactly match the host package version {hostPackageVersion}.");
        }

        if (!TryReadAndValidateLock(preflight, fullEntryPath, out FileBasedNuGetLock? lockFile, out string lockError))
        {
            return FileBasedCsBuildResult.Failure(WorkflowErrorCodes.ScriptNugetLockMismatch, lockError);
        }

        string sourceHash = ComputeFileHash(fullEntryPath);
        string workingDirectory = Path.GetDirectoryName(fullEntryPath)!;
        string runFileRoot;
        try
        {
            runFileRoot = sdkRunFileRootProvider();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return FileBasedCsBuildResult.Failure(WorkflowErrorCodes.ScriptBuildFailed, exception.Message);
        }

        FileStream outputLock;
        try
        {
            outputLock = AcquireFileBasedOutputLock(fullEntryPath, runFileRoot, cancellationToken, outputLockContentionObserver);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        {
            return FileBasedCsBuildResult.Failure(
                WorkflowErrorCodes.ScriptBuildFailed,
                $"Could not acquire the file-based output lock: {exception.Message}");
        }

        using (outputLock)
        {
            FileBasedSdkRunContext sdkRunContext;
            try
            {
                string sdkSessionRoot = sdkSessionRootProvider?.Invoke()
                    ?? (sessionRootUsesRunFileRoot ? runFileRoot : Path.GetTempPath());
                sdkRunContext = FileBasedSdkRunContext.Create(sdkSessionRoot);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptBuildFailed,
                    $"Could not create an isolated SDK output directory: {exception.Message}");
            }

            FileBasedSdkProcessResult sdkVersionResult;
            try
            {
                sdkVersionResult = sdkRunner.Run(["--version"], workingDirectory, cancellationToken, sdkRunContext);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return FileBasedCsBuildResult.Failure(WorkflowErrorCodes.ScriptSdkUnavailable, exception.Message);
            }
            string selectedSdkVersion = sdkVersionResult.StandardOutput.Trim();
            if (!sdkVersionResult.Succeeded || !IsSupportedSdkVersion(selectedSdkVersion))
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptSdkUnavailable,
                    $".NET SDK 10.0.404 or later in feature band 10.0.4xx is required. Selected SDK: {selectedSdkVersion}. {FormatProcessFailure(sdkVersionResult)}");
            }

            FileBasedSdkProcessResult sdkInfoResult;
            try
            {
                sdkInfoResult = sdkRunner.Run(["--info"], workingDirectory, cancellationToken, sdkRunContext);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return FileBasedCsBuildResult.Failure(WorkflowErrorCodes.ScriptSdkUnavailable, exception.Message);
            }

            if (!sdkInfoResult.Succeeded
                || !TryGetSdkSdksPath(sdkInfoResult.StandardOutput, selectedSdkVersion, out string expectedSdkSdksPath))
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported,
                    "The selected SDK installation path could not be verified before restore.");
            }

            FileBasedSdkProcessResult evaluationResult;
            try
            {
                evaluationResult = sdkRunner.Run(
                    ["build", fullEntryPath, "--no-restore", $"-getProperty:{EffectiveProjectProperties}", "-getItem:PackageReference"],
                    workingDirectory,
                    cancellationToken,
                    sdkRunContext);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return FileBasedCsBuildResult.Failure(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, exception.Message);
            }

            string evaluationError = "";
            if (!evaluationResult.Succeeded
                || !TryValidateEffectiveProject(
                    evaluationResult.StandardOutput,
                    preflight,
                    lockFile!,
                    expectedSdkSdksPath,
                    GetSdkArtifactsDirectory(fullEntryPath, sdkRunContext.RunFileRoot),
                    fullEntryPath,
                    out evaluationError))
            {
                string detail = evaluationResult.Succeeded
                    ? evaluationError
                    : FormatProcessFailure(evaluationResult);
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported,
                    $"The effective file-based project could not be verified before restore: {detail}");
            }

            FileBasedSdkProcessResult restoreResult;
            try
            {
                restoreResult = sdkRunner.Run(
                    ["restore", fullEntryPath, "--verbosity", "quiet"],
                    workingDirectory,
                    cancellationToken,
                    sdkRunContext);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return FileBasedCsBuildResult.Failure(WorkflowErrorCodes.ScriptNugetRestoreFailed, exception.Message);
            }
            if (!restoreResult.Succeeded)
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptNugetRestoreFailed,
                    FormatProcessFailure(restoreResult));
            }

            string? assetsPath = FindAssetsFile(fullEntryPath, sdkRunContext.RunFileRoot);
            string assetsError = "";
            if (assetsPath is null || !TryValidateAssets(assetsPath, preflight, lockFile!, out assetsError))
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptNugetLockMismatch,
                    assetsPath is null ? "The SDK did not produce the expected project.assets.json." : assetsError);
            }

            string postRestoreSourceHash;
            try
            {
                postRestoreSourceHash = ComputeFileHash(fullEntryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptFileDirectiveInvalid,
                    $"The workflow source could not be re-read after restore; the restored assets were discarded: {exception.Message}");
            }

            if (!string.Equals(sourceHash, postRestoreSourceHash, StringComparison.Ordinal))
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptFileDirectiveInvalid,
                    "The workflow source changed during restore; the restored assets were discarded.");
            }

            FileBasedSdkProcessResult buildResult;
            try
            {
                buildResult = sdkRunner.Run(
                    ["build", fullEntryPath, "--no-restore", "--verbosity", "quiet"],
                    workingDirectory,
                    cancellationToken,
                    sdkRunContext);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return FileBasedCsBuildResult.Failure(WorkflowErrorCodes.ScriptBuildFailed, exception.Message);
            }
            if (!buildResult.Succeeded)
            {
                return FileBasedCsBuildResult.Failure(
                    ClassifyBuildFailure(buildResult),
                    FormatProcessFailure(buildResult));
            }

            string postBuildSourceHash;
            try
            {
                postBuildSourceHash = ComputeFileHash(fullEntryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptFileDirectiveInvalid,
                    $"The workflow source could not be re-read after build; the generated assembly was discarded: {exception.Message}");
            }

            if (!string.Equals(sourceHash, postBuildSourceHash, StringComparison.Ordinal))
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptFileDirectiveInvalid,
                    "The workflow source changed during build; the generated assembly was discarded.");
            }

            string assemblyPath = GetSdkOutputAssemblyPath(fullEntryPath, sdkRunContext.RunFileRoot);
            if (!File.Exists(assemblyPath) || IsInsideDirectory(assemblyPath, workingDirectory))
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptBuildFailed,
                    "The SDK output was missing or was not isolated from the workflow source directory.");
            }

            afterBuild?.Invoke(assemblyPath);
            return FileBasedCsBuildResult.Success(assemblyPath, assetsPath, selectedSdkVersion);
        }
    }

    /// <summary>
    /// workflow 検証または実行の失敗結果を作成します。
    /// </summary>
    private static WorkflowValidationResult Failure(string path, string code, string message)
    {
        return new WorkflowValidationResult
        {
            Errors =
            [
                new ValidationError
                {
                    Path = path,
                    Code = code,
                    Message = message,
                },
            ],
        };
    }

    /// <summary>
    /// 入力隣接 NuGet lock の direct references と source を照合します。
    /// </summary>
    private static bool TryReadAndValidateLock(
        CsFileInputPreflight.PreflightResult preflight,
        string entryPath,
        out FileBasedNuGetLock? lockFile,
        out string error)
    {
        lockFile = null;
        error = "";
        try
        {
            FileBasedNuGetLock? parsedLock = LockDeserializer.Deserialize<FileBasedNuGetLock>(File.ReadAllText(preflight.LockFilePath));
            lockFile = parsedLock;
            if (parsedLock is null || parsedLock.Version != 1)
            {
                error = "NuGet lock file version is missing or unsupported.";
                return false;
            }

            if (!string.Equals(parsedLock.TargetFramework, "net10.0", StringComparison.OrdinalIgnoreCase))
            {
                error = "NuGet lock targetFramework must be net10.0.";
                return false;
            }

            if (parsedLock.DirectReferences.Count != preflight.PackageReferences.Count
                || preflight.PackageReferences.Any(reference => !parsedLock.DirectReferences.Any(locked =>
                    string.Equals(locked.PackageId, reference.Key, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(locked.Version, reference.Value, StringComparison.OrdinalIgnoreCase))))
            {
                error = "NuGet direct references do not exactly match the file-based package directives.";
                return false;
            }

            if (parsedLock.PackageSources.Count == 0 || !parsedLock.VerifyPackageSources)
            {
                error = "NuGet lock file must enable package source verification and list its sources.";
                return false;
            }

            IReadOnlyList<string> configuredSources = ReadPackageSources(Path.GetDirectoryName(entryPath)!);
            if (!PackageSourcesEqual(configuredSources, parsedLock.PackageSources, Path.GetDirectoryName(entryPath)!))
            {
                error = "Configured NuGet package sources do not match the lock file.";
                return false;
            }

            if (parsedLock.ResolvedDependencies.Count == 0
                || parsedLock.ResolvedDependencies.GroupBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            {
                error = "NuGet lock file must list each resolved package exactly once.";
                return false;
            }

            foreach (KeyValuePair<string, string> direct in preflight.PackageReferences)
            {
                FileBasedLockedPackage? package = parsedLock.ResolvedDependencies.SingleOrDefault(item =>
                    string.Equals(item.PackageId, direct.Key, StringComparison.OrdinalIgnoreCase));
                if (package is null
                    || !package.Direct
                    || !string.Equals(package.Version, direct.Value, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"NuGet lock direct package {direct.Key} does not match exact version {direct.Value}.";
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            error = $"NuGet lock file could not be read: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// restore 済み assets と要求 lock の依存 graph を照合します。
    /// </summary>
    private static bool TryValidateAssets(
        string assetsPath,
        CsFileInputPreflight.PreflightResult preflight,
        FileBasedNuGetLock lockFile,
        out string error)
    {
        error = "";
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("targets", out JsonElement targets)
                || targets.EnumerateObject().Count() != 1
                || !targets.TryGetProperty("net10.0", out _))
            {
                error = "Restored assets do not target only net10.0.";
                return false;
            }

            if (!root.TryGetProperty("libraries", out JsonElement libraries))
            {
                error = "Restored assets do not contain a libraries graph.";
                return false;
            }

            JsonElement frameworkDependencies = root.GetProperty("project")
                .GetProperty("frameworks")
                .GetProperty("net10.0")
                .GetProperty("dependencies");
            if (frameworkDependencies.EnumerateObject().Count() != preflight.PackageReferences.Count
                || preflight.PackageReferences.Any(reference =>
                    !frameworkDependencies.TryGetProperty(reference.Key, out JsonElement dependency)
                    || !string.Equals(dependency.GetProperty("target").GetString(), "Package", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        dependency.GetProperty("version").GetString(),
                        $"[{reference.Value}, {reference.Value}]",
                        StringComparison.OrdinalIgnoreCase)))
            {
                error = "The evaluated PackageReference set or exact version range differs from the file-based directives.";
                return false;
            }

            var actual = new Dictionary<string, FileBasedLockedPackage>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty library in libraries.EnumerateObject())
            {
                if (!string.Equals(library.Value.GetProperty("type").GetString(), "package", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int separator = library.Name.LastIndexOf('/');
                if (separator <= 0 || separator == library.Name.Length - 1)
                {
                    error = $"Restored package identity is malformed: {library.Name}";
                    return false;
                }

                string packageId = library.Name[..separator];
                string packageVersion = library.Name[(separator + 1)..];
                actual.Add(packageId, new FileBasedLockedPackage
                {
                    PackageId = packageId,
                    Version = packageVersion,
                    Direct = preflight.PackageReferences.ContainsKey(packageId),
                });
            }

            if (actual.Count != lockFile.ResolvedDependencies.Count
                || lockFile.ResolvedDependencies.Any(locked => !actual.TryGetValue(locked.PackageId, out FileBasedLockedPackage? resolved)
                    || !string.Equals(locked.Version, resolved.Version, StringComparison.OrdinalIgnoreCase)
                    || locked.Direct != resolved.Direct))
            {
                error = "Resolved NuGet package versions or direct/transitive flags do not match the lock file.";
                return false;
            }

            JsonElement restore = root.GetProperty("project").GetProperty("restore");
            IReadOnlyList<string> restoredSources = restore.GetProperty("sources").EnumerateObject().Select(item => item.Name).ToArray();
            if (!PackageSourcesEqual(restoredSources, lockFile.PackageSources, Path.GetDirectoryName(preflight.LockFilePath)!))
            {
                error = "Restored NuGet package sources do not match the lock file.";
                return false;
            }

            if (restore.TryGetProperty("runtimeIdentifier", out JsonElement runtimeIdentifier)
                && !string.Equals(runtimeIdentifier.GetString() ?? "", lockFile.RuntimeIdentifier ?? "", StringComparison.OrdinalIgnoreCase))
            {
                error = "Restored runtime identifier does not match the lock file.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = $"Restored NuGet assets could not be checked: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// SDK の evaluation-only 出力から実効 project properties と直接参照を確認します。
    /// </summary>
    private static bool TryValidateEffectiveProject(
        string evaluationJson,
        CsFileInputPreflight.PreflightResult preflight,
        FileBasedNuGetLock lockFile,
        string expectedSdkSdksPath,
        string expectedArtifactsDirectory,
        string entryPath,
        out string error)
    {
        return ValidateEffectiveProjectJson(
            evaluationJson,
            preflight.PackageReferences,
            lockFile.PackageSources,
            expectedSdkSdksPath,
            expectedArtifactsDirectory,
            entryPath,
            out error);
    }

    /// <summary>
    /// Evaluation-only JSON の TFM、build properties、source、直接参照を照合します。
    /// </summary>
    internal static bool ValidateEffectiveProjectJson(
        string evaluationJson,
        IReadOnlyDictionary<string, string> expectedPackageReferences,
        IReadOnlyList<string> expectedSources,
        string expectedSdkSdksPath,
        string expectedArtifactsDirectory,
        string entryPath,
        out string error)
    {
        error = "";
        try
        {
            using JsonDocument document = JsonDocument.Parse(evaluationJson);
            JsonElement root = document.RootElement;
            JsonElement properties = root.GetProperty("Properties");
            string targetFramework = GetProperty(properties, "TargetFramework");
            string targetFrameworks = GetProperty(properties, "TargetFrameworks");
            string outputType = GetProperty(properties, "OutputType");
            string publishAot = GetProperty(properties, "PublishAot");
            string packageLockEnabled = GetProperty(properties, "RestorePackagesWithLockFile");
            string restoreLockedMode = GetProperty(properties, "RestoreLockedMode");
            string centralVersionsEnabled = GetProperty(properties, "ManagePackageVersionsCentrally");
            string additionalSources = GetProperty(properties, "RestoreAdditionalProjectSources");
            string restoreConfigFile = GetProperty(properties, "RestoreConfigFile");
            string restoreSources = GetProperty(properties, "RestoreSources");
            string msbuildSdksPath = GetProperty(properties, "MSBuildSDKsPath");
            string msbuildExtensionsPath = GetProperty(properties, "MSBuildExtensionsPath");
            string baseOutputPath = GetProperty(properties, "BaseOutputPath");
            string outputPath = GetProperty(properties, "OutputPath");
            string outDir = GetProperty(properties, "OutDir");
            string baseIntermediateOutputPath = GetProperty(properties, "BaseIntermediateOutputPath");
            string intermediateOutputPath = GetProperty(properties, "IntermediateOutputPath");
            string projectExtensionsPath = GetProperty(properties, "MSBuildProjectExtensionsPath");
            string targetPath = GetProperty(properties, "TargetPath");
            string artifactsPath = GetProperty(properties, "ArtifactsPath");
            string runtimeIdentifier = GetProperty(properties, "RuntimeIdentifier");
            string runtimeIdentifiers = GetProperty(properties, "RuntimeIdentifiers");

            if (!string.Equals(targetFramework, "net10.0", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(targetFrameworks)
                || !string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(publishAot, "false", StringComparison.OrdinalIgnoreCase))
            {
                error = "The evaluated project must target only net10.0, produce a Library, and disable AOT.";
                return false;
            }

            if (!PathsEqual(msbuildSdksPath, expectedSdkSdksPath))
            {
                error = $"The evaluated MSBuild SDK path does not match the selected SDK installation: {msbuildSdksPath}";
                return false;
            }

            string expectedMsbuildExtensionsPath = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(expectedSdkSdksPath)) ?? "";
            if (!PathsEqual(msbuildExtensionsPath, expectedMsbuildExtensionsPath))
            {
                error = $"The evaluated MSBuild extensions path does not match the selected SDK installation: {msbuildExtensionsPath}";
                return false;
            }

            string expectedArtifacts = Path.GetFullPath(expectedArtifactsDirectory);
            string expectedBin = Path.Combine(expectedArtifacts, "bin");
            string expectedOutput = Path.Combine(expectedBin, "debug");
            string expectedObj = Path.Combine(expectedArtifacts, "obj");
            string expectedTarget = Path.Combine(expectedOutput, Path.GetFileNameWithoutExtension(entryPath) + ".dll");
            if (!PathsEqual(baseOutputPath, expectedBin)
                || !PathsEqual(outputPath, expectedOutput)
                || !PathsEqual(outDir, expectedOutput)
                || !PathsEqual(baseIntermediateOutputPath, expectedObj)
                || !PathsEqual(intermediateOutputPath, Path.Combine(expectedObj, "debug"))
                || !PathsEqual(projectExtensionsPath, expectedObj)
                || !PathsEqual(targetPath, expectedTarget)
                || !PathsEqual(artifactsPath, expectedArtifacts)
                || !string.IsNullOrWhiteSpace(runtimeIdentifier)
                || !string.IsNullOrWhiteSpace(runtimeIdentifiers))
            {
                error = "The effective SDK output, intermediate, assets, or target path differs from the locked file-based artifact directory.";
                return false;
            }

            if (IsEnabled(packageLockEnabled) || IsEnabled(restoreLockedMode) || IsEnabled(centralVersionsEnabled)
                || !string.IsNullOrWhiteSpace(additionalSources)
                || !string.IsNullOrWhiteSpace(restoreConfigFile))
            {
                error = "The evaluated project uses an unsupported lock, central package version, or NuGet source override.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(restoreSources))
            {
                string[] sources = restoreSources.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!PackageSourcesEqual(sources, expectedSources, Path.GetDirectoryName(entryPath)!))
                {
                    error = "The evaluated NuGet source override does not match the required lock sources.";
                    return false;
                }
            }

            if (!root.TryGetProperty("Items", out JsonElement items)
                || !items.TryGetProperty("PackageReference", out JsonElement packageReferences)
                || packageReferences.ValueKind != JsonValueKind.Array
                || packageReferences.GetArrayLength() != expectedPackageReferences.Count)
            {
                error = "The evaluated PackageReference set differs from the input directives.";
                return false;
            }

            foreach (JsonElement packageReference in packageReferences.EnumerateArray())
            {
                string packageId = GetProperty(packageReference, "Identity");
                string version = GetProperty(packageReference, "Version");
                if (!expectedPackageReferences.TryGetValue(packageId, out string? requestedVersion)
                    || !string.Equals(version, $"[{requestedVersion}]", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"The evaluated PackageReference is not an exact input directive: {packageId} {version}";
                    return false;
                }

                HashSet<string> expectedMetadata = new(StringComparer.Ordinal)
                {
                    "Identity", "Version", "FullPath", "RootDir", "Filename", "Extension", "RelativeDir",
                    "Directory", "RecursiveDir", "ModifiedTime", "CreatedTime", "AccessedTime",
                    "DefiningProjectFullPath", "DefiningProjectDirectory", "DefiningProjectName", "DefiningProjectExtension",
                };
                if (packageReference.EnumerateObject().Any(property => !expectedMetadata.Contains(property.Name)))
                {
                    error = $"The evaluated PackageReference contains unsupported metadata: {packageId}";
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or NotSupportedException or IOException)
        {
            error = $"The SDK did not return verifiable effective project data: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// build failure が C# compiler diagnostic を含む場合は専用診断へ分類します。
    /// </summary>
    internal static string ClassifyBuildFailure(FileBasedSdkProcessResult result)
    {
        string output = result.StandardError + Environment.NewLine + result.StandardOutput;
        return Regex.IsMatch(output, @"\berror\s+CS\d{4,6}\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            ? WorkflowErrorCodes.ScriptCompileFailed
            : WorkflowErrorCodes.ScriptBuildFailed;
    }

    /// <summary>
    /// SDK 10.0.404 以上かつ同じ 10.0.4xx feature band か判定します。
    /// </summary>
    internal static bool IsSupportedSdkVersion(string value)
    {
        return Version.TryParse(value, out Version? version)
            && version.Major == 10
            && version.Minor == 0
            && version.Build is >= 404 and <= 499;
    }

    /// <summary>
    /// SDK metadata の property 値を string として取得します。
    /// </summary>
    private static string GetProperty(JsonElement container, string name)
    {
        return container.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
            : "";
    }

    /// <summary>
    /// project property が true として指定されているか判定します。
    /// </summary>
    private static bool IsEnabled(string value) => bool.TryParse(value, out bool enabled) && enabled;

    /// <summary>
    /// 固定された CLI 表示言語で得た `dotnet --info` から選択 SDK の installation path を取得します。
    /// </summary>
    internal static bool TryGetSdkSdksPath(string sdkInfo, string selectedSdkVersion, out string sdkSdksPath)
    {
        sdkSdksPath = "";
        foreach (string line in sdkInfo.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            const string prefix = "Base Path:";
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string basePath = trimmed[prefix.Length..].Trim();
            if (!Path.IsPathRooted(basePath))
            {
                return false;
            }
            string normalizedBasePath;
            try
            {
                normalizedBasePath = Path.GetFullPath(basePath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
            {
                return false;
            }
            if (!string.Equals(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedBasePath)),
                    selectedSdkVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            sdkSdksPath = Path.GetFullPath(Path.Combine(normalizedBasePath, "Sdks"));
            return true;
        }

        return false;
    }

    /// <summary>
    /// SDK paths を OS の file-system comparison で比較します。
    /// </summary>
    private static bool PathsEqual(string left, string right)
    {
        return Path.IsPathRooted(left)
            && Path.IsPathRooted(right)
            && string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// 有効な NuGet package source を読み取ります。
    /// </summary>
    private static IReadOnlyList<string> ReadPackageSources(string workingDirectory)
    {
        ISettings settings = NuGet.Configuration.Settings.LoadDefaultSettings(workingDirectory);
        return new PackageSourceProvider(settings)
            .LoadPackageSources()
            .Where(source => source.IsEnabled)
            .Select(source => source.Source)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// NuGet source URLs compare by URI authority and case-sensitive path; local paths use OS path casing.
    /// </summary>
    internal static bool PackageSourcesEqual(IEnumerable<string> left, IEnumerable<string> right, string baseDirectory)
    {
        return PackageSourcesEqual(left, right, baseDirectory, OperatingSystem.IsWindows());
    }

    /// <summary>
    /// 指定した OS の path 規則で NuGet package source の集合が一致するか判定します。
    /// </summary>
    /// <param name="left">比較する左側の package source。</param>
    /// <param name="right">比較する右側の package source。</param>
    /// <param name="baseDirectory">相対 path を解決する基準 directory。</param>
    /// <param name="isWindows">Windows の path 比較規則を使う場合は <see langword="true"/>。</param>
    /// <returns>正規化後の source 集合が一致する場合は <see langword="true"/>。</returns>
    internal static bool PackageSourcesEqual(
        IEnumerable<string> left,
        IEnumerable<string> right,
        string baseDirectory,
        bool isWindows)
    {
        string[] normalizedLeft = left.Select(source => NormalizePackageSource(source, baseDirectory, isWindows)).ToArray();
        string[] normalizedRight = right.Select(source => NormalizePackageSource(source, baseDirectory, isWindows)).ToArray();
        return new HashSet<string>(normalizedLeft, StringComparer.Ordinal).SetEquals(normalizedRight);
    }

    /// <summary>
    /// source URI または相対 path を比較用の platform identity に正規化します。
    /// </summary>
    /// <param name="source">元の NuGet package source。</param>
    /// <param name="baseDirectory">相対 path を解決する基準 directory。</param>
    /// <returns>source の比較用 identity。</returns>
    private static string NormalizePackageSource(string source, string baseDirectory, bool isWindows)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
        {
            int schemeSeparator = source.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator < 0)
            {
                int colon = source.IndexOf(':');
                return $"uri:{uri.Scheme.ToLowerInvariant()}:{source[(colon + 1)..]}";
            }

            int authorityStart = schemeSeparator + 3;
            int suffixStart = source.IndexOfAny(['/', '\\', '?', '#'], authorityStart);
            string rawAuthority = suffixStart < 0 ? source[authorityStart..] : source[authorityStart..suffixStart];
            int userInfoSeparator = rawAuthority.LastIndexOf('@');
            string userInfo = userInfoSeparator < 0 ? "" : rawAuthority[..(userInfoSeparator + 1)];
            string host = uri.HostNameType == UriHostNameType.IPv6
                ? $"[{uri.IdnHost.ToLowerInvariant()}]"
                : uri.IdnHost.ToLowerInvariant();
            string port = uri.IsDefaultPort ? "" : $":{uri.Port}";
            string suffix = suffixStart < 0 ? "/" : source[suffixStart..];
            if (suffix[0] is '?' or '#')
            {
                suffix = "/" + suffix;
            }

            return $"uri:{uri.Scheme.ToLowerInvariant()}://{userInfo}{host}{port}{suffix}";
        }

        string localPath = Uri.TryCreate(source, UriKind.Absolute, out uri) && uri.IsFile
            ? uri.LocalPath
            : source;
        string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localPath, baseDirectory));
        if (isWindows)
        {
            normalizedPath = normalizedPath.ToUpperInvariant();
        }

        return $"path:{normalizedPath}";
    }

    /// <summary>
    /// 入力 file に対応する SDK runfile assets file を探します。
    /// </summary>
    internal static string? FindAssetsFile(string entryPath, string runFileRoot)
    {
        string artifactsDirectory = GetSdkArtifactsDirectory(entryPath, runFileRoot);
        string assetsPath = Path.GetFullPath(Path.Combine(artifactsDirectory, "obj", "project.assets.json"));
        if (!File.Exists(assetsPath))
        {
            return null;
        }

        string expectedProjectPath = Path.GetFullPath(entryPath) + ".csproj";
        StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            string projectPath = document.RootElement.GetProperty("project").GetProperty("restore").GetProperty("projectPath").GetString() ?? "";
            if (string.Equals(Path.GetFullPath(projectPath), expectedProjectPath, pathComparison))
            {
                return assetsPath;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
        }

        return null;
    }

    /// <summary>
    /// SDK runfile のデータ root を取得します。
    /// </summary>
    internal static string GetSdkRunFileRoot(bool isWindows, string tempPath, string localApplicationDataPath)
    {
        string root = isWindows ? tempPath : localApplicationDataPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("The platform did not provide an SDK file-based artifact root.");
        }

        return Path.GetFullPath(Path.Combine(root, "dotnet", "runfile"));
    }

    /// <summary>
    /// Current process platform に対応する SDK runfile root を取得します。
    /// </summary>
    private static string GetCurrentSdkRunFileRoot() => GetSdkRunFileRoot(
        OperatingSystem.IsWindows(),
        Path.GetTempPath(),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// SDK と同じ path casing normalization を適用した runfile artifact directory を返します。
    /// </summary>
    internal static string GetSdkArtifactsDirectory(string entryPath, string? runFileRoot = null)
    {
        string fullEntryPath = Path.GetFullPath(entryPath);
        string fileName = Path.GetFileNameWithoutExtension(fullEntryPath);
        string normalizedPath = fullEntryPath.ToUpperInvariant();
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(runFileRoot ?? GetCurrentSdkRunFileRoot(), $"{fileName}-{hash}");
    }

    /// <summary>
    /// file-based SDK output root 内の workflow assembly path を返します。
    /// </summary>
    internal static string GetSdkOutputAssemblyPath(string entryPath, string runFileRoot)
    {
        string artifactsDirectory = GetSdkArtifactsDirectory(entryPath, runFileRoot);
        return Path.Combine(
            artifactsDirectory,
            "bin",
            "debug",
            Path.GetFileNameWithoutExtension(entryPath) + ".dll");
    }

    /// <summary>
    /// 入力パスに対応する SDK 出力 lock を取得します。
    /// </summary>
    private static FileStream AcquireFileBasedOutputLock(
        string entryPath,
        string runFileRoot,
        CancellationToken cancellationToken,
        Action<string>? outputLockContentionObserver)
    {
        string artifactsDirectory = GetSdkArtifactsDirectory(entryPath, runFileRoot);
        string lockPath = GetFileBasedOutputLockPath(runFileRoot, artifactsDirectory, OperatingSystem.IsWindows());
        string lockRoot = Path.GetDirectoryName(lockPath)!;
        Directory.CreateDirectory(lockRoot);
        DateTime deadline = DateTime.UtcNow.AddMinutes(2);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                outputLockContentionObserver?.Invoke(lockPath);
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// SDK artifact directory と同じ platform path identity に対応する排他 lock path を返します。
    /// </summary>
    internal static string GetFileBasedOutputLockPath(string runFileRoot, string artifactsDirectory, bool isWindows)
    {
        string lockKey = isWindows ? artifactsDirectory.ToUpperInvariant() : artifactsDirectory;
        string lockName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockKey))).ToLowerInvariant() + ".lock";
        return Path.Combine(runFileRoot, ".devo6-locks", lockName);
    }

    /// <summary>
    /// ファイル内容の SHA-256 hash を計算します。
    /// </summary>
    private static string ComputeFileHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// NuGet package version が固定形式か判定します。
    /// </summary>
    private static bool IsExactPackageVersion(string? version)
    {
        return !string.IsNullOrWhiteSpace(version)
            && Regex.IsMatch(version, @"^\d+(?:\.\d+){2,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// ファイルが指定ディレクトリ内にあるか判定します。
    /// </summary>
    private static bool IsInsideDirectory(string path, string directory)
    {
        string normalizedPath = Path.GetFullPath(path);
        string normalizedRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// SDK process の標準エラーと標準出力を整形します。
    /// </summary>
    private static string FormatProcessFailure(FileBasedSdkProcessResult result)
    {
        return string.Join(Environment.NewLine, new[] { result.StandardError.Trim(), result.StandardOutput.Trim() }.Where(value => value.Length > 0));
    }

    private sealed class FileBasedNuGetLock
    {
        /// <summary>
        /// 要求された lock format version を取得します。
        /// </summary>
        public int Version { get; set; }
        /// <summary>
        /// lock が対象とする target framework を取得します。
        /// </summary>
        public string TargetFramework { get; set; } = "";
        /// <summary>
        /// lock が対象とする runtime identifier を取得します。
        /// </summary>
        public string? RuntimeIdentifier { get; set; }
        /// <summary>
        /// lock で package source verification を有効にするか取得します。
        /// </summary>
        public bool VerifyPackageSources { get; set; }
        /// <summary>
        /// lock に記録された package source を取得します。
        /// </summary>
        public List<string> PackageSources { get; set; } = [];
        /// <summary>
        /// 入力から直接指定された package references を取得します。
        /// </summary>
        public List<FileBasedLockedPackage> DirectReferences { get; set; } = [];
        /// <summary>
        /// lock に記録された解決済み依存を取得します。
        /// </summary>
        public List<FileBasedLockedPackage> ResolvedDependencies { get; set; } = [];
    }

    private sealed class FileBasedLockedPackage
    {
        /// <summary>
        /// lock 内の NuGet package identifier を取得します。
        /// </summary>
        public string PackageId { get; set; } = "";
        /// <summary>
        /// 要求された lock format version を取得します。
        /// </summary>
        public string Version { get; set; } = "";
        [YamlDotNet.Serialization.YamlMember(Alias = "direct")]
        /// <summary>
        /// package が入力から直接参照されたか取得します。
        /// </summary>
        public bool Direct { get; set; }
    }
}

/// <summary>
/// file-based SDK が実行した restore / build の結果を表します。
/// </summary>
public sealed record FileBasedCsBuildResult(
    bool Succeeded,
    string ErrorCode,
    string ErrorMessage,
    string? AssemblyPath,
    string? AssetsPath,
    string? SdkVersion)
{
    /// <summary>
    /// SDK build の成功結果を作成します。
    /// </summary>
    internal static FileBasedCsBuildResult Success(string assemblyPath, string assetsPath, string sdkVersion) =>
        new(true, "", "", assemblyPath, assetsPath, sdkVersion);

    /// <summary>
    /// workflow 検証または実行の失敗結果を作成します。
    /// </summary>
    internal static FileBasedCsBuildResult Failure(string code, string message) =>
        new(false, code, message, null, null, null);
}

internal interface IFileBasedSdkRunner
{
    /// <summary>
    /// SDK の file-based restore または build process を実行します。
    /// </summary>
    /// <param name="arguments">SDK process に渡す引数。</param>
    /// <param name="workingDirectory">SDK process の作業ディレクトリ。</param>
    /// <param name="cancellationToken">SDK process の終了を要求する token。</param>
    /// <param name="runContext">要求ごとに分離した SDK 出力・cache context。</param>
    /// <returns>SDK process の終了コードと出力。</returns>
    FileBasedSdkProcessResult Run(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        FileBasedSdkRunContext? runContext = null);
}

/// <summary>
/// 一回の SDK 要求専用の出力先とキャッシュ先を保持します。
/// </summary>
/// <param name="RunFileRoot">SDK の file-based assets と成果物を保存する root。</param>
/// <param name="CliHomeDirectory">要求専用の .NET CLI home。</param>
/// <param name="DataDirectory">要求専用の platform data home。</param>
/// <param name="RuntimeDirectory">要求専用の runtime scratch directory。</param>
/// <param name="TemporaryDirectory">要求専用の temporary directory。</param>
/// <param name="NuGetPackagesDirectory">要求専用の NuGet package cache。</param>
/// <param name="NuGetHttpCacheDirectory">要求専用の NuGet HTTP cache。</param>
internal sealed record FileBasedSdkRunContext(
    string RunFileRoot,
    string CliHomeDirectory,
    string DataDirectory,
    string RuntimeDirectory,
    string TemporaryDirectory,
    string NuGetPackagesDirectory,
    string NuGetHttpCacheDirectory)
{
    /// <summary>
    /// SDK runfile、CLI、NuGet、temporary data が要求間で共有されない path を作成します。
    /// </summary>
    /// <param name="baseRunFileRoot">隔離 directory を置く application-owned root。</param>
    /// <returns>今回だけ使用する SDK process context。</returns>
    internal static FileBasedSdkRunContext Create(string baseRunFileRoot)
    {
        string sessionDirectory = Path.Combine(Path.GetFullPath(baseRunFileRoot), $"devo6-sdk-{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(sessionDirectory);
        }
        else
        {
            Directory.CreateDirectory(
                sessionDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string cliHomeDirectory = Path.Combine(sessionDirectory, "cli");
        string dataDirectory = Path.Combine(sessionDirectory, "data");
        string runtimeDirectory = Path.Combine(sessionDirectory, "runtime");
        string temporaryDirectory = Path.Combine(sessionDirectory, "temp");
        string nugetDirectory = Path.Combine(sessionDirectory, "nuget");
        string nugetPackagesDirectory = Path.Combine(nugetDirectory, "packages");
        string nugetHttpCacheDirectory = Path.Combine(nugetDirectory, "http-cache");
        string runFileRoot = OperatingSystem.IsWindows()
            ? Path.Combine(temporaryDirectory, "dotnet", "runfile")
            : Path.Combine(dataDirectory, "dotnet", "runfile");

        foreach (string directory in new[]
        {
            cliHomeDirectory,
            dataDirectory,
            runtimeDirectory,
            temporaryDirectory,
            nugetPackagesDirectory,
            nugetHttpCacheDirectory,
        })
        {
            Directory.CreateDirectory(directory);
        }

        return new FileBasedSdkRunContext(
            runFileRoot,
            cliHomeDirectory,
            dataDirectory,
            runtimeDirectory,
            temporaryDirectory,
            nugetPackagesDirectory,
            nugetHttpCacheDirectory);
    }

    /// <summary>
    /// SDK 子 process に要求専用のデータ領域を適用します。
    /// </summary>
    /// <param name="environment">起動する process の環境変数。</param>
    internal void ApplyTo(IDictionary<string, string?> environment)
    {
        environment["DOTNET_CLI_HOME"] = CliHomeDirectory;
        environment["NUGET_PACKAGES"] = NuGetPackagesDirectory;
        environment["NUGET_HTTP_CACHE_PATH"] = NuGetHttpCacheDirectory;
        if (OperatingSystem.IsWindows())
        {
            environment["TEMP"] = TemporaryDirectory;
            environment["TMP"] = TemporaryDirectory;
        }
        else
        {
            environment["XDG_DATA_HOME"] = DataDirectory;
            environment["XDG_RUNTIME_DIR"] = RuntimeDirectory;
            environment["TMPDIR"] = TemporaryDirectory;
        }
    }
}

/// <summary>
/// SDK process の実行結果を保持します。
/// </summary>
internal sealed record FileBasedSdkProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// SDK process が成功したか取得します。
    /// </summary>
    internal bool Succeeded => ExitCode == 0;
}

internal sealed class DotnetFileBasedSdkRunner : IFileBasedSdkRunner
{
    private readonly string? executablePath;

    /// <summary>
    /// SDK process host を指定して runner を作成します。
    /// </summary>
    internal DotnetFileBasedSdkRunner(string? executablePath = null)
    {
        this.executablePath = executablePath;
    }

    /// <summary>
    /// SDK の file-based restore または build process を実行します。
    /// </summary>
    /// <param name="arguments">SDK process に渡す引数。</param>
    /// <param name="workingDirectory">SDK process の作業ディレクトリ。</param>
    /// <param name="cancellationToken">SDK process の終了を要求する token。</param>
    /// <param name="runContext">要求ごとに分離した SDK 出力・cache context。</param>
    /// <returns>SDK process の終了コードと標準出力・標準エラー。</returns>
    public FileBasedSdkProcessResult Run(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        FileBasedSdkRunContext? runContext = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        runContext?.ApplyTo(startInfo.Environment);
        if (arguments.Count == 1 && arguments[0] == "--info")
        {
            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        }
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet SDK process.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Exception? killFailure = null;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }
            catch (Exception exception)
            {
                killFailure = exception;
            }

            // Keep the caller's output lock until the SDK process and inherited output pipes have closed.
            process.WaitForExitAsync().GetAwaiter().GetResult();
            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            if (killFailure is not null)
            {
                throw new InvalidOperationException("The cancelled SDK process tree could not be stopped safely.", killFailure);
            }

            throw;
        }

        Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        return new FileBasedSdkProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
#endif
