using FluentCMS.Sites;

namespace FluentCMS.Localization;

/// <summary>What a request says about its locale. The transport fills it in; resolving it does not depend on HTTP.</summary>
/// <param name="Site">The site the request addresses, as <see cref="ISiteResolver"/> resolved it.</param>
public sealed record LocaleRequest(SiteId Site)
{
    /// <summary>The value of the <c>locale</c> parameter.</summary>
    public string? Parameter { get; init; }

    /// <summary>The value of the <c>X-Cms-Locale</c> header.</summary>
    public string? Header { get; init; }
}
