using System.Xml.Linq;

namespace FluentCMS.Architecture.Tests;

internal sealed record PackageDependency(string Name, bool DevelopmentOnly);

// Declared references are read from the project files because the compiler drops references that
// no code uses, so a stub assembly shows none of its project references in its metadata.
internal sealed record ProjectFile(
    string Name,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<PackageDependency> PackageReferences)
{
    private static readonly Lazy<IReadOnlyList<ProjectFile>> sourceProjects = new(LoadSourceProjects);

    public static IReadOnlyList<ProjectFile> SourceProjects => sourceProjects.Value;

    private static List<ProjectFile> LoadSourceProjects()
    {
        var srcDirectory = Path.Combine(FindRepositoryRoot(), "src");
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        return Directory.EnumerateFiles(srcDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(srcDirectory, path)
                .Split(separators)
                .Any(part => part is "bin" or "obj"))
            .Select(Parse)
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static ProjectFile Parse(string path)
    {
        var items = XDocument.Load(path).Descendants().ToList();

        var projectReferences = items
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')))
            .ToList();

        var packageReferences = items
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => new PackageDependency((string)e.Attribute("Include")!, IsDevelopmentOnly(e)))
            .ToList();

        return new ProjectFile(Path.GetFileNameWithoutExtension(path), projectReferences, packageReferences);
    }

    private static bool IsDevelopmentOnly(XElement packageReference)
    {
        var privateAssets = (string?)packageReference.Attribute("PrivateAssets")
            ?? packageReference.Element("PrivateAssets")?.Value;

        return string.Equals(privateAssets, "all", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FluentCMS.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("FluentCMS.slnx was not found above " + AppContext.BaseDirectory);
    }
}
