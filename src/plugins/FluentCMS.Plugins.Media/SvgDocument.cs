using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FluentCMS.Plugins.Media;

// An SVG read as XML with no DTD, no external resolution, no processing instructions and no comments. A file that is not
// well-formed, has a DTD or whose root is not <svg> is not an SVG. Sanitising is a conservative pass that keeps drawing and
// drops whatever can run or reach out: script and foreignObject, elements of other vocabularies such as XHTML, event
// handler attributes, attributes of other namespaces and xml:base, animations that set a link or a handler, a link that is
// not to a fragment of the document or a PNG, JPEG, GIF or WebP data: URL, and any value that holds a javascript:,
// vbscript: or other data: URL, a url() to anything else, or CSS that imports, evaluates or names an image by a string.
// Values are compared as a browser reads them as CSS, with comments and escapes undone.
internal static class SvgDocument
{
    public static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly XNamespace XLinkNamespace = "http://www.w3.org/1999/xlink";

    private static readonly HashSet<string> ActiveElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "foreignObject", "iframe", "embed", "object", "handler", "listener",
    };

    private static readonly HashSet<string> Animations = new(StringComparer.OrdinalIgnoreCase)
    {
        "animate", "set", "animateMotion", "animateTransform",
    };

    private static readonly string[] SafeDataUrls = ["data:image/png", "data:image/jpeg", "data:image/gif", "data:image/webp"];

    // CSS that reaches out without url(): an import, an expression, and the functions that take an image or a source as a string.
    private static readonly string[] ActiveCss = ["@import", "expression(", "image(", "image-set(", "src("];

    public static XDocument? Parse(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreProcessingInstructions = true,
            IgnoreComments = true,
            MaxCharactersFromEntities = 0,
            CloseInput = false,
        };

        try
        {
            using var reader = XmlReader.Create(stream, settings);
            var document = XDocument.Load(reader);
            return document.Root is { } root && root.Name.LocalName == "svg" && (root.Name.Namespace == SvgNamespace || root.Name.Namespace == XNamespace.None) ? document : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    public static void Sanitize(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var element in document.Descendants().ToList())
        {
            // Inside an element that was already taken out.
            if (element.Document is null)
            {
                continue;
            }

            if (IsActive(element))
            {
                element.Remove();
                continue;
            }

            foreach (var attribute in element.Attributes().ToList())
            {
                if (!attribute.IsNamespaceDeclaration && IsActive(attribute))
                {
                    attribute.Remove();
                }
            }
        }
    }

    public static byte[] Save(XDocument document)
    {
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
        {
            document.Save(writer);
        }

        return output.ToArray();
    }

    private static bool IsActive(XElement element)
    {
        if (element.Name.Namespace != SvgNamespace && element.Name.Namespace != XNamespace.None)
        {
            return true;
        }

        var name = element.Name.LocalName;
        if (ActiveElements.Contains(name))
        {
            return true;
        }

        if (name.Equals("style", StringComparison.OrdinalIgnoreCase) && IsActiveCss(element.Value))
        {
            return true;
        }

        return Animations.Contains(name)
            && element.Attribute("attributeName")?.Value.Trim() is { } target
            && (target.StartsWith("on", StringComparison.OrdinalIgnoreCase) || target.EndsWith("href", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsActive(XAttribute attribute)
    {
        var name = attribute.Name;
        if (name.Namespace != XNamespace.None && name.Namespace != XLinkNamespace && name.Namespace != XNamespace.Xml)
        {
            return true;
        }

        // The base that every reference, a fragment's too, is resolved against.
        if (name == XNamespace.Xml + "base" || name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // An HTML page that inlines the SVG reads the names of attributes in any case.
        if (name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase) && !IsLocalReference(attribute.Value))
        {
            return true;
        }

        return IsActiveValue(attribute.Value) || (name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase) && IsActiveCss(attribute.Value));
    }

    // Nothing, a fragment of the document, or an image in a data: URL.
    private static bool IsLocalReference(string value)
    {
        var compact = Compact(value);
        return compact.Length == 0 || compact[0] == '#' || IsSafeData(compact, 0);
    }

    private static bool IsActiveValue(string value)
    {
        var compact = Compact(value);
        if (compact.Contains("javascript:", StringComparison.Ordinal) || compact.Contains("vbscript:", StringComparison.Ordinal))
        {
            return true;
        }

        for (var data = compact.IndexOf("data:", StringComparison.Ordinal); data >= 0; data = compact.IndexOf("data:", data + 5, StringComparison.Ordinal))
        {
            if (!IsSafeData(compact, data))
            {
                return true;
            }
        }

        // url( takes its target bare or quoted; a data: URL in it was checked above.
        for (var url = compact.IndexOf("url(", StringComparison.Ordinal); url >= 0; url = compact.IndexOf("url(", url + 4, StringComparison.Ordinal))
        {
            var target = compact.AsSpan(url + 4).TrimStart("\"'");
            if (target.IsEmpty || (target[0] != '#' && !target.StartsWith("data:", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActiveCss(string css)
    {
        var compact = Compact(css);
        return ActiveCss.Any(active => compact.Contains(active, StringComparison.Ordinal)) || IsActiveValue(css);
    }

    private static bool IsSafeData(string compact, int at) =>
        SafeDataUrls.Any(safe => compact.AsSpan(at).StartsWith(safe, StringComparison.Ordinal)) && !compact.AsSpan(at).StartsWith("data:image/svg", StringComparison.Ordinal);

    // A value as a browser compares it: CSS comments and escapes undone, as in a style sheet or a presentation attribute,
    // without the white space and control characters that browsers ignore inside a scheme, and in lower case.
    private static string Compact(string value)
    {
        var text = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '/' && index + 1 < value.Length && value[index + 1] == '*')
            {
                var end = value.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                index = end + 1;
            }
            else if (character == '\\' && index + 1 < value.Length)
            {
                index = Unescape(value, index + 1, text);
            }
            else if (character > ' ')
            {
                text.Append(character);
            }
        }

        return text.ToString().ToLowerInvariant();
    }

    // The CSS escape that starts after a backslash: up to six hex digits of a code point, which one white space may end, or
    // any other character as itself. Answers the index of the escape's last character.
    private static int Unescape(string value, int start, StringBuilder text)
    {
        var end = start;
        while (end < value.Length && end - start < 6 && char.IsAsciiHexDigit(value[end]))
        {
            end++;
        }

        if (end == start)
        {
            if (value[start] > ' ')
            {
                text.Append(value[start]);
            }

            return start;
        }

        var code = int.Parse(value.AsSpan(start, end - start), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        text.Append(code is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF) ? "\uFFFD" : char.ConvertFromUtf32(code));
        return end < value.Length && char.IsWhiteSpace(value[end]) ? end : end - 1;
    }
}
