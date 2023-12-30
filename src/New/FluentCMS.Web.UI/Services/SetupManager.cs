using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Services;

public class SetupManager
{
    private readonly SetupClient _setupClient;
    private readonly NavigationManager _navigationManager;

    private static bool _initialized = false;

    public SetupManager(SetupClient setupClient, NavigationManager navigationManager)
    {
        _setupClient = setupClient;
        _navigationManager = navigationManager;
    }

    public async Task<bool> IsInitialized()
    {
        if (_initialized)
            return _initialized;

        var response = await _setupClient.IsInitializedAsync();

        _initialized = response.Data;

        return _initialized;
    }

    public async Task<bool> Start(SetupRequest request)
    {
        var response = await _setupClient.StartAsync(request);
        return response.Data;
    }

    public void NavigateToSetupRoute()
    {
        _navigationManager.NavigateTo("/setup", true);
    }
}
