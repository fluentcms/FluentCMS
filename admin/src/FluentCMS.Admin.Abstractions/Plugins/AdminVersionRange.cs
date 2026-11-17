using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Admin.Plugins;

/// <summary>
/// A range of versions of <c>FluentCMS.Admin.Abstractions</c> in NuGet's notation, as <c>AdminRange</c> names it:
/// <c>[1.0.0,2.0.0)</c>, <c>(,2.0.0]</c>, <c>[1.2.0]</c> for one version, or <c>1.0.0</c> for that version and every later
/// one. A version is compared by its major, minor and patch numbers; a pre-release label is left out.
/// </summary>
public sealed class AdminVersionRange
{
    private readonly Version? _minimum;
    private readonly Version? _maximum;
    private readonly bool _includesMinimum;
    private readonly bool _includesMaximum;

    private AdminVersionRange(Version? minimum, bool includesMinimum, Version? maximum, bool includesMaximum)
    {
        _minimum = minimum;
        _includesMinimum = includesMinimum;
        _maximum = maximum;
        _includesMaximum = includesMaximum;
    }

    /// <summary>Reads a range.</summary>
    /// <param name="text">The range, such as <c>[1.0.0,2.0.0)</c>.</param>
    /// <param name="range">The range, when the text is one.</param>
    /// <returns><see langword="true"/> when the text is a range.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out AdminVersionRange? range)
    {
        range = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text[0] is not ('[' or '('))
        {
            if (!TryVersion(text, out var only))
            {
                return false;
            }

            range = new AdminVersionRange(only, true, null, false);
            return true;
        }

        if (text.Length < 3 || text[^1] is not (']' or ')'))
        {
            return false;
        }

        var includesMinimum = text[0] == '[';
        var includesMaximum = text[^1] == ']';
        var parts = text[1..^1].Split(',');
        if (parts.Length == 1)
        {
            // One version in brackets is that version and no other.
            if (!includesMinimum || !includesMaximum || !TryVersion(parts[0], out var exact))
            {
                return false;
            }

            range = new AdminVersionRange(exact, true, exact, true);
            return true;
        }

        Version? minimum = null;
        Version? maximum = null;
        if (parts.Length != 2
            || (parts[0].Trim().Length > 0 && !TryVersion(parts[0], out minimum))
            || (parts[1].Trim().Length > 0 && !TryVersion(parts[1], out maximum))
            || (minimum is null && maximum is null)
            || (minimum is not null && maximum is not null && minimum > maximum))
        {
            return false;
        }

        range = new AdminVersionRange(minimum, includesMinimum, maximum, includesMaximum);
        return true;
    }

    /// <summary>Whether a version is in the range.</summary>
    /// <param name="version">The version, of which the major, minor and patch numbers count.</param>
    /// <returns><see langword="true"/> when the range holds the version.</returns>
    public bool Contains(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var value = Normalize(version);
        return (_minimum is null || (_includesMinimum ? value >= _minimum : value > _minimum))
            && (_maximum is null || (_includesMaximum ? value <= _maximum : value < _maximum));
    }

    private static bool TryVersion(string text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        text = text.Trim();
        var label = text.IndexOfAny(['-', '+']);
        if (label >= 0)
        {
            text = text[..label];
        }

        if (!text.Contains('.', StringComparison.Ordinal))
        {
            text += ".0";
        }

        if (!Version.TryParse(text, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
