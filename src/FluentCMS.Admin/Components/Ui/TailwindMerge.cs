using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FluentCMS.Admin.Components.Ui;

// A port of tailwind-merge (MIT, github.com/dcastil/tailwind-merge) to the Tailwind v4 utilities: when two classes set the same
// CSS property under the same variants, the later one wins and the earlier one is dropped, so a Class parameter overrides a
// component's own classes the way shadcn's cn() does. It keeps the algorithm and the class groups; the groups cover the core
// utilities, and a class it does not recognise is always kept.
public static partial class TailwindMerge
{
    private const int CacheLimit = 2000;

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static string Merge(string? classes)
    {
        if (string.IsNullOrWhiteSpace(classes))
        {
            return string.Empty;
        }
        if (Cache.TryGetValue(classes, out var merged))
        {
            return merged;
        }
        merged = MergeUncached(classes);
        if (Cache.Count >= CacheLimit)
        {
            Cache.Clear();
        }
        Cache[classes] = merged;
        return merged;
    }

    private static string MergeUncached(string classes)
    {
        var all = classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>(all.Length);

        // From the last class back, so the first time a property is seen it is the class that wins.
        for (var i = all.Length - 1; i >= 0; i--)
        {
            var parsed = Parse(all[i]);
            if (parsed is not { } utility)
            {
                kept.Add(all[i]);
                continue;
            }
            var scope = utility.Variants + (utility.Important ? "!" : string.Empty) + "|";
            if (!taken.Add(scope + utility.Group))
            {
                continue;
            }
            foreach (var conflict in Conflicts(utility.Group, utility.HasPostfix))
            {
                taken.Add(scope + conflict);
            }
            kept.Add(all[i]);
        }

        kept.Reverse();
        return string.Join(' ', kept);
    }

    private readonly record struct Utility(string Variants, bool Important, string Group, bool HasPostfix);

    private static Utility? Parse(string className)
    {
        var (variants, utility) = SplitVariants(className);
        var important = false;
        if (utility.StartsWith('!'))
        {
            important = true;
            utility = utility[1..];
        }
        else if (utility.EndsWith('!'))
        {
            important = true;
            utility = utility[..^1];
        }

        // An arbitrary property such as [mask-type:luminance] conflicts only with the same property.
        if (utility.StartsWith('[') && utility.EndsWith(']') && utility.IndexOf(':') is > 1 and var colon)
        {
            return new Utility(variants, important, "[" + utility[1..colon] + "]", false);
        }

        var postfix = IndexOutsideBrackets(utility, '/');
        if (postfix > 0 && GroupOf(utility[..postfix]) is { } withoutPostfix)
        {
            return new Utility(variants, important, withoutPostfix, true);
        }
        return GroupOf(utility) is { } group ? new Utility(variants, important, group, false) : null;
    }

    // Variants are sorted, so hover:focus: and focus:hover: are the same scope, except arbitrary variants and the
    // pseudo-elements, whose position changes what they select.
    private static (string Variants, string Utility) SplitVariants(string className)
    {
        var variants = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < className.Length; i++)
        {
            switch (className[i])
            {
                case '[' or '(':
                    depth++;
                    break;
                case ']' or ')':
                    depth--;
                    break;
                case ':' when depth == 0:
                    variants.Add(className[start..i]);
                    start = i + 1;
                    break;
            }
        }
        if (variants.Count == 0)
        {
            return (string.Empty, className);
        }

