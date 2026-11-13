using System.Collections.Frozen;

namespace FluentCMS.Settings;

/// <summary>What <see cref="ISettings{T}.ResolveAsync"/> gives: the settings that apply to a site, and which of their secrets the site takes from the instance defaults, from one read.</summary>
/// <typeparam name="T">The settings class.</typeparam>
public sealed class ResolvedSettings<T>
    where T : class
{
    /// <summary>Creates the result of a read.</summary>
    /// <param name="value">The settings that apply to the site.</param>
    /// <param name="inheritedSecrets">The names of the secret properties whose values come from the instance defaults.</param>
    public ResolvedSettings(T value, IEnumerable<string> inheritedSecrets)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(inheritedSecrets);
        Value = value;
        InheritedSecrets = inheritedSecrets.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>The settings that apply to the site, as <see cref="ISettings{T}.GetAsync"/> gives them.</summary>
    public T Value { get; }

    /// <summary>
    /// The secret properties that the site does not set itself, by the name of the property as <c>nameof</c> gives it. Their
    /// values in <see cref="Value"/> are those of the instance defaults at the time of the read, or the code defaults when
    /// there are no instance defaults. A secret property that is not named here is the site's own, also when its value is
    /// <see langword="null"/>.
    /// </summary>
    public IReadOnlySet<string> InheritedSecrets { get; }
}
