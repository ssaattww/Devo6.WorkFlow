using System.Xml.Linq;
using System.Security;
using System.Text.RegularExpressions;

namespace Devo6.WorkFlow.Cli;

/// <summary>
/// Install 済み .NET tool package の nuspec metadata から NuGet package version を読み取ります。
/// </summary>
internal static class ToolPackageVersionReader
{
    private const string ExpectedPackageId = "Devo6.WorkFlow.Cli";
    private static readonly Regex ExactPackageVersion = new(
        @"^\d+(?:\.\d+){2,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// CLI が配布版として使える一点 NuGet version か判定します。
    /// </summary>
    internal static bool IsExactPackageVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version) && ExactPackageVersion.IsMatch(version);

    /// <summary>
    /// CLI の配布 NuGet package version を nuspec metadata から読み取ります。
    /// </summary>
    internal static string? ReadInstalledPackageVersion(
        string applicationBaseDirectory,
        Func<string, IEnumerable<string>>? enumerateFiles = null)
    {
        try
        {
            enumerateFiles ??= directory => Directory.EnumerateFiles(directory, "*.nuspec", SearchOption.TopDirectoryOnly);
            DirectoryInfo? currentDirectory = new(Path.GetFullPath(applicationBaseDirectory));
            while (currentDirectory is not null)
            {
                IEnumerable<string> nuspecPaths;
                try
                {
                    nuspecPaths = enumerateFiles(currentDirectory.FullName);
                    foreach (string nuspecPath in nuspecPaths)
                    {
                        try
                        {
                            XDocument document = XDocument.Load(nuspecPath, LoadOptions.None);
                            XNamespace documentNamespace = document.Root?.Name.Namespace ?? XNamespace.None;
                            XElement? metadata = document.Root?.Element(documentNamespace + "metadata");
                            string? packageId = metadata?.Element(documentNamespace + "id")?.Value;
                            string? version = metadata?.Element(documentNamespace + "version")?.Value;
                            if (string.Equals(packageId, ExpectedPackageId, StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(version))
                            {
                                return version.Trim();
                            }
                        }
                        catch (Exception exception) when (IsPackageMetadataReadFailure(exception))
                        {
                            return null;
                        }
                    }
                }
                catch (Exception exception) when (IsPackageMetadataReadFailure(exception))
                {
                    return null;
                }

                currentDirectory = currentDirectory.Parent;
            }
        }
        catch (Exception exception) when (IsPackageMetadataReadFailure(exception))
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// package metadata を読めない主な I/O failure か判定します。
    /// </summary>
    private static bool IsPackageMetadataReadFailure(Exception exception) =>
        exception is IOException or System.Xml.XmlException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}
