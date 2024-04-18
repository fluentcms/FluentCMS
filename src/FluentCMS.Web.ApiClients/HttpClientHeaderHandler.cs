using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;

namespace Microsoft.Extensions.DependencyInjection;

public class HttpClientHeaderHandler(IServiceProvider serviceProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        // if httpContext is available, and we have a valid auth cookie
        if (contextAccessor.HttpContext is var httpContext && httpContext != null && httpContext.TryGetCookieAsJson<UserLoginResponse>(out var userLoginResponse))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userLoginResponse!.Token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
