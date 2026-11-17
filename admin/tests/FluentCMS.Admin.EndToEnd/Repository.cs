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

    public static IReadOnlyDictionary<string, string> AdminTexts { get; } = ReadTexts(Path.Combine(Root, "admin", "src", "FluentCMS.Admin", "Localization", "AdminResources.resx"));

    public static string AdminText(string key) =>
        AdminTexts.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"AdminResources.resx has no key {key}.");

    public static IReadOnlyDictionary<string, string> ComponentTexts { get; } = ReadTexts(Path.Combine(Root, "admin", "src", "FluentCMS.Admin.Components", "Resources", "CmsStrings.resx"));

    public static string ComponentText(string key) =>
        ComponentTexts.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"CmsStrings.resx has no key {key}.");

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
