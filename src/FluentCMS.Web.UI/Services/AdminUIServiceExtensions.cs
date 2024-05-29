﻿using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace Microsoft.Extensions.DependencyInjection;

public static class AdminUIServiceExtensions
{
    public static IServiceCollection AddAdminUIServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAutoMapper(typeof(AdminUIServiceExtensions));

        services.AddAuthorization();
        services.AddAuthentication(options =>
        {
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        }).AddCookie();

        services.AddApiClients(configuration);
        services.AddScoped<SetupManager>();
        services.AddScoped<AuthManager>();


        //services.AddCascadingAuthenticationState();

        // add global cascade parameter for UserLogin (UserLoginResponse)
        services.AddScoped(sp =>
        {
            var authStateProvider = sp.GetRequiredService<AuthenticationStateProvider>();
            var authStateTask = authStateProvider.GetAuthenticationStateAsync();
            var authState = authStateTask.GetAwaiter().GetResult();

            if (authState?.User?.Identity?.IsAuthenticated == null)
                return default;

            var loginResponse = new UserLoginResponse
            {
                UserId = Guid.Parse(authState.User.FindFirstValue(ClaimTypes.Sid) ?? Guid.Empty.ToString()),
                Email = authState.User.FindFirstValue(ClaimTypes.Email),
                UserName = authState.User.FindFirstValue(ClaimTypes.NameIdentifier),
                Token = authState.User.FindFirstValue("jwt"),
                RoleIds = authState.User.FindAll(ClaimTypes.Role).Select(x => Guid.Parse(x.Value)).ToList()
            };
            return loginResponse;

        });


        // add global cascade parameter for Page (PageFullDetailResponse)
        services.AddScoped(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var navigationManager = sp.GetRequiredService<NavigationManager>();
            var uerLogin = sp.GetRequiredService<UserLoginResponse>();

            var pageClient = httpClientFactory.CreateApiClient<PageClient>(uerLogin);
            var pageResponse = pageClient.GetByUrlAsync(navigationManager.Uri).GetAwaiter().GetResult();

            return pageResponse.Data ?? null;
        });

        return services;
    }
}
