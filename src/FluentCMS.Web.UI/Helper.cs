using Microsoft.AspNetCore.Components;
using System.Web;
using Microsoft.AspNetCore.Http;
using System;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FluentCMS.Web.UI;

public static class Helper
{
    public static Guid? GetIdFromQuery(this NavigationManager? navigation)
    {
        if (navigation == null)
            return default;

        var uri = new Uri(navigation.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (Guid.TryParse(query["id"], out var id))
            return id;

        return default;
    }

    public static string? GetStringFromQuery(this NavigationManager? navigation, string key)
    {
        if (navigation == null)
            return default;

        var uri = new Uri(navigation.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (!string.IsNullOrEmpty(query[key]))
            return query[key];

        return default;
    }

    public static async Task SignInAsync(this HttpContext httpContext, UserLoginRequest request)
    {
        var scopeFactory = httpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
        using (var scope = scopeFactory.CreateScope())
        {
            var serviceProvider = scope.ServiceProvider;
            var accountClient = serviceProvider.GetRequiredService<AccountClient>();
            var loginResponseIApiResult = await accountClient.AuthenticateAsync(new UserLoginRequest()
            {
                Username = request.Username,
                Password = request.Password
            });

            var claims = new List<Claim>();


            // fill userDetails
            claims.Add(new Claim(ClaimTypes.NameIdentifier, loginResponseIApiResult.Data.UserId.ToString("D")));
            claims.Add(new Claim("token", loginResponseIApiResult.Data.Token));

            //force set header
            var httpClient = (HttpClient)accountClient.GetType().GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(accountClient);
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("bearer", loginResponseIApiResult.Data.Token);

            var userDetails = await accountClient.GetUserDetailAsync();

            claims.Add(new Claim(ClaimTypes.Name, userDetails.Data.Username));
            claims.Add(new Claim(ClaimTypes.Email, userDetails.Data.Email));

            var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            if (userDetails.Data.PhoneNumber != null)
                claims.Add(new Claim(ClaimTypes.MobilePhone, userDetails.Data.PhoneNumber));


            await httpContext?.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal);
        }
    }
}
