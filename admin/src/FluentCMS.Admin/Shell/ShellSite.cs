using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Shell;

// The site the shell shows, which the API client sends as X-Cms-Site by its id, and the content locale of the views, sent as
// X-Cms-Locale. Apart from AdminContext, which reads through the client, so that the client does not depend on the context
// that uses it. A null locale is the site's default, which a request then does not name.
internal sealed class ShellSite : ICurrentSite, IContentLocale
{
    public MySiteInfo? Current { get; set; }

    public string? ContentLocale { get; set; }

    public string? Site => Current?.Id.ToString("D");

    public string? Locale => ContentLocale;
}
