// The size budget of the Pack job (admin spec 7): the compressed first download of the published admin app, which is the Brotli
// size of every file of its wwwroot folder but the lazy assemblies and the satellite resources, which the boot configuration in
// _framework/dotnet.js names and a first visit in the default culture does not fetch, and the scripts and stylesheets of a lazy
// assembly's own package, _content/{assembly}/, which only its code loads, such as the rich text editor's bundle; the scoped
// styles of such a package count, since the app's stylesheet imports them. A file without its .br is compressed here. The
// total is an upper bound: every other file counts, those of _content that a page fetches only when it needs them too (the
// library's lazy modules, reboot.css, the dark logo). The boot configuration's list of the satellite resources is left out of
// dotnet.js with them, so that a translation does not count, and printed on a line of its own, since every visitor downloads
// it. It prints the total, the files left out and the largest files, and exits with 1 above the budget. A publish with the
// pseudo-cultures of the end-to-end checks (FluentCmsAdminPseudoCultures) fails too, since no release carries them, unless
// --pseudo-cultures says it is measured on purpose.
//
//   dotnet run eng/check-admin-size.cs -- <publish folder of FluentCMS.Admin.App> <budget in bytes> [--pseudo-cultures]

// A file-based app publishes with Native AOT by default, which would restore the ILCompiler package for nothing.
#:property PublishAot=false

using System.IO.Compression;
using System.Text.Json;

var allowPseudoCultures = args.Length == 3 && args[2] == "--pseudo-cultures";
if ((args.Length != 2 && !allowPseudoCultures) || !Directory.Exists(args[0]) || !long.TryParse(args[1], out var budget))
{
    Console.Error.WriteLine("usage: dotnet run eng/check-admin-size.cs -- <publish folder of FluentCMS.Admin.App> <budget in bytes> [--pseudo-cultures]");
    return 2;
}

var root = Directory.Exists(Path.Combine(args[0], "wwwroot")) ? Path.Combine(args[0], "wwwroot") : args[0];
var framework = Path.Combine(root, "_framework");
var boot = Path.Combine(framework, "dotnet.js");
if (!File.Exists(boot))
{
    Console.Error.WriteLine($"{boot} does not exist: {args[0]} is not the published output of a Blazor WebAssembly app.");
    return 1;
}

var notFetched = NotFetched(File.ReadAllText(boot), framework);
if (notFetched is null)
{
    Console.Error.WriteLine($"{boot} holds no boot configuration between /*json-start*/ and /*json-end*/.");
    return 1;
}

string[] pseudoCultures = ["en-XA", "ar-XB"];
var pseudo = SatelliteCultures(File.ReadAllText(boot)).Intersect(pseudoCultures, StringComparer.OrdinalIgnoreCase).ToList();
if (pseudo.Count > 0 && !allowPseudoCultures)
{
    var refused = $"The publish holds the pseudo-cultures {string.Join(" and ", pseudo)}, which only the end-to-end checks build (FluentCmsAdminPseudoCultures); no release carries them.";
    Console.WriteLine(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? $"::error::{refused}" : $"FAIL  {refused}");
    return 1;
}

