using System.Globalization;

namespace FluentCMS.Core.Plugins;

// A SemVer 2.0 version, compared by precedence: build metadata is dropped, and a pre-release ranks below its release.
internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, IReadOnlyList<string> prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> Prerelease { get; }

    public bool IsPrerelease => Prerelease.Count > 0;

    public SemanticVersion Release => IsPrerelease ? new SemanticVersion(Major, Minor, Patch, []) : this;

    // Three parts; a bound of a NuGet range may leave out the minor and patch parts, which are then 0.
    public static SemanticVersion? Parse(string? text, bool allowShortForm = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var metadata = text.IndexOf('+', StringComparison.Ordinal);
        if (metadata >= 0)
        {
            if (!Identifiers(text[(metadata + 1)..], numeric: false, out _))
            {
                return null;
            }

            text = text[..metadata];
        }

        IReadOnlyList<string> prerelease = [];
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            if (!Identifiers(text[(dash + 1)..], numeric: true, out prerelease))
            {
                return null;
            }

            text = text[..dash];
        }

        var parts = text.Split('.');
        if (parts.Length > 3 || (parts.Length < 3 && !allowShortForm))
        {
            return null;
        }

        var numbers = new int[3];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!IsNumber(parts[index]) || !int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
            {
                return null;
            }
        }

        return new SemanticVersion(numbers[0], numbers[1], numbers[2], prerelease);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        result = result != 0 ? result : Minor.CompareTo(other.Minor);
        result = result != 0 ? result : Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        if (IsPrerelease != other.IsPrerelease)
        {
            return IsPrerelease ? -1 : 1;
        }

        for (var index = 0; index < Math.Min(Prerelease.Count, other.Prerelease.Count); index++)
        {
            result = CompareIdentifier(Prerelease[index], other.Prerelease[index]);
            if (result != 0)
            {
                return result;
            }
        }

        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public override string ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{string.Join('.', Prerelease)}" : $"{Major}.{Minor}.{Patch}";

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = IsNumber(left);
        var rightNumeric = IsNumber(right);
        if (leftNumeric && rightNumeric)
        {
            var length = left.Length.CompareTo(right.Length);
            return length != 0 ? length : string.CompareOrdinal(left, right);
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return string.CompareOrdinal(left, right);
    }

    // A numeric pre-release identifier may not have a leading zero; build metadata identifiers may.
    private static bool Identifiers(string text, bool numeric, out IReadOnlyList<string> identifiers)
    {
        var parts = text.Split('.');
        identifiers = parts;
        return parts.All(part => part.Length > 0
            && part.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            && (!numeric || !part.All(char.IsAsciiDigit) || IsNumber(part)));
    }

    private static bool IsNumber(string text) =>
        text.Length > 0 && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0');
}
