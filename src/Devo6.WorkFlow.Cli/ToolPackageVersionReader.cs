using System.Xml.Linq;

namespace Devo6.WorkFlow.Cli;

/// <summary>
/// Install 済み .NET tool package の nuspec metadata から NuGet package version を読み取ります。
/// </summary>
internal static class ToolPackageVersionReader
{
    private const string ExpectedPackageId = "Devo6.WorkFlow.Cli";

    /// <summary>
    /// CLI の配布 NuGet package version を nuspec metadata から読み取ります。
    /// </summary>
    internal static string? ReadInstalledPackageVersion(string applicationBaseDirectory)
    {
        DirectoryInfo? currentDirectory = new(Path.GetFullPath(applicationBaseDirectory));
        while (currentDirectory is not null)
        {
            foreach (string nuspecPath in Directory.EnumerateFiles(currentDirectory.FullName, "*.nuspec", SearchOption.TopDirectoryOnly))
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
                catch (Exception exception) when (exception is IOException or System.Xml.XmlException or UnauthorizedAccessException)
                {
                }
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }
}
