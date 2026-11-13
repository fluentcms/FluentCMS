// The check of the Pack job (spec 3.2): every NuGet package of a build is at the version of the build and carries a
// description, its README, its tags (fluentcms-plugin on the plugins, decision 15), the icon, the licence, the repository
// and SourceLink in the PDB of its assembly; @fluentcms/client carries its version, a description, the licence and a README.
// It names each part a package misses and exits with 1.
//
//   dotnet run eng/check-packages.cs -- <directory of the packages> <version>

// A file-based app publishes with Native AOT by default, which would restore the ILCompiler package for nothing.
#:property PublishAot=false

using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length != 2 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("usage: dotnet run eng/check-packages.cs -- <directory of the packages> <version>");
    return 2;
}

var (directory, version) = (args[0], args[1]);
var files = Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToList();
var nuget = files.Where(file => file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)).ToList();
var npm = files.Where(file => file.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)).ToList();
if (nuget.Count == 0)
{
    Console.Error.WriteLine($"{directory} holds no NuGet package.");
    return 1;
}

var failed = 0;
foreach (var (name, problems) in nuget.Select(file => Checks.NuGet(file, version)).Concat(npm.Select(file => Checks.Npm(file, version))))
{
    if (problems.Count == 0)
    {
        Console.WriteLine($"ok    {name} {version}");
        continue;
    }

    failed++;
    var message = $"{name} misses {string.Join("; ", problems)}";
    Console.WriteLine(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? $"::error::{message}" : $"FAIL  {message}");
}

Console.WriteLine(failed == 0
    ? $"The {nuget.Count + npm.Count} packages of {version} have every part."
    : $"{failed} of the {nuget.Count + npm.Count} packages of {version} miss a part.");
return failed == 0 ? 0 : 1;

internal static class Checks
{
    private const string Repository = "https://github.com/fluentcms/FluentCMS";
    private const string PluginTag = "fluentcms-plugin";
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    public static (string Name, List<string> Problems) NuGet(string path, string version)
    {
        var problems = new List<string>();
        using var package = ZipFile.OpenRead(path);
        var nuspec = package.Entries.SingleOrDefault(entry => !entry.FullName.Contains('/', StringComparison.Ordinal)
            && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspec is null)
        {
            return (Path.GetFileName(path), ["its .nuspec"]);
        }

        XElement metadata;
        using (var stream = nuspec.Open())
        {
            metadata = XDocument.Load(stream).Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "metadata") ?? new XElement("metadata");
        }

        XElement? Element(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name);
        string Value(string name) => Element(name)?.Value.Trim() ?? "";
        bool Holds(string file) => package.GetEntry(file) is { Length: > 0 };

        var id = Value("id");
        if (Value("version") != version)
        {
            problems.Add($"the version {version} (it has {Value("version")})");
        }

        if (Value("description") is "" or "Package Description")
        {
            problems.Add("a description");
        }

        if (Value("readme") is not { Length: > 0 } readme || !Holds(readme))
        {
            problems.Add("a README");
        }

        var tags = Value("tags").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var plugin = id.StartsWith("FluentCMS.Plugins.", StringComparison.Ordinal) && id != "FluentCMS.Plugins.Abstractions";
        if (tags.Length == 0)
        {
            problems.Add("its tags");
        }
        else if (plugin != tags.Contains(PluginTag, StringComparer.Ordinal))
        {
            problems.Add(plugin ? $"the tag {PluginTag}" : $"no tag {PluginTag}, which is for plugins");
        }

        if (Value("icon") is not { Length: > 0 } icon || !Holds(icon))
        {
            problems.Add("the icon");
        }

        if (Element("license") is not { } license || (string?)license.Attribute("type") != "expression" || license.Value.Trim() != "MIT")
        {
            problems.Add("the licence expression MIT");
        }

        var repository = Element("repository");
        var commit = (string?)repository?.Attribute("commit") ?? "";
        if (!string.Equals((string?)repository?.Attribute("url"), Repository, StringComparison.OrdinalIgnoreCase) || commit.Length != 40)
        {
            problems.Add($"the repository {Repository} with its commit");
        }

        // The package's own assembly, with its PDB embedded or in the symbols package beside it.
        var assemblies = package.Entries.Where(entry => entry.Name.Equals($"{id}.dll", StringComparison.OrdinalIgnoreCase)).ToList();
        var symbols = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".snupkg");
        foreach (var assembly in assemblies)
        {
            if (SourceLinkOf(assembly, symbols) is not { } sourceLink)
            {
                problems.Add($"SourceLink in the PDB of {assembly.FullName}");
            }
            else if (!sourceLink.Contains($"https://raw.githubusercontent.com/fluentcms/FluentCMS/{commit}/", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"SourceLink to the commit {commit} in the PDB of {assembly.FullName}");
            }
        }

        // Templates is the one package of content only.
        if (assemblies.Count == 0 && id != "FluentCMS.Templates")
        {
            problems.Add($"its assembly {id}.dll");
        }

        return (id.Length > 0 ? id : Path.GetFileName(path), problems);
    }

    public static (string Name, List<string> Problems) Npm(string path, string version)
    {
        var problems = new List<string>();
        string? name = null;
        var readme = false;
        using (var file = File.OpenRead(path))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var tar = new TarReader(gzip))
        {
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.Name == "package/package.json" && entry.DataStream is { } data)
                {
                    using var manifest = JsonDocument.Parse(data);
                    string Text(string property) =>
                        manifest.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

                    name = Text("name");
                    if (Text("version") != version)
                    {
                        problems.Add($"the version {version} (it has {Text("version")})");
                    }

                    if (Text("description").Length == 0)
                    {
                        problems.Add("a description");
                    }

                    if (Text("license") != "MIT")
                    {
                        problems.Add("the licence MIT");
                    }
                }
                else if (entry.Name.Equals("package/README.md", StringComparison.OrdinalIgnoreCase) && entry.Length > 0)
                {
                    readme = true;
                }
            }
        }

        if (name is null)
        {
            problems.Add("its package.json");
        }

        if (!readme)
        {
            problems.Add("a README");
        }

        return (name is { Length: > 0 } ? name : Path.GetFileName(path), problems);
    }

    private static string? SourceLinkOf(ZipArchiveEntry assembly, string symbols)
    {
        using var image = Copy(assembly);
        using var pe = new PEReader(image);
        var embedded = pe.ReadDebugDirectory().FirstOrDefault(entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        if (embedded.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
        {
            using var provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded);
            return SourceLinkOf(provider.GetMetadataReader());
        }

        if (!File.Exists(symbols))
        {
            return null;
        }

        using var symbolPackage = ZipFile.OpenRead(symbols);
        if (symbolPackage.GetEntry(Path.ChangeExtension(assembly.FullName, ".pdb")) is not { } pdb)
        {
            return null;
        }

        using var stream = Copy(pdb);
        using var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(stream);
        return SourceLinkOf(pdbProvider.GetMetadataReader());
    }

    private static string? SourceLinkOf(MetadataReader reader)
    {
        foreach (var handle in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var information = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(information.Kind) == SourceLinkKind)
            {
                return Encoding.UTF8.GetString(reader.GetBlobBytes(information.Value));
            }
        }

        return null;
    }

    // PEReader and the PDB reader need a stream they can seek in.
    private static MemoryStream Copy(ZipArchiveEntry entry)
    {
        var copy = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(copy);
        }

        copy.Position = 0;
        return copy;
    }
}
