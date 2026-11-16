namespace FluentCMS.Admin.Configuration;

/// <summary>The branding of the admin, from the <c>branding</c> member of <c>config.json</c>.</summary>
public sealed record AdminBranding
{
    /// <summary>The product name shown in the title and the shell; <c>FluentCMS</c> when not set.</summary>
    public string Name { get; init; } = "FluentCMS";

    /// <summary>The logo, an absolute http or https URL or a path relative to the admin's base; the built-in logo when <see langword="null"/>.</summary>
    public Uri? LogoUrl { get; init; }

    /// <summary>The favicon, an absolute http or https URL or a path relative to the admin's base; the built-in one when <see langword="null"/>.</summary>
    public Uri? FaviconUrl { get; init; }

    /// <summary>The accent color as a hex color such as <c>#0f6cbd</c>; the theme's own when <see langword="null"/>.</summary>
    public string? AccentColor { get; init; }
}
