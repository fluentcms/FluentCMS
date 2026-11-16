// The size budget of the Pack job (admin spec 7): the compressed first download of the published admin app, which is the Brotli
// size of every file of its wwwroot folder but the lazy assemblies and the satellite resources, which the boot configuration in
// _framework/dotnet.js names and a first visit in the default culture does not fetch. A file without its .br is compressed here.
// It prints the total and the largest files, and exits with 1 above the budget.
//
//   dotnet run eng/check-admin-size.cs -- <publish folder of FluentCMS.Admin.App> <budget in bytes>

// A file-based app publishes with Native AOT by default, which would restore the ILCompiler package for nothing.
#:property PublishAot=false

using System.IO.Compression;
using System.Text.Json;

if (args.Length != 2 || !Directory.Exists(args[0]) || !long.TryParse(args[1], out var budget))
{
    Console.Error.WriteLine("usage: dotnet run eng/check-admin-size.cs -- <publish folder of FluentCMS.Admin.App> <budget in bytes>");
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

var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
    .Where(file => !file.EndsWith(".br", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
    .Where(file => !notFetched.Contains(Path.GetFullPath(file)))
    .Select(file => (Name: Path.GetRelativePath(root, file).Replace('\\', '/'), Size: Compressed(file)))
    .OrderByDescending(file => file.Size)
    .ToList();

var total = files.Sum(file => file.Size);
Console.WriteLine($"The first download of the admin is {total:N0} B in Brotli, {files.Count} files; the budget is {budget:N0} B.");
Console.WriteLine($"Left out, as a first visit does not fetch them: {notFetched.Count} lazy assemblies and satellite resources.");
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

// The lazy assemblies, under _framework, and the satellite resources, under _framework/{culture}, of the boot configuration.
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
            paths.Add(Path.GetFullPath(Path.Combine(framework, assembly.GetProperty("name").GetString()!)));
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
