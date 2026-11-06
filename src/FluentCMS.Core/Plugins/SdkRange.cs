namespace FluentCMS.Core.Plugins;

// The SDK versions a plugin runs on, in NuGet interval notation: "1.0.0" is 1.0.0 or later, "[1.0.0]" exactly 1.0.0,
// "[1.0.0,2.0.0)" from 1.0.0 up to and without 2.0.0, and either bound may be left out. A pre-release of the SDK is held
// against a bound as the release it leads to, unless the bound is a pre-release itself (spec 4.13).
internal sealed class SdkRange
{
    private SdkRange(SemanticVersion? min, bool minInclusive, SemanticVersion? max, bool maxInclusive)
    {
        Min = min;
        MinInclusive = minInclusive;
        Max = max;
        MaxInclusive = maxInclusive;
    }

    public SemanticVersion? Min { get; }

    public bool MinInclusive { get; }

    public SemanticVersion? Max { get; }

    public bool MaxInclusive { get; }

    public static SdkRange? Parse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text[0] is not ('[' or '('))
        {
            return SemanticVersion.Parse(text, allowShortForm: true) is { } floor ? new SdkRange(floor, true, null, false) : null;
        }

        if (text.Length < 3 || text[^1] is not (']' or ')'))
        {
            return null;
        }

        var minInclusive = text[0] == '[';
        var maxInclusive = text[^1] == ']';
        var bounds = text[1..^1].Split(',');
        if (bounds.Length == 1)
        {
            return minInclusive && maxInclusive && SemanticVersion.Parse(bounds[0].Trim(), allowShortForm: true) is { } exact
                ? new SdkRange(exact, true, exact, true)
                : null;
        }

        if (bounds.Length != 2)
        {
            return null;
        }

        var (minText, maxText) = (bounds[0].Trim(), bounds[1].Trim());
        var min = minText.Length == 0 ? null : SemanticVersion.Parse(minText, allowShortForm: true);
        var max = maxText.Length == 0 ? null : SemanticVersion.Parse(maxText, allowShortForm: true);
        if ((minText.Length > 0 && min is null) || (maxText.Length > 0 && max is null) || (min is null && max is null))
        {
            return null;
        }

        if (min is not null && max is not null)
        {
            var order = min.CompareTo(max);
            if (order > 0 || (order == 0 && !(minInclusive && maxInclusive)))
            {
                return null;
            }
        }

        return new SdkRange(min, minInclusive, max, maxInclusive);
    }

    public bool Satisfies(SemanticVersion version)
    {
        if (Min is not null)
        {
            var order = Against(version, Min).CompareTo(Min);
            if (order < 0 || (order == 0 && !MinInclusive))
            {
                return false;
            }
        }

        if (Max is not null)
        {
            var order = Against(version, Max).CompareTo(Max);
            if (order > 0 || (order == 0 && !MaxInclusive))
            {
                return false;
            }
        }

        return true;
    }

    private static SemanticVersion Against(SemanticVersion version, SemanticVersion bound) =>
        bound.IsPrerelease ? version : version.Release;
}
