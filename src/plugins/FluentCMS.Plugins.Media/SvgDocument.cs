using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FluentCMS.Plugins.Media;

// An SVG read as XML with no DTD, no external resolution, no processing instructions and no comments. A file that is not
// well-formed, has a DTD or whose root is not <svg> is not an SVG. Sanitising keeps drawing and nothing else, by lists of
// the elements and attributes it keeps: names are compared in their exact case, so that no element or attribute turns into
// something else when an HTML page inlines the SVG and reads its names in lower case. An <a> becomes a <g>; <title> and
// <desc> keep their text only, since an HTML page parses their content as HTML. A link (href or xlink:href) stays only when
// it is a #fragment of name characters, as written. A <style> element goes, since its rules would apply to the whole page
// that inlines the SVG; a style attribute, which styles its own element, stays. A value read as CSS, the style attribute
// and every other attribute kept, goes when it holds a url() to anything but a #fragment, @import, expression(, image(,
// image-set(, src( or element(, in the value with its CSS comments kept or with them taken out, after CSS escapes are
// undone; the two forms cover a comment that a string hides and a string that a comment hides; a value with a backslash and
// a url( goes too. The root loses overflow, style and transform, which would paint over the page around it. An animation
// of a link, a handler, a class or an id goes. Scoping then gives every id and every reference to one a prefix of the file,
// so that an inlined SVG names nothing of the page, on window or in another SVG, and reaches none of its elements. Save
// writes what an HTML parser reads as an XML parser does: no CDATA section, comment or prefixed element.
internal static partial class SvgDocument
{
    public static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly XNamespace XLinkNamespace = "http://www.w3.org/1999/xlink";

    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "svg", "g", "defs", "symbol", "use", "switch", "view", "title", "desc",
        "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "tspan", "textPath",
        "linearGradient", "radialGradient", "stop", "pattern", "clipPath", "mask", "marker",
        "filter", "feBlend", "feColorMatrix", "feComponentTransfer", "feComposite", "feConvolveMatrix", "feDiffuseLighting",
        "feDisplacementMap", "feDistantLight", "feDropShadow", "feFlood", "feFuncA", "feFuncB", "feFuncG", "feFuncR",
        "feGaussianBlur", "feMerge", "feMergeNode", "feMorphology", "feOffset", "fePointLight", "feSpecularLighting",
        "feSpotLight", "feTile", "feTurbulence",
        "animate", "animateMotion", "animateTransform", "set", "mpath",
    };

    private static readonly HashSet<string> Animations = new(StringComparer.Ordinal) { "animate", "animateMotion", "animateTransform", "set" };

    // Elements whose content an HTML page that inlines the SVG parses as HTML.
    private static readonly HashSet<string> TextOnly = new(StringComparer.Ordinal) { "title", "desc" };

    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "id", "class", "style", "lang", "role", "aria-label", "aria-labelledby", "aria-describedby", "aria-hidden", "focusable",
        "x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "rx", "ry", "fx", "fy", "fr", "width", "height", "d", "points",
        "pathLength", "transform", "viewBox", "preserveAspectRatio", "version", "baseProfile",
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
        "stroke-dasharray", "stroke-dashoffset", "stroke-opacity", "opacity", "color", "display", "visibility", "overflow",
        "clip", "clip-path", "clip-rule", "mask", "mask-type", "filter", "marker-start", "marker-mid", "marker-end",
        "paint-order", "vector-effect", "shape-rendering", "text-rendering", "image-rendering", "color-interpolation",
        "color-interpolation-filters", "isolation", "mix-blend-mode", "transform-origin", "stop-color", "stop-opacity",
        "flood-color", "flood-opacity", "lighting-color",
        "font-family", "font-size", "font-size-adjust", "font-style", "font-variant", "font-weight", "font-stretch",
        "text-anchor", "dominant-baseline", "alignment-baseline", "baseline-shift", "letter-spacing", "word-spacing",
        "text-decoration", "writing-mode", "direction", "unicode-bidi", "dx", "dy", "rotate", "textLength", "lengthAdjust",
        "startOffset", "method", "spacing", "side",
        "offset", "gradientUnits", "gradientTransform", "spreadMethod", "patternUnits", "patternContentUnits",
        "patternTransform", "clipPathUnits", "maskUnits", "maskContentUnits", "markerUnits", "markerWidth", "markerHeight",
        "refX", "refY", "orient",
        "filterUnits", "primitiveUnits", "in", "in2", "result", "stdDeviation", "mode", "operator", "k1", "k2", "k3", "k4",
        "values", "type", "tableValues", "slope", "intercept", "amplitude", "exponent", "baseFrequency", "numOctaves", "seed",
        "stitchTiles", "scale", "xChannelSelector", "yChannelSelector", "radius", "kernelMatrix", "order", "divisor", "bias",
        "targetX", "targetY", "edgeMode", "kernelUnitLength", "preserveAlpha", "surfaceScale", "diffuseConstant",
        "specularConstant", "specularExponent", "azimuth", "elevation", "z", "pointsAtX", "pointsAtY", "pointsAtZ",
        "limitingConeAngle",
        "requiredFeatures", "requiredExtensions", "systemLanguage",
        "attributeName", "attributeType", "begin", "dur", "end", "min", "max", "restart", "repeatCount", "repeatDur",
        "calcMode", "keyTimes", "keySplines", "keyPoints", "from", "to", "by", "additive", "accumulate", "path",
        "href",
    };

    // The attributes that carry a URL, which an animation may not set.
    private static readonly HashSet<string> LinkAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "srcset", "poster", "action", "formaction", "background", "base", "data", "codebase", "cite", "longdesc", "ping",
    };

    // The white space a URL parser takes off the ends of a link; a no-break space is not, so "&#xA0;#a" is a path.
    private static readonly char[] UrlWhiteSpace = [' ', '\t', '\n', '\r', '\f'];

    // CSS that reaches out without url(): an import, an expression, the functions that take an image or a source as a string,
    // and element() and -moz-element(), which paint an element of the page.
    private static readonly string[] ActiveCss = ["@import", "expression(", "image(", "image-set(", "src(", "element("];

    // The attributes that name an element for the page's CSS and scripts, which an animation may not set.
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase) { "class", "id" };

    // The attributes of the root that would let the drawing paint over the page around it when the page inlines it.
    private static readonly HashSet<string> RootDropped = new(StringComparer.Ordinal) { "overflow", "style", "transform" };

    // Attributes that hold ids separated by white space, and those of an animation that can name an element, as id.event.
    private static readonly HashSet<string> IdLists = new(StringComparer.Ordinal) { "aria-labelledby", "aria-describedby" };

    private static readonly HashSet<string> Timings = new(StringComparer.Ordinal) { "begin", "end" };

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

            if (element.Name.LocalName == "a" && IsSvg(element.Name))
            {
                element.Name = element.Name.Namespace + "g";
            }

            if (!IsKept(element))
            {
                element.Remove();
                continue;
            }

            if (TextOnly.Contains(element.Name.LocalName))
            {
                element.Elements().Remove();
            }

            foreach (var attribute in element.Attributes().ToList())
            {
                if (!attribute.IsNamespaceDeclaration && (!IsKept(attribute) || (element.Parent is null && attribute.Name.Namespace == XNamespace.None && RootDropped.Contains(attribute.Name.LocalName))))
                {
                    attribute.Remove();
                }
            }
        }

        // A CDATA section is its text, which Save escapes; a comment or a processing instruction is nothing of the drawing.
        foreach (var node in document.DescendantNodes().Where(node => node is XCData or XComment or XProcessingInstruction).ToList())
        {
            if (node is XCData data)
            {
                node.ReplaceWith(new XText(data.Value));
            }
            else
            {
                node.Remove();
            }
        }

        document.Declaration = null;
    }

    // After Sanitize: every id, and every name that a link, a url(), an aria id list or the begin or end of an animation
    // refers to, takes the prefix fcms-, sixteen hex digits of the SHA-256 of the file as Save writes it before the prefix,
    // and a hyphen. A reference to no id of the file takes it too, so that it still finds nothing rather than an element of
    // the page. A prefix of that form that the file's ids carry already, or with no id its references, is kept only when it
    // is the one the file without it derives, which is the case for a file this wrote, so that sanitising it gives the same
    // bytes; any other takes the file's own on top. First the values this cannot rewrite go: a link that is no fragment, a
    // value whose url() it does not read, and a begin or end with a backslash, whose escapes a browser may read as a name.
    public static void Scope(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<string> names = [];
        Visit(document, name =>
        {
            names.Add(name);
            return name;
        }, normalize: true);

        var ids = document.Descendants().Select(element => element.Attribute("id")?.Value).OfType<string>().ToList();
        var named = ids.Count > 0 ? ids : names;
        if (named.Count == 0)
        {
            return;
        }

        if (ScopedPrefix().Match(named[0]) is { Success: true } existing && named.TrueForAll(name => name.StartsWith(existing.Value, StringComparison.Ordinal)))
        {
            var stripped = new XDocument(document);
            Visit(stripped, name => name.StartsWith(existing.Value, StringComparison.Ordinal) ? name[existing.Value.Length..] : name);
            if (PrefixOf(stripped) == existing.Value)
            {
                return;
            }
        }

        var prefix = PrefixOf(document);
        Visit(document, name => prefix + name);
    }

    private static string PrefixOf(XDocument document) => $"fcms-{Convert.ToHexStringLower(SHA256.HashData(Save(document)))[..16]}-";

    // Each name an attribute holds, as the map gives it back. Normalising, the values that cannot be rewritten go.
    private static void Visit(XDocument document, Func<string, string> map, bool normalize = false)
    {
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().ToList())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                var local = attribute.Name.LocalName;
                var plain = attribute.Name.Namespace == XNamespace.None;
                if (plain && local == "id")
                {
                    attribute.Value = map(attribute.Value);
                }
                else if (local == "href" && (plain || attribute.Name.Namespace == XLinkNamespace))
                {
                    var link = attribute.Value.Trim(UrlWhiteSpace);
                    if (Fragment().IsMatch(link))
                    {
                        attribute.Value = "#" + map(link[1..]);
                    }
                    else if (normalize)
                    {
                        attribute.Remove();
                    }
                }
                else if (plain && IdLists.Contains(local))
                {
                    attribute.Value = string.Join(' ', attribute.Value.Split(UrlWhiteSpace, StringSplitOptions.RemoveEmptyEntries).Select(map));
                }
                else if (plain && Timings.Contains(local) && Animations.Contains(element.Name.LocalName))
                {
                    if (normalize && attribute.Value.Contains('\\', StringComparison.Ordinal))
                    {
                        attribute.Remove();
                        continue;
                    }

                    attribute.Value = string.Join(';', attribute.Value.Split(';').Select(timing => Syncbase().Replace(timing, match => match.Groups[1].Value + map(match.Groups[2].Value) + ".", 1)));
                }
                else if (plain)
                {
                    if (normalize && Compact(CssReference().Replace(attribute.Value, ""), keepComments: true).Contains("url(", StringComparison.Ordinal))
                    {
                        attribute.Remove();
                        continue;
                    }

                    attribute.Value = CssReference().Replace(attribute.Value, match => $"url({match.Groups[1].Value}#{map(match.Groups[2].Value)}{match.Groups[1].Value})");
                }
            }
        }
    }

    // The drawing as an HTML parser reads it the same as an XML parser: SVG elements without a prefix under the SVG namespace as
    // the default, the attributes the lists keep with xlink: and xml: as HTML adjusts them, text and values escaped so that no
    // <, >, & or quote ends a context, and no declaration, CDATA section, comment or processing instruction. Characters that
    // XML would normalise are written as references and new lines as \n, so a file is written alike on every platform.
    public static byte[] Save(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var text = new StringBuilder();
        if (document.Root is { } root)
        {
            var xlink = root.DescendantsAndSelf().Attributes().Any(attribute => attribute.Name.Namespace == XLinkNamespace);
            Write(root, text, xlink);
        }

        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    private static void Write(XElement element, StringBuilder text, bool? xlink)
    {
        if (!IsSvg(element.Name))
        {
            return;
        }

        text.Append('<').Append(element.Name.LocalName);
        if (xlink is { } declares)
        {
            text.Append(" xmlns=\"").Append(SvgNamespace.NamespaceName).Append('"');
            if (declares)
            {
                text.Append(" xmlns:xlink=\"").Append(XLinkNamespace.NamespaceName).Append('"');
            }
        }

        foreach (var attribute in element.Attributes())
        {
            var prefix = attribute.Name.Namespace == XNamespace.None ? ""
                : attribute.Name.Namespace == XLinkNamespace ? "xlink:"
                : attribute.Name.Namespace == XNamespace.Xml ? "xml:"
                : null;
            if (!attribute.IsNamespaceDeclaration && prefix is not null)
            {
                text.Append(' ').Append(prefix).Append(attribute.Name.LocalName).Append("=\"");
                Escape(attribute.Value, text, inAttribute: true);
                text.Append('"');
            }
        }

        if (!element.Nodes().Any())
        {
            text.Append(" />");
            return;
        }

        text.Append('>');
        foreach (var node in element.Nodes())
        {
            if (node is XElement child)
            {
                Write(child, text, null);
            }
            else if (node is XText part)
            {
                Escape(part.Value, text, inAttribute: false);
            }
        }

        text.Append("</").Append(element.Name.LocalName).Append('>');
    }

    private static void Escape(string value, StringBuilder text, bool inAttribute)
    {
        foreach (var character in value)
        {
            var escaped = character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' when inAttribute => "&quot;",
                '\t' when inAttribute => "&#x9;",
                '\n' when inAttribute => "&#xA;",
                '\r' => "&#xD;",
                _ => null,
            };
            if (escaped is null)
            {
                text.Append(character);
            }
            else
            {
                text.Append(escaped);
            }
        }
    }

    private static bool IsSvg(XName name) => name.Namespace == SvgNamespace || name.Namespace == XNamespace.None;

    private static bool IsKept(XElement element)
    {
        var name = element.Name.LocalName;
        if (!IsSvg(element.Name) || !Elements.Contains(name))
        {
            return false;
        }

        if (!Animations.Contains(name) || element.Attribute("attributeName")?.Value.Trim() is not { } target)
        {
            return true;
        }

        var local = target[(target.LastIndexOf(':') + 1)..];
        return !local.StartsWith("on", StringComparison.OrdinalIgnoreCase) && !LinkAttributes.Contains(local) && !Names.Contains(local);
    }

    private static bool IsKept(XAttribute attribute)
    {
        var name = attribute.Name;
        if (name.Namespace == XNamespace.Xml)
        {
            return name.LocalName is "space" or "lang";
        }

        if (name.Namespace == XLinkNamespace || (name.Namespace == XNamespace.None && name.LocalName == "href"))
        {
            return name.LocalName == "href" && Fragment().IsMatch(attribute.Value.Trim(UrlWhiteSpace));
        }

        return name.Namespace == XNamespace.None && Attributes.Contains(name.LocalName) && !IsActiveCss(attribute.Value);
    }

    // An escape near url( can change what a browser reads its argument as, as \" does, which ends no string in an unquoted
    // url, so a value with a backslash and a url( goes whatever the url( holds.
    private static bool IsActiveCss(string css)
    {
        var kept = Compact(css, keepComments: true);
        return HoldsActiveCss(kept) || HoldsActiveCss(Compact(css, keepComments: false))
            || (css.Contains('\\', StringComparison.Ordinal) && kept.Contains("url(", StringComparison.Ordinal));
    }

    private static bool HoldsActiveCss(ReadOnlySpan<char> compact)
    {
        foreach (var active in ActiveCss)
        {
            if (compact.Contains(active, StringComparison.Ordinal))
            {
                return true;
            }
        }

        for (var url = compact.IndexOf("url(", StringComparison.Ordinal); url >= 0; url = compact.IndexOf("url(", StringComparison.Ordinal))
        {
            compact = compact[(url + 4)..];
            if (!CssFragment().IsMatch(compact))
            {
                return true;
            }
        }

        return false;
    }

    // A value as CSS reads it: escapes undone, with or without its comments, without white space and control characters,
    // and in lower case.
    private static string Compact(string value, bool keepComments)
    {
        var text = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!keepComments && character == '/' && index + 1 < value.Length && value[index + 1] == '*')
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

    // A link as written: a # and name characters, nothing else.
    [GeneratedRegex(@"^#[\p{L}\p{N}_.:-]+$")]
    private static partial Regex Fragment();

    // What follows url( in the compact form: a #fragment, bare or quoted, and the parenthesis that closes it.
    [GeneratedRegex(@"^(['""]?)#[\p{L}\p{N}_.:-]+\1\)")]
    private static partial Regex CssFragment();

    // A url() of a #fragment as written in a value that Sanitize kept, which has no escape there.
    [GeneratedRegex(@"(?i:url)\([\t\n\f\r ]*(['""]?)#([\p{L}\p{N}_.:-]+)\1[\t\n\f\r ]*\)")]
    private static partial Regex CssReference();

    // The element of a begin or end value, id.event, id.begin, id.end or id.repeat(n), where a clock value such as 1.5s is
    // no id since what follows its dot is no letter.
    [GeneratedRegex(@"^(\s*)([^\s.;+()\\-][^\s.;+()\\]*)\.(?=[A-Za-z])")]
    private static partial Regex Syncbase();

    [GeneratedRegex("^fcms-[0-9a-f]{16}-")]
    private static partial Regex ScopedPrefix();
}
