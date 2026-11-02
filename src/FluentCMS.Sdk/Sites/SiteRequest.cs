namespace FluentCMS.Sites;

/// <summary>What a request says about the site it addresses. The transport fills it in; resolving it does not depend on HTTP.</summary>
public sealed record SiteRequest
{
    /// <summary>The site that the API key of the request is bound to, or <see langword="null"/> when the request has no key or the key is not bound. It wins over everything else.</summary>
    public SiteId? ApiKeySite { get; init; }

    /// <summary>The value of the <c>X-Cms-Site</c> header: a site id or a site slug.</summary>
    public string? Header { get; init; }

    /// <summary>The value of the <c>site</c> parameter: a site id or a site slug.</summary>
    public string? Parameter { get; init; }

    /// <summary>The host name of the request, for an <see cref="ISiteHostResolver"/>.</summary>
    public string? Host { get; init; }
}
