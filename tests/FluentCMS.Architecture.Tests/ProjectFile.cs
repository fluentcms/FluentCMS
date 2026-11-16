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
    IReadOnlyList<AnalyzerReference> AnalyzerReferences,
    string Location)
{
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private static readonly Lazy<IReadOnlyList<ProjectFile>> sourceProjects = new(() => LoadProjects("src", ["bin", "obj"]));

    private static readonly Lazy<IReadOnlyList<ProjectFile>> repositoryProjects = new(() => LoadProjects(".", ["bin", "obj", "node_modules", "tests"]));

    public static IReadOnlyList<ProjectFile> SourceProjects => sourceProjects.Value;

    // Every project of the repository but the tests, wherever it is, so that a rule can find a project by what it references.
    public static IReadOnlyList<ProjectFile> RepositoryProjects => repositoryProjects.Value;

    private static List<ProjectFile> LoadProjects(string folder, string[] skipped)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, folder);

        return Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path)
                .Split(Separators)
                .Any(part => skipped.Contains(part) || part.StartsWith('.')))
            .Select(path => Parse(XDocument.Load(path), Path.GetRelativePath(root, path).Replace('\\', '/')))
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToList();
    }

    public static ProjectFile Load(string relativePath) =>
        Parse(XDocument.Load(Path.Combine(FindRepositoryRoot(), relativePath)), relativePath);

    // A project that is not on disk, for the case that breaks a rule.
    public static ProjectFile FromXml(string location, string xml) => Parse(XDocument.Parse(xml), location);

    private static ProjectFile Parse(XDocument project, string location)
    {
        var items = project.Descendants().ToList();

        var references = items
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (Name: Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')), AnalyzerOnly: IsAnalyzerOnly(e), Private: IsDevelopmentOnly(e)))
            .ToList();

        var packageReferences = items
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => new PackageDependency((string)e.Attribute("Include")!, IsDevelopmentOnly(e)))
            .ToList();

        return new ProjectFile(
            Path.GetFileNameWithoutExtension(location),
            [.. references.Where(reference => !reference.AnalyzerOnly).Select(reference => reference.Name)],
            packageReferences,
            [.. references.Where(reference => reference.AnalyzerOnly).Select(reference => new AnalyzerReference(reference.Name, reference.Private))],
            location);
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

    public static string FindRepositoryRoot()
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
