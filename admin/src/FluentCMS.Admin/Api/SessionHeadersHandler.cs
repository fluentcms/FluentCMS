using System.Net.Http.Headers;

namespace FluentCMS.Admin.Api;

// Sets the bearer token, the shell's site and the view's content locale on every Management call. A site or locale the
// request names itself is kept. It runs again on a retry, so a refreshed token is sent then.
internal sealed class SessionHeadersHandler(IAccessTokenSource tokens, ICurrentSite site, IContentLocale locale) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);
        AddUnlessSet(request, ManagementHeaders.Site, site.Site);
        AddUnlessSet(request, ManagementHeaders.Locale, locale.Locale);
        return await base.SendAsync(request, cancellationToken);
    }

    private static void AddUnlessSet(HttpRequestMessage request, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value) && !request.Headers.Contains(name))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