        var ordered = new List<string>(variants.Count);
        var run = new List<string>();
        foreach (var variant in variants)
        {
            if (variant.StartsWith('[') || OrderSensitiveVariants.Contains(variant))
            {
                run.Sort(StringComparer.Ordinal);
                ordered.AddRange(run);
                run.Clear();
                ordered.Add(variant);
            }
            else
            {
                run.Add(variant);
            }
        }
        run.Sort(StringComparer.Ordinal);
        ordered.AddRange(run);
        return (string.Join(':', ordered), className[start..]);
    }

    private static readonly HashSet<string> OrderSensitiveVariants =
        ["*", "**", "after", "backdrop", "before", "details-content", "file", "first-letter", "first-line", "marker", "placeholder", "selection"];

    private static int IndexOutsideBrackets(string value, char character)
    {
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '[' or '(':
                    depth++;
                    break;
                case ']' or ')':
                    depth--;
                    break;
                default:
                    if (value[i] == character && depth == 0)
                    {
                        return i;
                    }
                    break;
            }
        }
        return -1;
    }

    // Tries the longest prefix first: rounded-tl-md is rounded-tl with md before it is rounded with tl-md. An arbitrary value or
    // variable stays one part even when it holds a dash, as in w-[calc(100%-2rem)].
    private static string? GroupOf(string utility)
    {
        if (utility.StartsWith('-'))
        {
            utility = utility[1..];
        }
        var arbitrary = utility.IndexOf("-[", StringComparison.Ordinal) is var bracket and >= 0 ? bracket : utility.IndexOf("-(", StringComparison.Ordinal);
        var parts = arbitrary >= 0
            ? [.. utility[..arbitrary].Split('-'), utility[(arbitrary + 1)..]]
            : utility.Split('-');

        for (var length = parts.Length; length >= 1; length--)
        {
            var prefix = string.Join('-', parts, 0, length);
            if (!Rules.TryGetValue(prefix, out var rules))
            {
                continue;
            }
            var value = string.Join('-', parts, length, parts.Length - length);
            foreach (var rule in rules)
            {
                if (rule.Matches(value))
                {
                    return rule.Group;
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> Conflicts(string group, bool hasPostfix)
    {
        var conflicts = ConflictingGroups.TryGetValue(group, out var found) ? found : [];
        // text-lg/7 sets the line height as well, so it also replaces an earlier leading-*.
        return hasPostfix && group == "font-size" ? [.. conflicts, "leading"] : conflicts;
    }

    private static readonly Dictionary<string, string[]> ConflictingGroups = new()
    {
        ["overflow"] = ["overflow-x", "overflow-y"],
        ["overscroll"] = ["overscroll-x", "overscroll-y"],
        ["inset"] = ["inset-x", "inset-y", "start", "end", "top", "right", "bottom", "left"],
        ["inset-x"] = ["start", "end", "right", "left"],
        ["inset-y"] = ["top", "bottom"],
        ["flex"] = ["basis", "grow", "shrink"],
        ["gap"] = ["gap-x", "gap-y"],
        ["p"] = ["px", "py", "ps", "pe", "pt", "pr", "pb", "pl"],
        ["px"] = ["ps", "pe", "pr", "pl"],
        ["py"] = ["pt", "pb"],
        ["m"] = ["mx", "my", "ms", "me", "mt", "mr", "mb", "ml"],
        ["mx"] = ["ms", "me", "mr", "ml"],
        ["my"] = ["mt", "mb"],
        ["size"] = ["w", "h"],
        ["line-clamp"] = ["display", "overflow"],
        ["rounded"] = ["rounded-s", "rounded-e", "rounded-t", "rounded-r", "rounded-b", "rounded-l", "rounded-ss", "rounded-se", "rounded-ee", "rounded-es", "rounded-tl", "rounded-tr", "rounded-br", "rounded-bl"],
        ["rounded-s"] = ["rounded-ss", "rounded-es"],
        ["rounded-e"] = ["rounded-se", "rounded-ee"],
        ["rounded-t"] = ["rounded-tl", "rounded-tr"],
        ["rounded-r"] = ["rounded-tr", "rounded-br"],
        ["rounded-b"] = ["rounded-br", "rounded-bl"],
        ["rounded-l"] = ["rounded-tl", "rounded-bl"],
        ["border-w"] = ["border-w-x", "border-w-y", "border-w-s", "border-w-e", "border-w-t", "border-w-r", "border-w-b", "border-w-l"],
        ["border-w-x"] = ["border-w-s", "border-w-e", "border-w-r", "border-w-l"],
        ["border-w-y"] = ["border-w-t", "border-w-b"],
        ["border-color"] = ["border-color-x", "border-color-y", "border-color-s", "border-color-e", "border-color-t", "border-color-r", "border-color-b", "border-color-l"],
        ["border-color-x"] = ["border-color-s", "border-color-e", "border-color-r", "border-color-l"],
        ["border-color-y"] = ["border-color-t", "border-color-b"],
        ["border-spacing"] = ["border-spacing-x", "border-spacing-y"],
        ["scroll-m"] = ["scroll-mx", "scroll-my", "scroll-ms", "scroll-me", "scroll-mt", "scroll-mr", "scroll-mb", "scroll-ml"],
        ["scroll-p"] = ["scroll-px", "scroll-py", "scroll-ps", "scroll-pe", "scroll-pt", "scroll-pr", "scroll-pb", "scroll-pl"],
        ["translate"] = ["translate-x", "translate-y", "translate-z"],
        ["scale"] = ["scale-x", "scale-y", "scale-z"],
        ["skew"] = ["skew-x", "skew-y"],
    };

    private sealed record Rule(Func<string, bool> Matches, string Group);

    private static readonly Dictionary<string, List<Rule>> Rules = BuildRules();

    private static Dictionary<string, List<Rule>> BuildRules()
    {
        var rules = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);

        void Add(string group, Func<string, bool> matches, params string[] prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (!rules.TryGetValue(prefix, out var list))
                {
                    rules[prefix] = list = [];
                }
                list.Add(new Rule(matches, group));
            }
        }

        // A class that is a group on its own: block, sr-only, underline.
        void Classes(string group, params string[] classNames) => Add(group, Empty, classNames);

        // A prefix followed by one of a few words: overflow-hidden, cursor-pointer when the words are listed.
        void Words(string group, string prefix, params string[] words)
        {
            var set = words.ToHashSet(StringComparer.Ordinal);
            Add(group, set.Contains, prefix);
        }

        // Layout
        Add("aspect", Any, "aspect");
        Add("columns", Any, "columns");
        Add("break-after", Any, "break-after");
        Add("break-before", Any, "break-before");
        Add("break-inside", Any, "break-inside");
        Add("box-decoration", Any, "box-decoration");
        Words("box", "box", "border", "content");
        Classes("display", "block", "inline-block", "inline", "flex", "inline-flex", "table", "inline-table", "table-caption", "table-cell",
            "table-column", "table-column-group", "table-footer-group", "table-header-group", "table-row-group", "table-row", "flow-root",
            "grid", "inline-grid", "contents", "list-item", "hidden");
        Classes("sr", "sr-only", "not-sr-only");
        Add("float", Any, "float");
        Add("clear", Any, "clear");
        Classes("isolation", "isolate", "isolation-auto");
        Words("object-fit", "object", "contain", "cover", "fill", "none", "scale-down");
        Add("object-position", Any, "object");
        Add("overflow", Any, "overflow");
        Add("overflow-x", Any, "overflow-x");
        Add("overflow-y", Any, "overflow-y");
        Add("overscroll", Any, "overscroll");
        Add("overscroll-x", Any, "overscroll-x");
        Add("overscroll-y", Any, "overscroll-y");
        Classes("position", "static", "fixed", "absolute", "relative", "sticky");
        foreach (var inset in new[] { "inset", "inset-x", "inset-y", "start", "end", "top", "right", "bottom", "left" })
        {
            Add(inset, value => Spacing(value) || Fraction(value) || value is "auto" or "full", inset);
        }
        Classes("visibility", "visible", "invisible", "collapse");
        Add("z", Any, "z");

        // Flexbox and grid
        Add("basis", Any, "basis");
        Words("flex-direction", "flex", "row", "row-reverse", "col", "col-reverse");
        Words("flex-wrap", "flex", "wrap", "wrap-reverse", "nowrap");
        Add("flex", Any, "flex");
        Add("grow", AnyOrEmpty, "grow");
        Add("shrink", AnyOrEmpty, "shrink");
        Add("order", Any, "order");
        Add("grid-cols", Any, "grid-cols");
        Add("col-start-end", Any, "col");
        Add("col-start", Any, "col-start");
        Add("col-end", Any, "col-end");
        Add("grid-rows", Any, "grid-rows");
        Add("row-start-end", Any, "row");
        Add("row-start", Any, "row-start");
        Add("row-end", Any, "row-end");
        Add("grid-flow", Any, "grid-flow");
        Add("auto-cols", Any, "auto-cols");
        Add("auto-rows", Any, "auto-rows");
        Add("gap", Spacing, "gap");
        Add("gap-x", Spacing, "gap-x");
        Add("gap-y", Spacing, "gap-y");
        Add("justify-content", Any, "justify");
        Add("justify-items", Any, "justify-items");
        Add("justify-self", Any, "justify-self");
        Words("align-content", "content", "normal", "center", "start", "end", "between", "around", "evenly", "baseline", "stretch");
        Add("content", Any, "content");
        Add("align-items", Any, "items");
        Add("align-self", Any, "self");
        Add("place-content", Any, "place-content");
        Add("place-items", Any, "place-items");
        Add("place-self", Any, "place-self");

        // Spacing and sizing
        // Spacing and sizing take their own values only, so a custom class such as my-card is not read as a margin.
        foreach (var side in new[] { "p", "px", "py", "ps", "pe", "pt", "pr", "pb", "pl" })
        {
            Add(side, Spacing, side);
        }
        foreach (var side in new[] { "m", "mx", "my", "ms", "me", "mt", "mr", "mb", "ml" })
        {
            Add(side, value => Spacing(value) || value == "auto", side);
        }
        Add("space-x", Spacing, "space-x");
        Classes("space-x-reverse", "space-x-reverse");
        Add("space-y", Spacing, "space-y");
        Classes("space-y-reverse", "space-y-reverse");
        foreach (var size in new[] { "size", "w", "min-w", "max-w", "h", "min-h", "max-h" })
        {
            Add(size, Size, size);
        }

        // Typography
        Add("font-size", value => TshirtSize(value) || value == "base" || ArbitraryLength(value), "text");
        Words("text-alignment", "text", "left", "center", "right", "justify", "start", "end");
        Words("text-wrap", "text", "wrap", "nowrap", "balance", "pretty");
        Words("text-overflow", "text", "ellipsis", "clip");
        Add("text-color", Any, "text");
        Classes("text-overflow", "truncate");
        Classes("font-smoothing", "antialiased", "subpixel-antialiased");
        Classes("font-style", "italic", "not-italic");
        Words("font-weight", "font", "thin", "extralight", "light", "normal", "medium", "semibold", "bold", "extrabold", "black");
        Add("font-weight", ArbitraryNumber, "font");
        Add("font-stretch", Any, "font-stretch");
        Add("font-family", Any, "font");
        Add("tracking", Any, "tracking");
        Add("line-clamp", Any, "line-clamp");
        Add("leading", Any, "leading");
        Add("list-image", Any, "list-image");
        Words("list-position", "list", "inside", "outside");
        Add("list-style-type", Any, "list");
        Add("placeholder-color", Any, "placeholder");
        Classes("text-decoration", "underline", "overline", "line-through", "no-underline");
        Words("text-decoration-style", "decoration", "solid", "double", "dotted", "dashed", "wavy");
        Add("text-decoration-thickness", value => Number(value) || value is "from-font" or "auto" || ArbitraryLength(value), "decoration");
        Add("text-decoration-color", Any, "decoration");
        Add("underline-offset", Any, "underline-offset");
        Classes("text-transform", "uppercase", "lowercase", "capitalize", "normal-case");
        Add("indent", Any, "indent");
        Add("vertical-align", Any, "align");
        Add("whitespace", Any, "whitespace");
        Words("break", "break", "normal", "words", "all", "keep");
        Add("wrap", Any, "wrap");
        Add("hyphens", Any, "hyphens");
        Add("text-shadow", Any, "text-shadow");

        // Backgrounds
        Words("bg-attachment", "bg", "fixed", "local", "scroll");
        Add("bg-clip", Any, "bg-clip");
        Add("bg-origin", Any, "bg-origin");
        Words("bg-position", "bg", "bottom", "center", "left", "left-bottom", "left-top", "right", "right-bottom", "right-top", "top",
            "top-left", "top-right", "bottom-left", "bottom-right");
        Add("bg-position", value => ArbitraryOf(value, "position", static _ => false), "bg");
        Add("bg-position", Any, "bg-position");
        Words("bg-repeat", "bg", "repeat", "no-repeat", "repeat-x", "repeat-y", "repeat-round", "repeat-space");
        Words("bg-size", "bg", "auto", "cover", "contain");
        Add("bg-size", value => ArbitraryOf(value, "size", static _ => false), "bg");
        Add("bg-size", Any, "bg-size");
        Words("bg-image", "bg", "none");
        Add("bg-image", value => ArbitraryOf(value, "image", IsImage) || ArbitraryOf(value, "url", static _ => false), "bg");
        Add("bg-image", Any, "bg-linear", "bg-radial", "bg-conic");
        Add("bg-color", Any, "bg");
        foreach (var stop in new[] { "from", "via", "to" })
        {
            Add("gradient-" + stop + "-position", value => Percentage(value) || ArbitraryOf(value, "percentage", Percentage), stop);
            Add("gradient-" + stop, Any, stop);
        }

        // Borders
        foreach (var side in new[] { "rounded", "rounded-s", "rounded-e", "rounded-t", "rounded-r", "rounded-b", "rounded-l",
                     "rounded-ss", "rounded-se", "rounded-ee", "rounded-es", "rounded-tl", "rounded-tr", "rounded-br", "rounded-bl" })
        {
            Add(side, AnyOrEmpty, side);
        }
        Words("border-style", "border", "solid", "dashed", "dotted", "double", "hidden", "none");
        foreach (var (prefix, suffix) in new[] { ("border", ""), ("border-x", "-x"), ("border-y", "-y"), ("border-s", "-s"), ("border-e", "-e"),
                     ("border-t", "-t"), ("border-r", "-r"), ("border-b", "-b"), ("border-l", "-l") })
        {
            Add("border-w" + suffix, value => value.Length == 0 || Number(value) || ArbitraryLength(value), prefix);
            Add("border-color" + suffix, Any, prefix);
        }
        Classes("border-collapse", "border-collapse", "border-separate");
        Add("border-spacing", Any, "border-spacing");
        Add("border-spacing-x", Any, "border-spacing-x");
        Add("border-spacing-y", Any, "border-spacing-y");
        Add("divide-x", value => value.Length == 0 || Number(value) || ArbitraryLength(value), "divide-x");
        Classes("divide-x-reverse", "divide-x-reverse");
        Add("divide-y", value => value.Length == 0 || Number(value) || ArbitraryLength(value), "divide-y");
        Classes("divide-y-reverse", "divide-y-reverse");
        Words("divide-style", "divide", "solid", "dashed", "dotted", "double", "hidden", "none");
        Add("divide-color", Any, "divide");
        Words("outline-style", "outline", "hidden", "none", "solid", "dashed", "dotted", "double");
        Add("outline-w", value => value.Length == 0 || Number(value) || ArbitraryLength(value), "outline");
        Add("outline-color", Any, "outline");
        Add("outline-offset", Any, "outline-offset");
        Classes("ring-w-inset", "ring-inset");
        Add("ring-w", value => value.Length == 0 || Number(value) || ArbitraryLength(value), "ring");
        Add("ring-color", Any, "ring");
        Add("ring-offset-w", value => Number(value) || ArbitraryLength(value), "ring-offset");
        Add("ring-offset-color", Any, "ring-offset");
        Add("inset-ring-w", value => value.Length == 0 || Number(value) || ArbitraryLength(value), "inset-ring");
        Add("inset-ring-color", Any, "inset-ring");

        // Effects and filters
        Add("shadow", value => value.Length == 0 || value == "none" || TshirtSize(value) || ArbitraryOf(value, "shadow", IsShadow), "shadow");
        Add("shadow-color", Any, "shadow");
        Add("inset-shadow", value => value == "none" || TshirtSize(value) || ArbitraryOf(value, "shadow", IsShadow), "inset-shadow");
        Add("inset-shadow-color", Any, "inset-shadow");
        Add("opacity", Any, "opacity");
        Add("mix-blend", Any, "mix-blend");
        Add("bg-blend", Any, "bg-blend");
        Add("blur", AnyOrEmpty, "blur");
        Add("backdrop-blur", AnyOrEmpty, "backdrop-blur");
        Add("drop-shadow", AnyOrEmpty, "drop-shadow");
        Add("grayscale", AnyOrEmpty, "grayscale");
        Add("invert", AnyOrEmpty, "invert");
        Add("sepia", AnyOrEmpty, "sepia");
        Add("brightness", Any, "brightness");
        Add("contrast", Any, "contrast");
        Add("saturate", Any, "saturate");
        Add("hue-rotate", Any, "hue-rotate");

        // Tables
        Words("table-layout", "table", "auto", "fixed");
        Add("caption", Any, "caption");

        // Transitions and animation
        Words("transition-behavior", "transition", "discrete", "normal");
        Add("transition", AnyOrEmpty, "transition");
        Add("duration", Any, "duration");
        Add("ease", Any, "ease");
        Add("delay", Any, "delay");
        Add("animate", Any, "animate");

        // Transforms
        Add("transform", AnyOrEmpty, "transform");
        Add("origin", Any, "origin");
        Add("scale", Any, "scale");
        Add("scale-x", Any, "scale-x");
        Add("scale-y", Any, "scale-y");
        Add("scale-z", Any, "scale-z");
        Add("rotate", Any, "rotate");
        Add("translate", Any, "translate");
        Add("translate-x", Any, "translate-x");
        Add("translate-y", Any, "translate-y");
        Add("translate-z", Any, "translate-z");
        Add("skew", Any, "skew");
        Add("skew-x", Any, "skew-x");
        Add("skew-y", Any, "skew-y");

        // Interactivity
        Add("accent", Any, "accent");
        Add("appearance", Any, "appearance");
        Add("caret", Any, "caret");
        Add("scheme", Any, "scheme");
        Add("cursor", Any, "cursor");
        Add("field-sizing", Any, "field-sizing");
        Add("pointer-events", Any, "pointer-events");
        Add("resize", AnyOrEmpty, "resize");
        Words("scroll-behavior", "scroll", "auto", "smooth");
        foreach (var side in new[] { "m", "mx", "my", "ms", "me", "mt", "mr", "mb", "ml", "p", "px", "py", "ps", "pe", "pt", "pr", "pb", "pl" })
        {
            Add("scroll-" + side, Any, "scroll-" + side);
        }
        Add("snap-align", Any, "snap");
        Add("touch", Any, "touch");
        Add("select", Any, "select");
        Add("will-change", Any, "will-change");

        // SVG and accessibility
        Add("fill", Any, "fill");
        Add("stroke-w", value => Number(value) || ArbitraryLength(value) || ArbitraryNumber(value), "stroke");
        Add("stroke", Any, "stroke");
        Add("forced-color-adjust", Any, "forced-color-adjust");

        return rules;
    }

    private static bool Empty(string value) => value.Length == 0;

    private static bool Any(string value) => value.Length > 0;

    private static bool AnyOrEmpty(string value) => true;

    private static bool Number(string value) => double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);

    private static bool TshirtSize(string value) => TshirtSizeRegex().IsMatch(value);

    private static bool Fraction(string value) => FractionRegex().IsMatch(value);

    private static bool Percentage(string value) => value.EndsWith('%') && Number(value[..^1]);

    private static bool Arbitrary(string value) => ArbitraryRegex().IsMatch(value);

    private static bool Spacing(string value) => Number(value) || value == "px" || Arbitrary(value);

    private static bool Size(string value) => Spacing(value) || Fraction(value) || TshirtSize(value) || SizeKeywords.Contains(value);

    private static readonly HashSet<string> SizeKeywords =
        ["auto", "full", "screen", "svw", "lvw", "dvw", "svh", "lvh", "dvh", "min", "max", "fit", "prose", "none", "lh"];

    // An arbitrary value [..] or variable (..), with an optional label as in [length:var(--x)] or (color:--x). A labelled value
    // belongs to its label; an unlabelled one is judged by its content; a variable without a label is not judged.
    private static bool ArbitraryOf(string value, string label, Func<string, bool> content)
    {
        var match = ArbitraryRegex().Match(value);
        if (!match.Success)
        {
            return false;
        }
        if (match.Groups["label"].Success)
        {
            return match.Groups["label"].Value == label;
        }
        return match.Groups["open"].Value == "[" && content(match.Groups["value"].Value);
    }

    private static bool ArbitraryLength(string value) => ArbitraryOf(value, "length", IsLength);

    private static bool ArbitraryNumber(string value) => ArbitraryOf(value, "number", Number);

    private static bool IsLength(string value) => value == "0" || LengthRegex().IsMatch(value) || value.StartsWith("calc(", StringComparison.Ordinal)
        || value.StartsWith("min(", StringComparison.Ordinal) || value.StartsWith("max(", StringComparison.Ordinal) || value.StartsWith("clamp(", StringComparison.Ordinal);

    private static bool IsImage(string value) => value.StartsWith("url(", StringComparison.Ordinal) || ImageRegex().IsMatch(value);

    private static bool IsShadow(string value) => ShadowRegex().IsMatch(value);

    [GeneratedRegex(@"^(\d+(\.\d+)?)?(xs|sm|md|lg|xl)$")]
    private static partial Regex TshirtSizeRegex();

    [GeneratedRegex(@"^\d+/\d+$")]
    private static partial Regex FractionRegex();

    [GeneratedRegex(@"^(?:(?<open>\[)(?:(?<label>[a-z][a-z-]*):)?(?<value>.+)\]|(?<open>\()(?:(?<label>[a-z][a-z-]*):)?(?<value>.+)\))$")]
    private static partial Regex ArbitraryRegex();

    [GeneratedRegex(@"^-?\d*\.?\d+(%|px|r?em|ex|ch|lh|r?lh|vw|vh|vmin|vmax|[dsl]v[hw]|v[bi]|cq[whib]|cqmin|cqmax|cm|mm|in|pt|pc|q)$")]
    private static partial Regex LengthRegex();

    [GeneratedRegex(@"^(repeating-)?(linear|radial|conic)-gradient\(|^(image|cross-fade|image-set|element)\(")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"^(inset_)?-?(\d+(\.\d+)?|\.\d+)(px|r?em)?_-?(\d+(\.\d+)?|\.\d+)(px|r?em)?")]
    private static partial Regex ShadowRegex();
}
