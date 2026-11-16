using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Tests;

// The pseudo-cultures of the tests (AQ14): every value read from a resource comes out marked, so a visible text that is
// not marked did not come from a resource.
public static class PseudoCulture
{
    public const string LeftToRight = "en-XA";
    public const string RightToLeft = "ar-XB";

    private const char Open = '[';
    private const char Close = ']';
    private const char RightToLeftOverride = '‮';
    private const char PopDirectionalFormatting = '‬';

    private static readonly string[] TextAttributes = ["aria-label", "aria-description", "title", "placeholder", "alt"];

    public static string Transform(string value, bool rightToLeft) =>
        rightToLeft
            ? $"{Open}{RightToLeftOverride}{value}{PopDirectionalFormatting}{Close}"
            : $"{Open}{Accent(value)}{Close}";

    public static bool IsMarked(string text) => text.Length >= 2 && text[0] == Open && text[^1] == Close;

    // The visible texts and the text attributes of the markup that are not marked, data aside.
    public static IReadOnlyList<string> Unlocalized(INodeList nodes)
    {
        var found = new List<string>();
        foreach (var node in nodes)
        {
            Collect(node, found);
        }

        return found;
    }

    public static int TextCount(INodeList nodes) =>
        nodes.Sum(node => node.GetDescendantsAndSelf().OfType<IText>().Count(text => text.Data.Trim().Length > 0));

    private static void Collect(INode node, List<string> found)
    {
        if (node is IText text)
        {
            var value = text.Data.Trim();
            if (value.Length > 0 && !IsMarked(value))
            {
                found.Add(value);
            }
        }

        if (node is IElement element)
        {
            foreach (var name in TextAttributes)
            {
                var value = element.GetAttribute(name)?.Trim();
                if (!string.IsNullOrEmpty(value) && !IsMarked(value))
                {
                    found.Add($"{name}=\"{value}\"");
                }
            }
        }

        foreach (var child in node.ChildNodes)
        {
            Collect(child, found);
        }
    }

    // Accents the letters, leaving format items such as {0} as they are.
    private static string Accent(string value)
    {
        var builder = new StringBuilder(value.Length);
        var depth = 0;
        foreach (var c in value)
        {
            depth += c switch { '{' => 1, '}' => -1, _ => 0 };
            builder.Append(depth > 0 ? c : c switch
            {
                'a' => 'à', 'e' => 'é', 'i' => 'î', 'o' => 'ö', 'u' => 'ü', 'c' => 'ç', 'n' => 'ñ', 's' => 'š', 'y' => 'ý',
                'A' => 'À', 'E' => 'É', 'I' => 'Î', 'O' => 'Ö', 'U' => 'Ü', 'C' => 'Ç', 'N' => 'Ñ', 'S' => 'Š', 'Y' => 'Ý',
                _ => c,
            });
        }

        return builder.ToString();
    }
}

internal sealed class PseudoStringLocalizerFactory(IStringLocalizerFactory inner, CultureInfo culture) : IStringLocalizerFactory
{
    public IStringLocalizer Create(Type resourceSource) => new PseudoStringLocalizer(inner.Create(resourceSource), culture);

    public IStringLocalizer Create(string baseName, string location) => new PseudoStringLocalizer(inner.Create(baseName, location), culture);
}

// A key missing from the resources comes out unmarked, so the check also fails on it.
internal sealed class PseudoStringLocalizer(IStringLocalizer inner, CultureInfo culture) : IStringLocalizer
{
    public LocalizedString this[string name] => Mark(inner[name]);

    public LocalizedString this[string name, params object[] arguments] => Mark(inner[name, arguments]);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => inner.GetAllStrings(includeParentCultures).Select(Mark);

    private LocalizedString Mark(LocalizedString value) =>
        value.ResourceNotFound
            ? value
            : new LocalizedString(value.Name, PseudoCulture.Transform(value.Value, culture.TextInfo.IsRightToLeft), false, value.SearchedLocation);
}
