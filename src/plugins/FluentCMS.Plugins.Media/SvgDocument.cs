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
// a url( goes too. The root loses overflow, style, transform and filter, which would paint over the page around it, and
// an animation of the root that sets one of them or moves it goes. An animation of a link, a handler, a class or an id
// goes, and so does one that loses its begin or end. A file nested deeper than MaxDepth is no SVG. Scoping then gives
// every id and every reference to one a prefix of the file, so that an inlined SVG names nothing of the page, on window or
// in another SVG, and reaches none of its elements. Save writes what an HTML parser reads as an XML parser does: no CDATA
// section, comment or prefixed element.
internal static partial class SvgDocument
{
    // The most levels of elements a file nests, the root included. A file nested deeper is no SVG the plugin takes: no
    // drawing needs as many, and the passes over the tree, Save's included, recurse as deep as it nests.
    public const int MaxDepth = 128;

    // The most nodes a file holds, elements, text, the white space between elements included, CDATA sections, comments and
    // processing instructions, and the most attributes an element has, namespace declarations included. With the depth,
    // they bound the size of the tree that is built and of each list a pass sets again; a drawing has far fewer. A file
    // past either is no SVG the plugin takes. The node cap is fixed rather than a setting: what a site raises is the size.
    public const int MaxNodes = 100_000;

    public const int MaxAttributes = 1_000;

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

    // The attributes of the root that would let the drawing paint over the page around it when the page inlines it, which
    // an animation of the root may not set either; animateTransform and animateMotion of the root move it as transform does.
    private static readonly HashSet<string> RootDropped = new(StringComparer.OrdinalIgnoreCase) { "overflow", "style", "transform", "filter" };

    // What an animation of the root may not set: those, and its size and place, which the root keeps as written but which an
    // animation would grow past the size the page's CSS gives it.
    private static readonly HashSet<string> RootAnimated = new([.. RootDropped, "width", "height", "x", "y", "viewBox", "preserveAspectRatio"], StringComparer.OrdinalIgnoreCase);

    // The white space of a begin or end list; a no-break space is none, so that it is no part of a value that is read whole.
    private static readonly char[] TimingSpace = [' ', '\t', '\n', '\r'];

    // Attributes that hold ids separated by white space, and those of an animation that can name an element, as id.event.
    private static readonly HashSet<string> IdLists = new(StringComparer.Ordinal) { "aria-labelledby", "aria-describedby" };

    private static readonly HashSet<string> Timings = new(StringComparer.Ordinal) { "begin", "end" };

    // Parse, then Sanitize, Scope and Save: the bytes to store, or null when the stream holds no SVG.
    public static byte[]? Sanitized(Stream stream, long maxSize, CancellationToken cancellationToken) =>
        Parse(stream, maxSize, cancellationToken) is { } document ? Sanitized(document, cancellationToken) : null;

    public static byte[] Sanitized(XDocument document, CancellationToken cancellationToken)
    {
        Sanitize(document, cancellationToken);
        Scope(document, cancellationToken);
        return Save(document, cancellationToken);
    }

    // A file over maxSize, from where the stream stands, is refused as too large before it is read as XML; a stream that
    // cannot seek is read up to the limit only.
    public static XDocument? Parse(Stream stream, long maxSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreProcessingInstructions = true,
            IgnoreComments = true,
            MaxCharactersFromEntities = 0,
            CloseInput = false,
        };

        // The depth and the counts are read first, without building the tree, so that a file past a limit costs one pass of the
        // reader; that pass sees the comments and processing instructions the tree leaves out, which still cost its building.
        var seekable = stream.CanSeek ? stream : Buffered(stream, maxSize, cancellationToken);
        var start = seekable.Position;
        if (seekable.Length - start > maxSize)
        {
            throw TooLarge(maxSize);
        }