var published = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
    .Where(file => !file.EndsWith(".br", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
    .ToLookup(file => notFetched.Contains(Path.GetFullPath(file)));
var skipped = published[true].Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
// A translation is its satellite assemblies, and the boot configuration's list of them grows with each: the list is left out
// with them and printed on its own.
var satelliteList = SatelliteListSize(File.ReadAllText(boot));
var files = published[false]
    .Select(file => (Name: Path.GetRelativePath(root, file).Replace('\\', '/'), Size: Compressed(file) - (Path.GetFullPath(file) == Path.GetFullPath(boot) ? satelliteList : 0)))
    .OrderByDescending(file => file.Size)
    .ToList();

var total = files.Sum(file => file.Size);
Console.WriteLine($"The first download of the admin is at most {total:N0} B in Brotli, {files.Count} files; the budget is {budget:N0} B.");
Console.WriteLine($"Left out, as lazy assemblies, satellite resources or the scripts and stylesheets of a lazy assembly's package: {skipped.Count} files.");
Console.WriteLine($"Left out of dotnet.js, the boot configuration's list of satellite resources, which every visitor downloads and each culture grows: {satelliteList:N0} B in Brotli.");
foreach (var name in skipped)
{
    Console.WriteLine($"  {name}");
}

// A name the boot configuration gives whose file is not where it is looked for is counted, so the total errs on the high side.
if (notFetched.Count > skipped.Count)
{
    Console.WriteLine($"Counted although the boot configuration names them as lazy or satellite: {notFetched.Count - skipped.Count} files not found where expected.");
}

Console.WriteLine("The largest files:");
foreach (var (name, size) in files.Take(15))
{
    Console.WriteLine($"  {size,12:N0}  {name}");
}

if (total <= budget)
{
    Console.WriteLine($"ok    {budget - total:N0} B under the budget.");
    return 0;
}

var message = $"The first download of the admin is {total:N0} B in Brotli, {total - budget:N0} B over the budget of {budget:N0} B (admin spec 7).";
Console.WriteLine(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? $"::error::{message}" : $"FAIL  {message}");
return 1;

// What the boot configuration's list of satellite resources adds to dotnet.js in Brotli: the script compressed with it and
// without it, the member and the comma before it cut out of the text.
static long SatelliteListSize(string script)
{
    const string Start = "/*json-start*/";
    var start = script.IndexOf(Start, StringComparison.Ordinal) + Start.Length;
    var end = script.IndexOf("/*json-end*/", start, StringComparison.Ordinal);
    var prefix = System.Text.Encoding.UTF8.GetBytes(script[..start]);
    var bytes = System.Text.Encoding.UTF8.GetBytes(script[start..]);
    var reader = new Utf8JsonReader(bytes.AsSpan(0, System.Text.Encoding.UTF8.GetByteCount(script[start..end])), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
    while (reader.Read())
    {
        if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 2 && reader.ValueTextEquals("satelliteResources"))
        {
            var from = (int)reader.TokenStartIndex;
            reader.Read();
            reader.Skip();
            var to = (int)reader.BytesConsumed;
            var comma = Array.LastIndexOf(bytes, (byte)',', from - 1);
            var cut = comma >= 0 && bytes[(comma + 1)..from].All(b => b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t') ? comma : from;
            return Brotli([.. prefix, .. bytes]) - Brotli([.. prefix, .. bytes[..cut], .. bytes[to..]]);
        }
    }

    return 0;
}

static long Brotli(byte[] content)
{
    using var output = new MemoryStream();
    using (var compressor = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
    {
        compressor.Write(content);
    }

    return output.Length;
}

// The cultures the boot configuration names satellite resources of.
static List<string> SatelliteCultures(string script)
{
    const string Start = "/*json-start*/";
    const string End = "/*json-end*/";
    var start = script.IndexOf(Start, StringComparison.Ordinal);
    var end = script.IndexOf(End, StringComparison.Ordinal);
    using var config = JsonDocument.Parse(script[(start + Start.Length)..end]);
    return config.RootElement.TryGetProperty("resources", out var resources) && resources.TryGetProperty("satelliteResources", out var satellites)
        ? [.. satellites.EnumerateObject().Select(culture => culture.Name)]
        : [];
}

// The lazy assemblies, under _framework, with the scripts and stylesheets of their packages, and the satellite resources, under
// _framework/{culture}, of the boot configuration.
static HashSet<string>? NotFetched(string script, string framework)
{
    const string Start = "/*json-start*/";
    const string End = "/*json-end*/";
    var start = script.IndexOf(Start, StringComparison.Ordinal);
    var end = script.IndexOf(End, StringComparison.Ordinal);
    if (start < 0 || end < start)
    {
        return null;
    }

    using var config = JsonDocument.Parse(script[(start + Start.Length)..end]);
    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (!config.RootElement.TryGetProperty("resources", out var resources))
    {
        return paths;
    }

    if (resources.TryGetProperty("lazyAssembly", out var lazy))
    {
        foreach (var assembly in lazy.EnumerateArray())
        {
            var name = assembly.GetProperty("name").GetString()!;
            paths.Add(Path.GetFullPath(Path.Combine(framework, name)));

            // The name is fingerprinted; the virtual path is the assembly's own name, which is its package's.
            var assemblyName = assembly.TryGetProperty("virtualPath", out var virtualPath) ? virtualPath.GetString()! : name;
            var package = Path.Combine(Path.GetDirectoryName(framework)!, "_content", Path.GetFileNameWithoutExtension(assemblyName));
            if (Directory.Exists(package))
            {
                paths.UnionWith(Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories)
                    .Where(file => file.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                        || (file.EndsWith(".css", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".bundle.scp.css", StringComparison.OrdinalIgnoreCase)))
                    .Select(Path.GetFullPath));
            }
        }
    }

    if (resources.TryGetProperty("satelliteResources", out var satellites))
    {
        foreach (var culture in satellites.EnumerateObject())
        {
            foreach (var assembly in culture.Value.EnumerateArray())
            {
                var name = assembly.GetProperty("name").GetString()!;
                paths.Add(Path.GetFullPath(name.Contains('/', StringComparison.Ordinal) ? Path.Combine(framework, name) : Path.Combine(framework, culture.Name, name)));
            }
        }
    }

    return paths;
}

// The .br that the publish wrote beside the file, which is what the host serves, or the file compressed as the publish would.
static long Compressed(string file)
{
    var brotli = file + ".br";
    if (File.Exists(brotli))
    {
        return new FileInfo(brotli).Length;
    }

    using var output = new MemoryStream();
    using (var compressor = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
    using (var input = File.OpenRead(file))
    {
        input.CopyTo(compressor);
    }

    return output.Length;
}
