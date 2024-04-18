using System.Diagnostics;
using BitzArt.Blazor.Cookies;
using FluentCMS.Web.ApiClients;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.DependencyInjection;

public class HttpClientHeaderHandler(IServiceProvider serviceProvider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        // if httpContext is available, and we have a valid auth cookie
        if (contextAccessor.HttpContext is var httpContext && httpContext != null && httpContext.Request.TryGetJsonCookie<UserLoginResponse>(nameof(UserLoginResponse), out var userLoginResponse))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userLoginResponse.Token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
