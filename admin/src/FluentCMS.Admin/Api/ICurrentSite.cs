namespace FluentCMS.Admin.Api;

// The site the shell shows, an id or a slug sent as X-Cms-Site; the site switcher fills it. Null is the instance's default site.
internal interface ICurrentSite
{
    string? Site { get; }
}
