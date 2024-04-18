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
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        //use: Service

        var cookieService = serviceProvider.GetRequiredService<ICookieService>();
        var cookie = await cookieService.GetAsync(nameof(UserLoginResponse));

        //use: httpContext
        var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        // if httpContext is available, and we have a valid auth cookie
        if (contextAccessor.HttpContext is var httpContext && httpContext != null && httpContext.Request.TryGetJsonCookie<UserLoginResponse>(nameof(UserLoginResponse), out var userLoginResponse))
        {
            string Hash(string value)
            {
                return Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(value)));
            }


            Debug.WriteLine($"tokenReturnedByCookieService={Hash(cookie.Value??"")} and tokenReturnedByHttpContext={Hash(userLoginResponse.Token)}", "HttpClientHeaderHandler");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userLoginResponse.Token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
