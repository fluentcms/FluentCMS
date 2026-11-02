using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage.Entities;

namespace FluentCMS.Core.Storage;

// The default site, its locale and its Public role, which the system migration inserts. A change here is a model
// change, so the snapshot tests fail until a migration carries it.
internal static class SiteSeed
{
    // The date the first release of the model was cut; a seed needs a fixed value.
    private static readonly DateTimeOffset SeededAt = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public static Site Site() => new()
    {
        Id = DefaultSite.Id,
        Name = DefaultSite.Name,
        Slug = DefaultSite.Slug,
        DefaultLocale = DefaultSite.Locale,
        IsDefault = true,
        CreatedAt = SeededAt,
    };

    public static SiteLocale Locale() => new() { SiteId = DefaultSite.Id, Code = DefaultSite.Locale, Position = 0, Enabled = true };

    public static Role PublicRole() => RoleSeed.PublicOf(DefaultSite.PublicRole, DefaultSite.Id);
}
