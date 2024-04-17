using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using BitzArt.Blazor.Cookies;
using FluentCMS.Web.ApiClients;

namespace Microsoft.Extensions.DependencyInjection;

public class HttpClientHeaderHandler(ICookieService cookieService):DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (cookieService.GetAsync(nameof(UserLoginResponse)).GetAwaiter().GetResult() is { } cookie &&
            JsonSerializer.Deserialize<UserLoginResponse>(HttpUtility.UrlDecode(cookie.Value)) is { } response)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", response.Token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
