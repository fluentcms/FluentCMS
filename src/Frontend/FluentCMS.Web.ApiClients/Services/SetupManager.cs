namespace FluentCMS.Web.ApiClients.Services;
public class SetupManager(ApiClientFactory apiClientFactory)
{
    private static bool _initialized = false;

    public async Task<bool> IsInitialized()
    {
        if (_initialized)
            return _initialized;

        var initResponse = await apiClientFactory.Setup.IsInitializedAsync();

        _initialized = initResponse.Data;

        return _initialized;
    }

    public async Task<bool> Start(SetupRequest request)
    {
        if (_initialized)
            return _initialized;

        var initResponse = await apiClientFactory.Setup.IsInitializedAsync();

        _initialized = initResponse.Data;

        if (_initialized)
            return _initialized;

        var response = await apiClientFactory.Setup.StartAsync(request);

        _initialized = response.Data;
        return response.Data;
    }
}

