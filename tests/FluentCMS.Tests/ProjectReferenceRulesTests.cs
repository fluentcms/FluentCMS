using System.Xml.Linq;

namespace FluentCMS.Tests;

// Enforces the dependency direction from ADR-0004 by reading the project files, so a forbidden reference fails the build's tests.
public sealed class ProjectReferenceRulesTests
{
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["FluentCMS.Core"] = [],
        ["FluentCMS.Infrastructure"] = ["FluentCMS.Core"],
        ["FluentCMS.Mcp"] = ["FluentCMS.Core"],
        ["FluentCMS.Api"] = ["FluentCMS.Core"],
        ["FluentCMS.Admin"] = ["FluentCMS.Core"],
        ["FluentCMS.Host"] = ["FluentCMS.Core", "FluentCMS.Infrastructure", "FluentCMS.Mcp", "FluentCMS.Api", "FluentCMS.Admin"],
    };

    [Fact]
    public void Every_project_references_only_what_ADR_0004_permits()
    {
        var sourceDirectory = Path.Combine(FindRepositoryRoot(), "src");
        var projectFiles = Directory.GetFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories);

        Assert.Equal(
            AllowedReferences.Keys.Order(),
            projectFiles.Select(Path.GetFileNameWithoutExtension).Order());

        var violations = new List<string>();

        foreach (var projectFile in projectFiles)
        {
            var project = Path.GetFileNameWithoutExtension(projectFile);
            var references = XDocument.Load(projectFile)
                .Descendants("ProjectReference")
                .Select(reference => ProjectName((string)reference.Attribute("Include")!));

            violations.AddRange(references
                .Where(reference => !AllowedReferences[project].Contains(reference))
                .Select(reference => $"{project} references {reference}"));
        }

        Assert.Empty(violations);
    }

    private static string ProjectName(string include)
    {
        return Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluentCMS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("FluentCMS.slnx was not found above the test directory.");
    }
}