        var counting = settings.Clone();
        counting.IgnoreComments = false;
        counting.IgnoreProcessingInstructions = false;
        try
        {
            using (var scan = XmlReader.Create(seekable, counting))
            {
                var nodes = 0;
                while (scan.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (scan.NodeType is not (XmlNodeType.EndElement or XmlNodeType.XmlDeclaration) && ++nodes > MaxNodes)
                    {
                        return null;
                    }

                    if (scan.NodeType == XmlNodeType.Element && (scan.Depth >= MaxDepth || scan.AttributeCount > MaxAttributes))
                    {
                        return null;
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            seekable.Position = start;
            using var reader = XmlReader.Create(seekable, settings);
            var document = XDocument.Load(reader);
            return document.Root is { } root && root.Name.LocalName == "svg" && (root.Name.Namespace == SvgNamespace || root.Name.Namespace == XNamespace.None) ? document : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static MemoryStream Buffered(Stream stream, long maxSize, CancellationToken cancellationToken)
    {
        var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (copy.Length + read > maxSize)
            {
                throw TooLarge(maxSize);
            }

            copy.Write(buffer, 0, read);
        }

        copy.Position = 0;
        return copy;
    }

    private static PluginException TooLarge(long maxSize) => new(MediaErrorCodes.TooLarge, $"The file is over the limit of {maxSize} bytes an SVG.");

    // From the root down, each element's children are read once and set again once, the kept ones only: removing nodes one
    // at a time walks the list of siblings each time, which takes a time that grows with the square of their number. A
    // CDATA section becomes its text, which Save escapes, and goes when it is empty, so that a second pass writes the same
    // empty element; a comment or a processing instruction is nothing of the drawing.
    public static void Sanitize(XDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Declaration = null;
        if (document.Root is not { } root)
        {
            return;
        }

        KeepAttributes(root, isRoot: true);
        var rootId = root.Attribute("id")?.Value;
        Stack<XElement> pending = new([root]);
        while (pending.TryPop(out var element))
        {
            var textOnly = TextOnly.Contains(element.Name.LocalName);
            List<XNode> kept = [];
            foreach (var node in element.Nodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (node)
                {
                    case XCData data when data.Value.Length > 0:
                        kept.Add(new XText(data.Value));
                        break;
                    case XCData:
                        break;
                    case XText text:
                        kept.Add(text);
                        break;
                    case XElement child when !textOnly:
                        if (child.Name.LocalName == "a" && IsSvg(child.Name))
                        {
                            child.Name = child.Name.Namespace + "g";
                        }

                        // Its attributes first, so that a link that goes leaves an animation of its parent, as a later pass reads it.
                        if (IsKept(child))
                        {
                            var animation = Animations.Contains(child.Name.LocalName);
                            var timings = animation ? TimingsOf(child) : 0;
                            KeepAttributes(child, isRoot: false);
                            if (!(animation && ((Targets(child, root, rootId) && MovesTheBox(child)) || TimingsOf(child) < timings)))
                            {
                                kept.Add(child);
                            }
                        }

                        break;
                }
            }

            element.ReplaceNodes(kept);
            foreach (var child in element.Elements())
            {
                pending.Push(child);
            }
        }
    }

    // Set again once, as the children are.
    private static void KeepAttributes(XElement element, bool isRoot) =>
        element.ReplaceAttributes(element.Attributes()
            .Where(attribute => attribute.IsNamespaceDeclaration || (IsKept(attribute) && !(isRoot && attribute.Name.Namespace == XNamespace.None && RootDropped.Contains(attribute.Name.LocalName))))
            .ToList());

    // After Sanitize: every id, and every name that a link, a url(), an aria id list or the begin or end of an animation
    // refers to, takes the prefix fcms_, sixteen hex digits of the SHA-256 of the file as Save writes it before the prefix,
    // and an underscore; a hyphen would be read as an offset in id.end-1s. A reference to no id of the file takes it too, so
    // that it still finds nothing rather than an element of the page. A prefix of that form that the file carries already
    // is kept only when taking it off and scoping the file again gives the same bytes, which is the case for a file this
    // wrote, so that sanitising it gives the same bytes; otherwise every name takes the file's own on top. First the values
    // this cannot rewrite go: a link that is no fragment, a value whose url() it does not read, and a begin or end that is
    // not read whole, such as one with a backslash, whose escapes a browser may read as a name.
    public static void Scope(XDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<string> names = [];
        Visit(document, name =>
        {
            names.Add(name);
            return name;
        }, cancellationToken, normalize: true);

        var ids = document.Descendants().Select(element => element.Attribute("id")?.Value).OfType<string>().ToList();
        var named = ids.Count > 0 ? ids : names;
        if (named.Count == 0)
        {
            return;
        }

        if (ScopedPrefix().Match(named[0]) is { Success: true } existing)
        {
            var again = new XDocument(document);
            Visit(again, name => name.StartsWith(existing.Value, StringComparison.Ordinal) ? name[existing.Value.Length..] : name, cancellationToken);
            var derived = PrefixOf(again, cancellationToken);
            Visit(again, name => derived + name, cancellationToken);
            if (Save(again, cancellationToken).AsSpan().SequenceEqual(Save(document, cancellationToken)))
            {
                return;
            }
        }

        var prefix = PrefixOf(document, cancellationToken);
        Visit(document, name => prefix + name, cancellationToken);
    }

    private static string PrefixOf(XDocument document, CancellationToken cancellationToken) =>
        $"fcms_{Convert.ToHexStringLower(SHA256.HashData(Save(document, cancellationToken)))[..16]}_";

    // Each name an attribute holds, as the map gives it back. Normalising, the values that cannot be rewritten go, and so
    // does an animation that loses its begin or end to them, which would otherwise start when the drawing loads.
    private static void Visit(XDocument document, Func<string, string> map, CancellationToken cancellationToken, bool normalize = false)
    {
        List<XElement> untimed = [];
        foreach (var element in document.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    var timings = attribute.Value.Split(';');
                    if (normalize && !timings.All(timing => timing.Trim(TimingSpace).Length == 0 || Timing().IsMatch(timing)))
                    {
                        attribute.Remove();
                        untimed.Add(element);
                        continue;
                    }

                    attribute.Value = string.Join(';', timings.Select(timing => Timing().Match(timing) is { Success: true } match && match.Groups["id"] is { Success: true } id
                        ? timing[..id.Index] + map(id.Value) + timing[(id.Index + id.Length)..]
                        : timing));
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

        foreach (var element in untimed.Distinct())
        {
            element.Remove();
        }
    }

    // The drawing as an HTML parser reads it the same as an XML parser: SVG elements without a prefix under the SVG namespace as
    // the default, the attributes the lists keep with xlink: and xml: as HTML adjusts them, text and values escaped so that no
    // <, >, & or quote ends a context, and no declaration, CDATA section, comment or processing instruction. Characters that
    // XML would normalise are written as references and new lines as \n, so a file is written alike on every platform.
    public static byte[] Save(XDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var text = new StringBuilder();
        if (document.Root is { } root)
        {
            var xlink = root.DescendantsAndSelf().Attributes().Any(attribute => attribute.Name.Namespace == XLinkNamespace);
            Write(root, text, xlink, cancellationToken);
        }

        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    private static void Write(XElement element, StringBuilder text, bool? xlink, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        // An empty text is no content, so that an element is written alike whether or not one was left in it.
        if (!element.Nodes().Any(node => node is XElement || (node is XText text && text.Value.Length > 0)))
        {
            text.Append(" />");
            return;
        }

        text.Append('>');
        foreach (var node in element.Nodes())
        {
            if (node is XElement child)
            {
                Write(child, text, null, cancellationToken);
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

    // An animation without a link animates its parent; one with a link, the element of that id. With both links, either
    // naming the root counts, since browsers differ on which one wins.
    private static bool Targets(XElement animation, XElement root, string? rootId)
    {
        XAttribute?[] links = [animation.Attribute("href"), animation.Attribute(XLinkNamespace + "href")];
        return links.All(link => link is null)
            ? animation.Parent == root
            : rootId is not null && links.Any(link => link is not null && link.Value.Trim(UrlWhiteSpace) == "#" + rootId);
    }

    private static int TimingsOf(XElement animation) =>
        animation.Attributes().Count(attribute => attribute.Name.Namespace == XNamespace.None && Timings.Contains(attribute.Name.LocalName));

    private static bool MovesTheBox(XElement animation) =>
        animation.Name.LocalName is "animateTransform" or "animateMotion"
        || (animation.Attribute("attributeName")?.Value.Trim() is { } target && RootAnimated.Contains(target[(target.LastIndexOf(':') + 1)..]));

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

    // One value of a begin or end list, read whole: an event, begin, end or repeat(n) with an optional id and a dot before
    // it, with an optional offset; or a clock value, with an optional sign; or indefinite, which reads as an event. Anything
    // else is a value a browser may read otherwise, accessKey(c) and wallclock(...) included: a browser reads an id and an
    // event inside wallclock(), and accessKey() listens to keys on the whole page.
    [GeneratedRegex(@"^[ \t\r\n]*(?:(?:(?<id>[\p{L}_][\p{L}\p{N}_:-]*)\.)?(?:repeat\([0-9]+\)|[A-Za-z]+)(?:[ \t\r\n]*[+-][ \t\r\n]*" + Clock + ")?|[+-]?[ \t\r\n]*" + Clock + @")[ \t\r\n]*$")]
    private static partial Regex Timing();

    private const string Clock = @"(?:[0-9]+:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?|[0-9]+:[0-9]{2}(?:\.[0-9]+)?|[0-9]+(?:\.[0-9]+)?(?:h|min|s|ms)?)";

    [GeneratedRegex("^fcms_[0-9a-f]{16}_")]
    private static partial Regex ScopedPrefix();
}
