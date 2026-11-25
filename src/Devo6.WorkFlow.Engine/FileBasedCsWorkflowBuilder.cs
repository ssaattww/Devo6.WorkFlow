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
        "TargetFramework,TargetFrameworks,OutputType,PublishAot,RestoreSources,RestoreAdditionalProjectSources,RestoreConfigFile,RestorePackagesWithLockFile,RestoreLockedMode,ManagePackageVersionsCentrally,MSBuildSDKsPath,MSBuildExtensionsPath";
    private static readonly IDeserializer LockDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
    private readonly IFileBasedSdkRunner sdkRunner;
    private readonly Func<string> sdkRunFileRootProvider;
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
    internal FileBasedCsWorkflowBuilder(
        IFileBasedSdkRunner sdkRunner,
        Func<string>? sdkRunFileRootProvider,
        Action<string>? outputLockContentionObserver = null)
    {
        this.sdkRunner = sdkRunner;
        this.sdkRunFileRootProvider = sdkRunFileRootProvider ?? GetCurrentSdkRunFileRoot;
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
                    assemblyPath, providerTypeName, qualifiedEntryName, options);
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
            FileBasedSdkProcessResult sdkVersionResult;
            try
            {
                sdkVersionResult = sdkRunner.Run(["--version"], workingDirectory, cancellationToken);
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
                sdkInfoResult = sdkRunner.Run(["--info"], workingDirectory, cancellationToken);
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
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return FileBasedCsBuildResult.Failure(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, exception.Message);
            }

            string evaluationError = "";
            if (!evaluationResult.Succeeded
                || !TryValidateEffectiveProject(evaluationResult.StandardOutput, preflight, lockFile!, expectedSdkSdksPath, out evaluationError))
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
                    cancellationToken);
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

            string? assetsPath = FindAssetsFile(fullEntryPath, runFileRoot);
            string assetsError = "";
            if (assetsPath is null || !TryValidateAssets(assetsPath, preflight, lockFile!, out assetsError))
            {
                return FileBasedCsBuildResult.Failure(
                    WorkflowErrorCodes.ScriptNugetLockMismatch,
                    assetsPath is null ? "The SDK did not produce the expected project.assets.json." : assetsError);
            }

            if (!string.Equals(sourceHash, ComputeFileHash(fullEntryPath), StringComparison.Ordinal))
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
                    cancellationToken);
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

            string assemblyPath = GetSdkOutputAssemblyPath(fullEntryPath, runFileRoot);
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
            if (!StringSetEquals(configuredSources, parsedLock.PackageSources))
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
            if (!StringSetEquals(restoredSources, lockFile.PackageSources))
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
        out string error)
    {
        return ValidateEffectiveProjectJson(
            evaluationJson,
            preflight.PackageReferences,
            lockFile.PackageSources,
            expectedSdkSdksPath,
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
                if (!StringSetEquals(sources, expectedSources))
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
    /// 文字列集合を大文字小文字を区別せず比較します。
    /// </summary>
    private static bool StringSetEquals(IEnumerable<string> left, IEnumerable<string> right)
    {
        return new HashSet<string>(left, StringComparer.OrdinalIgnoreCase)
            .SetEquals(right);
    }

    /// <summary>
    /// 入力 file に対応する SDK runfile assets file を探します。
    /// </summary>
    internal static string? FindAssetsFile(string entryPath, string runFileRoot)
    {
        string artifactsDirectory = GetSdkArtifactsDirectory(entryPath, runFileRoot);
        if (!Directory.Exists(artifactsDirectory))
        {
            return null;
        }

        string expectedProjectPath = Path.GetFullPath(entryPath) + ".csproj";
        StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (string candidate in Directory.EnumerateFiles(artifactsDirectory, "project.assets.json", SearchOption.AllDirectories))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(candidate));
                string projectPath = document.RootElement.GetProperty("project").GetProperty("restore").GetProperty("projectPath").GetString() ?? "";
                if (string.Equals(Path.GetFullPath(projectPath), expectedProjectPath, pathComparison))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
            }
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
    FileBasedSdkProcessResult Run(IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken);
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
    public FileBasedSdkProcessResult Run(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
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
