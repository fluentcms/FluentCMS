using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

// The test of the keys (spec 7): each culture's resource file of each admin assembly has every key of its neutral file, no key
// the neutral file lacks, and the placeholders of each of its texts. The gallery's texts are no part of a translation.
public sealed class ResourceKeysTests
{
    private static readonly string Sources = Path.Combine(FindRoot(), "admin", "src");

    private static readonly HashSet<string> LeftOut = new(StringComparer.Ordinal) { "GalleryResources" };

    [Fact]
    public void EveryCultureOfEveryAssemblyHasTheKeysAndPlaceholdersOfItsNeutralFile()
    {
        var differences = new List<string>();
        foreach (var neutral in NeutralFiles())
        {
            var name = Path.GetFileNameWithoutExtension(neutral);
            foreach (var translated in Directory.EnumerateFiles(Path.GetDirectoryName(neutral)!, $"{name}.*.resx"))
            {
                var culture = Path.GetFileNameWithoutExtension(translated)[(name.Length + 1)..];
                differences.AddRange(ResourceKeyCheck.Differences(Relative(neutral), Texts(XDocument.Load(neutral)), culture, Texts(XDocument.Load(translated))));
            }
        }

        differences.ShouldBeEmpty();
    }

    // With English alone the test above compares nothing, so it is proven on the pseudo-cultures, written as the build writes them.
    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void ThePseudoCulturesHaveTheKeysAndPlaceholdersOfEveryNeutralFile(string culture)
    {
        var files = NeutralFiles().ToList();
        var differences = files.SelectMany(file =>
        {
            var neutral = XDocument.Load(file);
            return ResourceKeyCheck.Differences(Relative(file), Texts(neutral), culture, Texts(PseudoCulture.Resources(neutral, culture)));
        });

        files.Count.ShouldBeGreaterThan(15);
        differences.ShouldBeEmpty();
    }

    [Fact]
    public void TheCheckNamesAKeyACultureLacks()
    {
        ResourceKeyCheck.Differences("A.resx", Map(("Shell.Save", "Save"), ("Shell.Close", "Close")), "fr", Map(("Shell.Save", "Enregistrer")))
            .ShouldBe(["A.fr.resx lacks Shell.Close, which A.resx has"]);
    }

    [Fact]
    public void TheCheckNamesAKeyTheNeutralFileLacks()
    {
        ResourceKeyCheck.Differences("A.resx", Map(("Shell.Save", "Save")), "fr", Map(("Shell.Save", "Enregistrer"), ("Shell.Gone", "Parti")))
            .ShouldBe(["A.fr.resx has Shell.Gone, which A.resx lacks"]);
    }

    [Fact]
    public void TheCheckNamesATextWhosePlaceholdersDiffer()
    {
        ResourceKeyCheck.Differences(
                "A.resx",
                Map(("Content.List.Count", "{0} of {1} entries"), ("Content.List.Escaped", "{{0}} is shown as is"), ("Content.List.Format", "Saved at {0:t}")),
                "fr",
                Map(("Content.List.Count", "{0} entrées"), ("Content.List.Escaped", "{{0}} est montré tel quel"), ("Content.List.Format", "Enregistré à {0}")))
            .ShouldBe(["Content.List.Count of A.fr.resx has the placeholders {0}, and A.resx has {0}, {1}"]);
    }

    private static IEnumerable<string> NeutralFiles() =>
        Directory.EnumerateFiles(Sources, "*.resx", SearchOption.AllDirectories)
            .Where(file => !Path.GetFileNameWithoutExtension(file).Contains('.', StringComparison.Ordinal))
            .Where(file => !LeftOut.Contains(Path.GetFileNameWithoutExtension(file)))
            .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .Order(StringComparer.Ordinal);

    private static string Relative(string file) => Path.GetRelativePath(Sources, file).Replace('\\', '/');

    private static Dictionary<string, string> Texts(XDocument resources) =>
        resources.Root!.Elements("data").Where(PseudoCulture.IsText)
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? "", StringComparer.Ordinal);

    private static Dictionary<string, string> Map(params (string Key, string Text)[] texts) =>
        texts.ToDictionary(text => text.Key, text => text.Text, StringComparer.Ordinal);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FluentCMS.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No FluentCMS.slnx above {AppContext.BaseDirectory}; the test of the keys reads the checkout.");
    }
}

internal static partial class ResourceKeyCheck
{
    // One sentence per difference between a culture's texts and the neutral ones of the file named.
    public static IReadOnlyList<string> Differences(string neutralFile, IReadOnlyDictionary<string, string> neutral, string culture, IReadOnlyDictionary<string, string> translated)
    {
        var cultureFile = $"{neutralFile[..^".resx".Length]}.{culture}.resx";
        var differences = new List<string>();
        foreach (var key in neutral.Keys.Where(key => !translated.ContainsKey(key)).Order(StringComparer.Ordinal))
        {
            differences.Add($"{cultureFile} lacks {key}, which {neutralFile} has");
        }

        foreach (var key in translated.Keys.Where(key => !neutral.ContainsKey(key)).Order(StringComparer.Ordinal))
        {
            differences.Add($"{cultureFile} has {key}, which {neutralFile} lacks");
        }

        foreach (var (key, text) in translated.Where(pair => neutral.ContainsKey(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var expected = Placeholders(neutral[key]);
            var found = Placeholders(text);
            if (!expected.SequenceEqual(found))
            {
                differences.Add($"{key} of {cultureFile} has the placeholders {Describe(found)}, and {neutralFile} has {Describe(expected)}");
            }
        }

        return differences;
    }

    // The indexes of a format string's items, an escaped brace aside: a translation may move an item, not drop or add one.
    public static int[] Placeholders(string text) =>
        [.. FormatItem().Matches(text.Replace("{{", "", StringComparison.Ordinal).Replace("}}", "", StringComparison.Ordinal))
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .Order()];

    private static string Describe(int[] placeholders) =>
        placeholders.Length == 0 ? "none" : string.Join(", ", placeholders.Select(index => $"{{{index}}}"));

    [GeneratedRegex(@"\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}")]
    private static partial Regex FormatItem();
}
