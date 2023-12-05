﻿using Amazon.Runtime.Internal;
using FluentCMS.Api.Models;
using FluentCMS.Services.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace FluentCMS.Web.UI;

public class AuthStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HttpClient _httpClient;
    private ClaimsPrincipal CurrentUser { get; set; }

    public AuthStateProvider(IHttpContextAccessor httpContextAccessor, HttpClient httpClient)
    {
        _httpContextAccessor = httpContextAccessor;
        _httpClient = httpClient;
        CurrentUser = GetAnonymous();
    }

    private static ClaimsPrincipal GetAnonymous()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Sid, "0"),
            new Claim(ClaimTypes.Name, "Anonymous"),
            new Claim(ClaimTypes.Role, "Anonymous")
        }, null);
        return new ClaimsPrincipal(identity);
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var result = await _httpClient.PostAsJsonAsync("/api/Account/Authenticate", request);
        return new Task<AuthenticationState>(() => new AuthenticationState(CurrentUser));
    }

    //public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    //{

    //    var result = await _httpClient.PostAsJsonAsync("/api/Account/Authenticate", request);

    //    // fetch access-token from cookies
    //    var accessToken = _httpContextAccessor.HttpContext?.Request.Cookies["access-token"];

    //    // if no access-token in cookies
    //    if (string.IsNullOrEmpty(accessToken))
    //    {
    //        //fail auth
    //        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));
    //    }

    //    // get claims from access-token
    //    var claims = new JsonWebToken(accessToken).Claims;

    //    // authenticated with access-token
    //    return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "access-token"))));
    //}

    // Authenticate
    //public async Task<(bool result,List<Error> errors)> Authenticate(UserAuthenticateRequest request)
    //{
    //    // TODO: NSwag generated Clients are Causing AntiForgeryToken related Errors
    //    var result = await _httpClient.PostAsJsonAsync("/api/Account/Authenticate", request);

    //    // check http status
    //    if (result.IsSuccessStatusCode)
    //    {
    //        var authResult = await result.Content.ReadFromJsonAsync<ApiResult<UserAuthenticateDto>>(); // parse

    //        // check authResult is not null
    //        if (authResult is null)
    //        {
    //            return (false, []);
    //        }

    //        // check authResult Errors is empty
    //        if (authResult.Errors is not null && authResult.Errors.Count > 0)
    //        {
    //            return (false,authResult.Errors);
    //        }

    //        // set access-token
    //        _httpContextAccessor.HttpContext!.Response.Cookies.Append("access-token", authResult.Data!.Token, new CookieOptions
    //        {
    //            // TODO: Add Expiration to result
    //        });

    //        return (true, []);
    //    }

    //    return (false, []);
    //}
}
