// The prebuilt static build of the admin (admin spec 6.3), fluentcms-admin-{version}.zip, which the Publish workflow attaches to
// the GitHub release and the Pack job of CI makes on every full run. pack writes the wwwroot of a publish of FluentCMS.Admin.App,
// which holds the sample config.json and the licence and the third-party notices, with a README that points to the hosting
// guide, and checks the zip; check checks a zip alone. The check names each file the zip misses, each compressed copy of
// index.html or config.json it holds, which a server that prefers a precompressed copy would serve in place of the edited
// file, each entry it holds twice and each resource of a pseudo-culture, which no release carries, and exits with 1.
//
//   dotnet run eng/admin-zip.cs -- pack <publish folder of FluentCMS.Admin.App> <version> <output folder>
//   dotnet run eng/admin-zip.cs -- check <zip>

// A file-based app publishes with Native AOT by default, which would restore the ILCompiler package for nothing.
#:property PublishAot=false

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

const string Guide = "https://github.com/fluentcms/FluentCMS/blob/main/docs/admin-hosting.md";

switch (args)
{
    case ["pack", var publish, var version, var output] when Directory.Exists(publish):
        var root = Directory.Exists(Path.Combine(publish, "wwwroot")) ? Path.Combine(publish, "wwwroot") : publish;
        Directory.CreateDirectory(output);
        var written = Path.Combine(output, $"fluentcms-admin-{version}.zip");
        File.Delete(written);
        using (var archive = ZipFile.Open(written, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                archive.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.SmallestSize);
            }

            using var readme = new StreamWriter(archive.CreateEntry(StaticBuild.Readme).Open());
            readme.Write($"""
                FluentCMS admin {version}, the prebuilt static build

                The admin's files for any static host or CDN. Before serving them, set apiBaseUrl in config.json to the
                API host, and the href of <base> in index.html to the path the admin is served from, such as /admin/.
                The host answers index.html for every path that is no file, never caches index.html and config.json,
                and sends the page's headers; the API host lists the admin's origin and its redirect URIs. The hosting
                guide says how, with recipes for nginx, Caddy, Azure Static Web Apps, Amazon S3 with CloudFront and
                Cloudflare Pages: {Guide}

                LICENSE is the licence of FluentCMS, and THIRD-PARTY-NOTICES.md the notices of the code of others it holds.

                """.ReplaceLineEndings("\n"));
        }

        Console.WriteLine($"Wrote {written}.");
        return StaticBuild.Check(written);

    case ["check", var zip] when File.Exists(zip):
        return StaticBuild.Check(zip);

    default:
        Console.Error.WriteLine("usage: dotnet run eng/admin-zip.cs -- pack <publish folder of FluentCMS.Admin.App> <version> <output folder>");
        Console.Error.WriteLine("       dotnet run eng/admin-zip.cs -- check <zip>");
        return 2;
}

internal static partial class StaticBuild
{
    public const string Readme = "README.txt";

    private static readonly string[] Required = ["index.html", "config.json", "_framework/blazor.webassembly.js", "LICENSE", "THIRD-PARTY-NOTICES.md", Readme];

    private static readonly string[] Uncompressed = ["index.html", "config.json"];

    // The pseudo-cultures of admin spec 4.17, as eng/check-admin-size.cs names them.
    private static readonly string[] PseudoCultures = ["en-XA", "ar-XB"];

    private static bool IsPseudoCulture(string name) =>
        PseudoCultures.Any(culture => name.Split('/').Contains(culture, StringComparer.OrdinalIgnoreCase)
            || name.Contains($".{culture}.", StringComparison.OrdinalIgnoreCase));

    public static int Check(string zip)
    {
        var problems = new List<string>();
        using (var archive = ZipFile.OpenRead(zip))
        {
            var files = archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).GroupBy(entry => entry.FullName, StringComparer.Ordinal).ToList();
            foreach (var name in files.Where(group => group.Count() > 1).Select(group => group.Key))
            {
                problems.Add($"holds {name} twice, and an unzip keeps one of them");
            }

            var entries = files.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (var name in entries.Keys.Where(IsPseudoCulture))
            {
                problems.Add($"holds {name}, a resource of a pseudo-culture, which only the end-to-end checks build (FluentCmsAdminPseudoCultures)");
            }

            foreach (var name in Required.Where(name => !entries.ContainsKey(name)))
            {
                problems.Add($"misses {name}");
            }

            foreach (var name in Uncompressed.SelectMany(name => new[] { name + ".br", name + ".gz" }).Where(entries.ContainsKey))
            {
                problems.Add($"holds {name}, a compressed copy that a server could serve in place of the edited file");
            }

            if (entries.TryGetValue("index.html", out var index) && BaseElement().Count(Read(index)) is var bases and not 1)
            {
                problems.Add($"has {bases} <base href> elements in index.html, where the operator edits exactly one");
            }

            if (entries.TryGetValue("config.json", out var configuration) && !IsSample(Read(configuration)))
            {
                problems.Add("has a config.json that is not the sample, a JSON object with apiBaseUrl");
            }

            foreach (var name in entries.Keys.Where(name => name.Contains('\\', StringComparison.Ordinal) || name.StartsWith('/') || name.StartsWith("wwwroot/", StringComparison.Ordinal)))
            {
                problems.Add($"holds {name}, which is not a path from the root of the build");
            }

            foreach (var name in Required.Where(name => entries.TryGetValue(name, out var entry) && entry.Length == 0))
            {
                problems.Add($"has an empty {name}");
            }
        }

        var file = Path.GetFileName(zip);
        if (problems.Count == 0)
        {
            Console.WriteLine($"ok    {file} holds the static build: {string.Join(", ", Required)}, and no compressed copy of {string.Join(" or ", Uncompressed)}.");
            return 0;
        }

        foreach (var problem in problems)
        {
            var message = $"{file} {problem}.";
            Console.WriteLine(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? $"::error::{message}" : $"FAIL  {message}");
        }

        return 1;
    }

    private static string Read(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static bool IsSample(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("apiBaseUrl", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The rule of the hosting package, which sets the href of the one <base> element.
    [GeneratedRegex("""<base\s+href\s*=\s*"[^"]*"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex BaseElement();
}
