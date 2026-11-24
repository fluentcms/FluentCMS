using System.Reflection;
using System.Xml.Linq;

namespace FluentCMS.Admin.EndToEnd;

// The checkout the tests run from: the host's project to publish, and the admin's neutral resources, which the selectors
// take their accessible names from so that a change of wording does not break a test silently.
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    public static string HostProject => Path.Combine(Root, "src", "FluentCMS.Host", "FluentCMS.Host.csproj");

    // The configuration this test assembly was built in, which the host was built in too, as its project reference asks.
    public static string Configuration { get; } =
        typeof(Repository).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

    // Whether this test assembly, and so the host, was built with the pseudo-cultures of the admin (spec 4.17).
    public static bool PseudoCultures { get; } =
        typeof(Repository).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(attribute => attribute is { Key: "FluentCmsAdminPseudoCultures", Value: "true" });

    public static IReadOnlyDictionary<string, string> AdminTexts { get; } = ReadTexts(Path.Combine(Root, "admin", "src", "FluentCMS.Admin", "Localization", "AdminResources.resx"));

    public static string AdminText(string key) =>
        AdminTexts.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"AdminResources.resx has no key {key}.");

    public static IReadOnlyDictionary<string, string> ComponentTexts { get; } = ReadTexts(Path.Combine(Root, "admin", "src", "FluentCMS.Admin.Components", "Resources", "CmsStrings.resx"));

    public static string ComponentText(string key) =>
        ComponentTexts.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"CmsStrings.resx has no key {key}.");

    public static IReadOnlyDictionary<string, string> MediaTexts { get; } = ReadTexts(Path.Combine(Root, "admin", "src", "plugins", "FluentCMS.Plugins.Media.Admin", "MediaResources.resx"));

    public static string MediaText(string key) =>
        MediaTexts.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"MediaResources.resx has no key {key}.");

    // The texts of an area of the administration pages, from the resource file in the area's folder, read once.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> AdministrationTexts = new(StringComparer.Ordinal);

    public static string AdministrationText(string area, string key) =>
        AdministrationTexts.GetOrAdd(area, name => ReadTexts(Path.Combine(Root, "admin", "src", "FluentCMS.Admin.Administration", name, $"{name}Resources.resx")))
            .TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"{area}Resources.resx has no key {key}.");

    // The heading of an area's first page, which is {Area}.Page.Heading of its resources (spec 4.15): what a case knows the
    // page by, beside its address.
    public static string AdministrationHeading(string area) => AdministrationText(area, $"{area}.Page.Heading");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> FileTexts = new(StringComparer.Ordinal);

    // A text of any resource file of the admin, by its path under admin/src, read once.
    public static string Text(string file, string key) =>
        FileTexts.GetOrAdd(file, path => ReadTexts(Path.Combine(Root, "admin", "src", path)))
            .TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"{file} has no key {key}.");

    // Every text of a neutral resource file of the admin whose key ends in Loading: what a page shows while it waits for the API.
    public static IReadOnlyList<string> LoadingTexts { get; } =
        [.. Directory.EnumerateFiles(Path.Combine(Root, "admin", "src"), "*.resx", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Count(c => c == '.') == 1)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(path => ReadTexts(path).Where(text => text.Key.EndsWith("Loading", StringComparison.Ordinal)).Select(text => text.Value))
            .Distinct(StringComparer.Ordinal)];

    public static IReadOnlyDictionary<string, string> RichTextTexts { get; } = ReadTexts(Path.Combine(Root, "admin", "src", "FluentCMS.Admin.RichText", "RichTextResources.resx"));

    public static string RichTextText(string key) =>
        RichTextTexts.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"RichTextResources.resx has no key {key}.");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FluentCMS.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No FluentCMS.slnx above {AppContext.BaseDirectory}; the end-to-end tests run from a checkout.");
    }

    private static Dictionary<string, string> ReadTexts(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!, StringComparer.Ordinal);
}
