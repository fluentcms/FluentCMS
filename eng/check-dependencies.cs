// The check of the Pack job of the Publish workflow, after the pack: every dependency of a NuGet package in the directory on a
// FluentCMS package is a package of the directory too, so that a release pushes no package that cannot be restored from
// nuget.org. It names each such dependency and exits with 1.
//
//   dotnet run eng/check-dependencies.cs -- <directory of the packages>

// A file-based app publishes with Native AOT by default, which would restore the ILCompiler package for nothing.
#:property PublishAot=false

using System.IO.Compression;
using System.Xml.Linq;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("usage: dotnet run eng/check-dependencies.cs -- <directory of the packages>");
    return 2;
}

var packages = Directory.GetFiles(args[0], "*.nupkg").Order(StringComparer.Ordinal).Select(Read).ToList();
if (packages.Count == 0)
{
    Console.Error.WriteLine($"{args[0]} holds no NuGet package.");
    return 1;
}

var ids = packages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
var failed = 0;
foreach (var (id, dependencies) in packages)
{
    var missing = dependencies.Where(dependency => dependency.StartsWith("FluentCMS.", StringComparison.OrdinalIgnoreCase) && !ids.Contains(dependency))
        .Order(StringComparer.Ordinal).ToList();
    if (missing.Count == 0)
    {
        Console.WriteLine($"ok    {id}");
        continue;
    }

    failed++;
    var message = $"{id} depends on {string.Join(", ", missing)}, which the packages do not hold";
    Console.WriteLine(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? $"::error::{message}" : $"FAIL  {message}");
}

Console.WriteLine(failed == 0
    ? $"The {packages.Count} packages depend on no FluentCMS package outside them."
    : $"{failed} of the {packages.Count} packages depend on a FluentCMS package outside them.");
return failed == 0 ? 0 : 1;

// The id of a package and the ids of its dependencies, of every target framework.
static (string Id, List<string> Dependencies) Read(string path)
{
    using var package = ZipFile.OpenRead(path);
    var nuspec = package.Entries.SingleOrDefault(entry => !entry.FullName.Contains('/', StringComparison.Ordinal)
        && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    if (nuspec is null)
    {
        return (Path.GetFileName(path), []);
    }

    using var stream = nuspec.Open();
    var metadata = XDocument.Load(stream).Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "metadata");
    var id = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName == "id")?.Value.Trim() ?? Path.GetFileName(path);
    var dependencies = metadata?.Descendants().Where(element => element.Name.LocalName == "dependency")
        .Select(element => ((string?)element.Attribute("id"))?.Trim() ?? "").Where(dependency => dependency.Length > 0).Distinct().ToList() ?? [];
    return (id, dependencies);
}
