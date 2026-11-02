namespace FluentCMS.Localization;

/// <summary>The locales to read a localized field from, in order: the requested locale first, then each fallback.</summary>
public sealed class FallbackChain
{
    /// <summary>Creates a chain.</summary>
    /// <param name="locales">The locales, requested locale first. There must be at least one.</param>
    /// <exception cref="ArgumentException"><paramref name="locales"/> is empty.</exception>
    public FallbackChain(IEnumerable<string> locales)
    {
        ArgumentNullException.ThrowIfNull(locales);
        Locales = [.. locales.Distinct(StringComparer.OrdinalIgnoreCase)];
        if (Locales.Count == 0)
        {
            throw new ArgumentException("A fallback chain needs at least the requested locale.", nameof(locales));
        }
    }

    /// <summary>The locales, requested locale first, without duplicates.</summary>
    public IReadOnlyList<string> Locales { get; }

    /// <summary>The requested locale.</summary>
    public string Requested => Locales[0];

    /// <summary>The locales to try after the requested one, in order.</summary>
    public IEnumerable<string> Fallbacks => Locales.Skip(1);

    /// <summary>A chain of the requested locale alone, which is what <c>fallback=none</c> reads.</summary>
    /// <param name="locale">The requested locale.</param>
    /// <returns>The chain.</returns>
    public static FallbackChain Only(string locale) => new([locale]);
}
