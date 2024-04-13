using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Web;

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
        using var scope = scopeFactory.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var accountClient = serviceProvider.GetRequiredService<AccountClient>();

        //get access token
        var loginResponseIApiResult = await accountClient.AuthenticateAsync(new UserLoginRequest()
        {
            Username = request.Username,
            Password = request.Password
        });

        var claims = new List<Claim>();

        //force set auth header
        // todo: find a better solution for this
        var httpClient = (HttpClient)accountClient.GetType().GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(accountClient)!;
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("bearer", loginResponseIApiResult.Data.Token);

        // get UserAccountDetails
        var userDetails = await accountClient.GetUserDetailAsync();

        // login
        claims.BuildUserClaims(loginResponseIApiResult, userDetails);

        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal);
    }

    private static void BuildUserClaims(this List<Claim> claims, UserLoginResponseIApiResult loginResponseIApiResult,
        UserDetailResponseIApiResult userDetails)
    {
        claims.BuildUserClaims(loginResponseIApiResult);
        claims.BuildUserClaims(userDetails);
    }

    private static void BuildUserClaims(this List<Claim> claims, UserDetailResponseIApiResult userDetails)
    {
        claims.Add(new Claim(ClaimTypes.Name, userDetails.Data.Username));
        claims.Add(new Claim(ClaimTypes.Email, userDetails.Data.Email));
        if (userDetails.Data.PhoneNumber != null)
            claims.Add(new Claim(ClaimTypes.MobilePhone, userDetails.Data.PhoneNumber));
    }

    private static void BuildUserClaims(this List<Claim> claims, UserLoginResponseIApiResult loginResponseIApiResult)
    {
        claims.Add(new Claim(ClaimTypes.NameIdentifier, loginResponseIApiResult.Data.UserId.ToString("D")));
        claims.Add(new Claim("token", loginResponseIApiResult.Data.Token));
    }
}
