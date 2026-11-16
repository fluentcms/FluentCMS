namespace FluentCMS.Admin.Api;

/// <summary>A locale of a site, as <see cref="MySiteInfo.Locales"/> lists it.</summary>
public sealed record MySiteLocale
{
    /// <summary>The BCP 47 tag.</summary>
    public required string Code { get; init; }

    /// <summary>The position of the locale in the site's order.</summary>
    public required int Position { get; init; }

    /// <summary>Whether the locale is enabled.</summary>
    public required bool Enabled { get; init; }
}
