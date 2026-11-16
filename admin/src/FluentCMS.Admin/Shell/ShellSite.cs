using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Shell;

// The site the shell shows, which the API client sends as X-Cms-Site by its id. Apart from AdminContext, which reads through
// the client, so that the client does not depend on the context that uses it. The locale switcher (A-17) fills the locale;
// until then the views read the site's default locale.
internal sealed class ShellSite : ICurrentSite, IContentLocale
{
    public MySiteInfo? Current { get; set; }

    public string? Site => Current?.Id.ToString("D");

    public string? Locale => null;
}
