using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.UI.Services;

public class SetupManager
{
    private readonly SetupClient _setupClient;
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly AccountClient _accountClient;
    private readonly IServiceScopeFactory _scopeFactory;

    private static bool _initialized = false;

    public SetupManager(SetupClient setupClient, AccountClient accountClient, IHttpContextAccessor contextAccessor, IServiceScopeFactory scopeFactory)
    {
        _setupClient = setupClient;
        _accountClient = accountClient;
        _contextAccessor = contextAccessor;
        _scopeFactory = scopeFactory;
    }
    public async Task<bool> IsInitialized()
    {
        if (_initialized)
            return _initialized;

        var initResponse = await _setupClient.IsInitializedAsync();

        _initialized = initResponse.Data;

        return _initialized;
    }


    public async Task<bool> Start(SetupRequest request)
    {
        if (_initialized)
            return _initialized;

        var initResponse = await _setupClient.IsInitializedAsync();

        _initialized = initResponse.Data;

        if (_initialized)
            return _initialized;

        var response = await _setupClient.StartAsync(request);



        var claims = new List<Claim>();

        using (var scope = _scopeFactory.CreateScope())
        {
            var serviceProvider = scope.ServiceProvider;

            var loginResponseIApiResult = await _accountClient.AuthenticateAsync(new UserLoginRequest()
            {
                Username = request.Username,
                Password = request.Password
            });

            // fill userDetails
            claims.Add(new Claim(ClaimTypes.NameIdentifier, loginResponseIApiResult.Data.UserId.ToString("D")));
            claims.Add(new Claim("token", loginResponseIApiResult.Data.Token));

            //force set header
            var httpClient = (HttpClient)_accountClient.GetType().GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_accountClient);
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("bearer", loginResponseIApiResult.Data.Token);


            var userDetails = await _accountClient.GetUserDetailAsync();

            claims.Add(new Claim(ClaimTypes.Name, userDetails.Data.Username));
            claims.Add(new Claim(ClaimTypes.Email, userDetails.Data.Email));
            if (userDetails.Data.PhoneNumber != null)
                claims.Add(new Claim(ClaimTypes.MobilePhone, userDetails.Data.PhoneNumber));
        }

        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await _contextAccessor.HttpContext?.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal);

        _initialized = response.Data;
        return response.Data;
    }

    public async Task<PageFullDetailResponse> GetSetupPage()
    {
        var response = await _setupClient.GetSetupPageAsync();

        return response.Data;
    }
}
