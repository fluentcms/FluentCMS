using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FluentCMS.Plugins.Media;

// An SVG read as XML with no DTD, no external resolution, no processing instructions and no comments. A file that is not
// well-formed, has a DTD or whose root is not <svg> is not an SVG. Sanitising is a conservative pass that keeps drawing and
// drops whatever can run or reach out: script and foreignObject, elements of other vocabularies such as XHTML, event
// handler attributes, attributes of other namespaces, animations that set a link or a handler, and any value that holds a
// javascript:, vbscript: or non-image data: URL, or CSS that imports or evaluates.
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

        if (name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsActiveValue(attribute.Value) || (name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase) && IsActiveCss(attribute.Value));
    }

    // White space and control characters inside a scheme are ignored by browsers, so they are taken out before comparing.
    private static bool IsActiveValue(string value)
    {
        var compact = new string([.. value.Where(character => character > ' ')]).ToLowerInvariant();
        if (compact.Contains("javascript:", StringComparison.Ordinal) || compact.Contains("vbscript:", StringComparison.Ordinal))
        {
            return true;
        }

        var data = compact.IndexOf("data:", StringComparison.Ordinal);
        while (data >= 0)
        {
            if (!SafeDataUrls.Any(safe => compact.AsSpan(data).StartsWith(safe, StringComparison.Ordinal)) || compact.AsSpan(data).StartsWith("data:image/svg", StringComparison.Ordinal))
            {
                return true;
            }

            data = compact.IndexOf("data:", data + 5, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsActiveCss(string css)
    {
        var compact = new string([.. css.Where(character => character > ' ')]).ToLowerInvariant();
        return compact.Contains("@import", StringComparison.Ordinal) || compact.Contains("expression(", StringComparison.Ordinal) || IsActiveValue(compact);
    }
}
