using FluentCMS.Web.ApiClients;

namespace FluentCMS.Web.UI.Services;

public class SetupManager
{
    private readonly SetupClient _setupClient;

    private static bool _initialized = false;

    public SetupManager(SetupClient setupClient)
    {
        _setupClient = setupClient;
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

        _initialized = response.Data;
        return response.Data;
    }

    public async Task<PageFullDetailResponse> GetSetupPage()
    {
        var response = await _setupClient.GetSetupPageAsync();

        return response.Data;
    }
}
