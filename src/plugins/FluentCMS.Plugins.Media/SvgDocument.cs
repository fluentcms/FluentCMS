using System.Globalization;
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
// it is a #fragment of name characters, as written. A value read as CSS, the style attribute, a <style> element and every
// other attribute kept, goes when it holds a url() to anything but a #fragment, @import, expression(, image(, image-set(
// or src(, in the value with its CSS comments kept or with them taken out, after CSS escapes are undone; the two forms
// cover a comment that a string hides and a string that a comment hides; a value with a backslash and a url( goes too. An
// animation of a link or a handler goes.
internal static partial class SvgDocument
{
    public static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly XNamespace XLinkNamespace = "http://www.w3.org/1999/xlink";

    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "svg", "g", "defs", "symbol", "use", "switch", "view", "title", "desc", "style",
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
                if (!attribute.IsNamespaceDeclaration && !IsKept(attribute))
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

    private static bool IsSvg(XName name) => name.Namespace == SvgNamespace || name.Namespace == XNamespace.None;

    private static bool IsKept(XElement element)
    {
        var name = element.Name.LocalName;
        if (!IsSvg(element.Name) || !Elements.Contains(name))
        {
            return false;
        }

        if (name == "style")
        {
            return !element.HasElements && !IsActiveCss(element.Value);
        }

        if (!Animations.Contains(name) || element.Attribute("attributeName")?.Value.Trim() is not { } target)
        {
            return true;
        }

        var local = target[(target.LastIndexOf(':') + 1)..];
        return !local.StartsWith("on", StringComparison.OrdinalIgnoreCase) && !LinkAttributes.Contains(local);
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
}
