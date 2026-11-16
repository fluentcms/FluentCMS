namespace FluentCMS.Admin.Api;

/// <summary>A site the caller may work in, from <c>GET /me/sites</c>.</summary>
public sealed record MySiteInfo
{
    /// <summary>The id of the site.</summary>
    public required Guid Id { get; init; }

    /// <summary>The name of the site.</summary>
    public required string Name { get; init; }

    /// <summary>The slug of the site, which <c>X-Cms-Site</c> accepts as the id does.</summary>
    public required string Slug { get; init; }

    /// <summary>The default locale of the site.</summary>
    public required string DefaultLocale { get; init; }

    /// <summary>Whether this is the instance's default site.</summary>
    public required bool IsDefault { get; init; }

    /// <summary>The locales of the site, in their order.</summary>
    public required IReadOnlyList<MySiteLocale> Locales { get; init; }
}
