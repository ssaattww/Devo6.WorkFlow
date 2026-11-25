using System.Collections;
using Devo6.WorkFlow.Abstractions;
using System.Text.RegularExpressions;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// .NET 10 file-based workflow input に対する restore 前の限定検査を行います。
/// </summary>
internal static class CsFileInputPreflight
{
    private const string RequiredLockFileName = "devo6.nuget.lock.yaml";
    private static readonly Regex PackageDirective = new(
        @"^#:package\s+(?<id>[A-Za-z0-9][A-Za-z0-9._-]*)@\[(?<version>[^\]]+)\]\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ExactPackageVersion = new(
        @"^\d+(?:\.\d+){2,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 現在の Engine asset の target framework を取得します。
    /// </summary>
    internal static string CurrentHostTargetFramework
    {
        get
        {
#if NET10_0
            return ".NETCoreApp,Version=v10.0";
#else
            return ".NETCoreApp,Version=v8.0";
#endif
        }
    }

    /// <summary>
    /// 入力ホスト、file-based directive、および必須 lock file の存在を検査します。
    /// </summary>
    internal static PreflightResult Validate(string entryPath, string? hostTargetFramework)
    {
        return Validate(entryPath, hostTargetFramework, allowedNuGetReferences: null);
    }

    /// <summary>
    /// Input, exact NuGet references, allowed references, and mandatory lock file を restore 前に検査します。
    /// </summary>
    internal static PreflightResult Validate(
        string entryPath,
        string? hostTargetFramework,
        IReadOnlyList<CsxNuGetReference>? allowedNuGetReferences)
    {
        if (!IsNet10(hostTargetFramework))
        {
            return PreflightResult.Failure(
                WorkflowErrorCodes.ScriptFileRequiresNet10Host,
                $"A .NET 10 host is required for file-based C# workflow input. Host target framework: {hostTargetFramework ?? "unknown"}.");
        }

        if (!File.Exists(entryPath))
        {
            return PreflightResult.Failure(
                WorkflowErrorCodes.EntryScriptNotFound,
                $"Entry file was not found: {entryPath}");
        }

        if (!TryValidateDirectives(entryPath, out Dictionary<string, string> packageReferences, out string directiveError))
        {
            return PreflightResult.Failure(WorkflowErrorCodes.ScriptFileDirectiveInvalid, directiveError);
        }

        if (allowedNuGetReferences is { Count: > 0 })
        {
            foreach (KeyValuePair<string, string> reference in packageReferences)
            {
                if (!allowedNuGetReferences.Any(allowed =>
                        string.Equals(allowed.PackageId, reference.Key, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(allowed.Version, reference.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    return PreflightResult.Failure(
                        WorkflowErrorCodes.ScriptReferenceNotAllowed,
                        $"NuGet reference is not allowed: {reference.Key}, {reference.Value}");
                }
            }
        }

        if (!ValidateBuildEnvironment(entryPath, out string environmentError))
        {
            return PreflightResult.Failure(WorkflowErrorCodes.ScriptBuildEnvironmentUnsupported, environmentError);
        }

        string lockPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(entryPath))!, RequiredLockFileName);
        if (!File.Exists(lockPath))
        {
            return PreflightResult.Failure(
                WorkflowErrorCodes.ScriptNugetLockMissing,
                $"NuGet lock file was not found: {lockPath}");
        }

        return PreflightResult.Successful(packageReferences, lockPath);
    }

    /// <summary>
    /// Restore 前に MSBuild response file と環境変数による property override を拒否します。
    /// </summary>
    internal static bool ValidateBuildEnvironment(string entryPath, out string error, IDictionary? environment = null)
    {
        error = "";
        string[] responseFiles = [
            "Directory.Build.rsp", "MSBuild.rsp", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
        ];
        string directory = Path.GetDirectoryName(Path.GetFullPath(entryPath))!;
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            foreach (string fileName in responseFiles)
            {
                string responsePath = Path.Combine(current.FullName, fileName);
                if (File.Exists(responsePath))
                {
                    error = $"Inherited MSBuild configuration is not supported for file-based workflow input: {responsePath}";
                    return false;
                }
            }
        }

        string[] overriddenProperties =
        [
            "TargetFramework", "TargetFrameworks", "OutputType", "PublishAot",
            "RestoreSources", "RestoreAdditionalProjectSources", "RestoreConfigFile",
            "RestorePackagesWithLockFile", "RestoreLockedMode", "ManagePackageVersionsCentrally",
            "MSBuildProjectExtensionsPath", "MSBuildProjectDirectory",
            "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath",
            "CustomBeforeMicrosoftCommonProps", "CustomAfterMicrosoftCommonProps",
            "CustomBeforeMicrosoftCommonTargets", "CustomAfterMicrosoftCommonTargets",
            "CustomBeforeMicrosoftCSharpTargets", "CustomAfterMicrosoftCSharpTargets",
        ];
        HashSet<string> propertyNames = new(overriddenProperties, StringComparer.OrdinalIgnoreCase);
        IDictionary variables = environment ?? Environment.GetEnvironmentVariables();
        foreach (DictionaryEntry variable in variables)
        {
            string name = variable.Key?.ToString() ?? "";
            string value = variable.Value?.ToString() ?? "";
            if (propertyNames.Contains(name) && !string.IsNullOrWhiteSpace(value))
            {
                error = $"MSBuild environment property override is not supported: {name}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// host target framework が .NET 10 か判定します。
    /// </summary>
    private static bool IsNet10(string? targetFramework)
    {
        return string.Equals(targetFramework, "net10.0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(targetFramework, ".NETCoreApp,Version=v10.0", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 許可 directive、固定 package version、Library properties を検証します。
    /// </summary>
    private static bool TryValidateDirectives(
        string entryPath,
        out Dictionary<string, string> packageReferences,
        out string error)
    {
        error = "";
        packageReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string line in File.ReadLines(entryPath))
        {
            string directive = line;
            if (!directive.StartsWith("#:", StringComparison.Ordinal))
            {
                if (directive.TrimStart().StartsWith("#:", StringComparison.Ordinal))
                {
                    error = $"File-based directives must start at the beginning of a line: {line}";
                    return false;
                }

                continue;
            }

            if (directive.StartsWith("#:property", StringComparison.Ordinal))
            {
                string assignment = directive["#:property".Length..].Trim();
                int separator = assignment.IndexOf('=');
                if (separator <= 0 || separator == assignment.Length - 1)
                {
                    error = $"Malformed file-based property directive: {line}";
                    return false;
                }

                string name = assignment[..separator].Trim();
                string value = assignment[(separator + 1)..].Trim();
                if (name is not ("OutputType" or "TargetFramework" or "PublishAot")
                    || !properties.TryAdd(name, value))
                {
                    error = $"Unsupported or duplicate file-based property directive: {line}";
                    return false;
                }

                continue;
            }

            if (directive.StartsWith("#:package", StringComparison.Ordinal))
            {
                Match match = PackageDirective.Match(directive);
                if (!match.Success || !ExactPackageVersion.IsMatch(match.Groups["version"].Value))
                {
                    error = $"A package directive must use an exact version: {line}";
                    return false;
                }

                if (!packageReferences.TryAdd(match.Groups["id"].Value, match.Groups["version"].Value))
                {
                    error = $"A package may be referenced only once: {match.Groups["id"].Value}";
                    return false;
                }

                continue;
            }

            error = $"Unsupported file-based directive: {line}";
            return false;
        }

        foreach ((string name, string value) in new[]
        {
            ("OutputType", "Library"),
            ("TargetFramework", "net10.0"),
            ("PublishAot", "false"),
        })
        {
            if (!properties.TryGetValue(name, out string? actual) || !string.Equals(actual, value, StringComparison.OrdinalIgnoreCase))
            {
                error = $"File-based property must be set to {name}={value}.";
                return false;
            }
        }

        if (!packageReferences.ContainsKey("Devo6.WorkFlow.Engine"))
        {
            error = "A fixed-version Devo6.WorkFlow.Engine package directive is required.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// restore 前入力検査の結果を保持します。
    /// </summary>
    internal sealed record PreflightResult(
        bool Succeeded,
        string ErrorCode,
        string ErrorMessage,
        IReadOnlyDictionary<string, string> PackageReferences,
        string LockFilePath)
    {
        /// <summary>
        /// 前提検査の成功結果を作成します。
        /// </summary>
        internal static PreflightResult Successful(IReadOnlyDictionary<string, string> packageReferences, string lockFilePath) =>
            new(true, "", "", packageReferences, lockFilePath);

        /// <summary>
        /// workflow 検証または実行の失敗結果を作成します。
        /// </summary>
        internal static PreflightResult Failure(string errorCode, string errorMessage) =>
            new(false, errorCode, errorMessage, new Dictionary<string, string>(), "");
    }
}
