using System.Net.Http.Headers;

namespace FluentCMS.Admin.Api;

// Sets the bearer token on every Management call, and the shell's site and the view's content locale unless the request
// named its own (ManagementApi says which through the request's options). It runs again on a retry, so a refreshed token
// is sent then.
internal sealed class SessionHeadersHandler(IAccessTokenSource tokens, ICurrentSite site, IContentLocale locale) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);
        if (Adds(request, ManagementHeaders.AddSessionSite))
        {
            AddUnlessSet(request, ManagementHeaders.Site, site.Site);
        }

        if (Adds(request, ManagementHeaders.AddSessionLocale))
        {
            AddUnlessSet(request, ManagementHeaders.Locale, locale.Locale);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool Adds(HttpRequestMessage request, HttpRequestOptionsKey<bool> key) =>
        !request.Options.TryGetValue(key, out var adds) || adds;

    private static void AddUnlessSet(HttpRequestMessage request, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value) && !request.Headers.Contains(name))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
