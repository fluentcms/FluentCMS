namespace FluentCMS.Admin.Api;

// The content locale of the view, a BCP 47 tag sent as X-Cms-Locale; the locale switcher fills it. Null is the site's default.
internal interface IContentLocale
{
    string? Locale { get; }
}
