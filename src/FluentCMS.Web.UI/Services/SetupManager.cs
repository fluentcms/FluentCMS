using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.UI.Services;

public class SetupManager
{
    private readonly SetupClient _setupClient;
    private readonly IHttpContextAccessor _contextAccessor;

    private static bool _initialized = false;

    public SetupManager(SetupClient setupClient, AccountClient accountClient, IHttpContextAccessor contextAccessor, IServiceScopeFactory scopeFactory)
    {
        _setupClient = setupClient;
        _contextAccessor = contextAccessor;
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

        await _contextAccessor?.HttpContext?.SignInAsync(new UserLoginRequest()
        {
            Username = request.Username,
            Password = request.Password,
        });

        _initialized = response.Data;
        return response.Data;
    }

    public async Task<PageFullDetailResponse> GetSetupPage()
    {
        var response = await _setupClient.GetSetupPageAsync();

        return response.Data;
    }
}
