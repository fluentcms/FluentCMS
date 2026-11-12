using System.Xml.Linq;

namespace FluentCMS.Architecture.Tests;

internal sealed record PackageDependency(string Name, bool DevelopmentOnly);

internal sealed record AnalyzerReference(string Name, bool Private);

// Declared references are read from the project files because the compiler drops references that
// no code uses, so a stub assembly shows none of its project references in its metadata. A project referenced only as an
// analyzer, such as FluentCMS.Analyzers, runs in the build and is not a reference of the assembly.
internal sealed record ProjectFile(
    string Name,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<PackageDependency> PackageReferences,
    IReadOnlyList<AnalyzerReference> AnalyzerReferences)
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

    public static ProjectFile Load(string relativePath) => Parse(Path.Combine(FindRepositoryRoot(), relativePath));

    private static ProjectFile Parse(string path)
    {
        var items = XDocument.Load(path).Descendants().ToList();

        var references = items
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (Name: Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')), AnalyzerOnly: IsAnalyzerOnly(e), Private: IsDevelopmentOnly(e)))
            .ToList();

        var packageReferences = items
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => new PackageDependency((string)e.Attribute("Include")!, IsDevelopmentOnly(e)))
            .ToList();

        return new ProjectFile(
            Path.GetFileNameWithoutExtension(path),
            [.. references.Where(reference => !reference.AnalyzerOnly).Select(reference => reference.Name)],
            packageReferences,
            [.. references.Where(reference => reference.AnalyzerOnly).Select(reference => new AnalyzerReference(reference.Name, reference.Private))]);
    }

    private static bool IsAnalyzerOnly(XElement projectReference) =>
        string.Equals((string?)projectReference.Attribute("OutputItemType"), "Analyzer", StringComparison.OrdinalIgnoreCase)
        && string.Equals((string?)projectReference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase);

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
