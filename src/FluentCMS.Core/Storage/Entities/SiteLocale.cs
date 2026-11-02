namespace FluentCMS.Core.Storage.Entities;

// A row of cms_site_locale: one locale a site allows. The key is (site, code).
public sealed class SiteLocale
{
    public Guid SiteId { get; set; }

    // A BCP 47 tag in the casing of LocaleTag.Normalize.
    public string Code { get; set; } = "";

    // Orders the locales of a site for display.
    public int Position { get; set; }

    // JSON array of the codes to try, in order, after this one. Null means the implicit chain: the tag with its last
    // subtag removed until one is allowed, then the site's default locale.
    public string? Fallbacks { get; set; }

    // A disabled locale is kept, but is not accepted on a request and is skipped in a chain.
    public bool Enabled { get; set; } = true;
}
