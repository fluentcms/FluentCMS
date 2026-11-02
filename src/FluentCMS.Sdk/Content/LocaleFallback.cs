namespace FluentCMS.Content;

/// <summary>Whether a missing localized value falls back to another locale on read.</summary>
public enum LocaleFallback
{
    /// <summary>Follow the fallback chain of the site.</summary>
    Chain = 0,

    /// <summary>Do not fall back (<c>none</c>).</summary>
    None = 1,
}
