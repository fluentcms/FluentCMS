namespace FluentCMS.Core.Storage.Entities;

// A row of cms_site. Exactly one site is the default; it is seeded, and cannot be deleted.
public sealed class Site
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    // Unique, lower case; what the X-Cms-Site header and the site parameter may name besides the id.
    public string Slug { get; set; } = "";

    // A code in this site's cms_site_locale rows.
    public string DefaultLocale { get; set; } = "";

    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
