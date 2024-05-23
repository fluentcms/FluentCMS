namespace FluentCMS.Web.UI.Services;

public class SetupManager
{
    private readonly IHttpClientFactory _httpClientFactory;

    private static bool _initialized = false;

    public SetupManager(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> IsInitialized()
    {
        if (_initialized)
            return _initialized;

        var httpClient = _httpClientFactory.CreateApiClient();
        var setupClient = new SetupClient(httpClient);
        var initResponse = await setupClient.IsInitializedAsync();

        _initialized = initResponse.Data;

        return _initialized;
    }

    public async Task<bool> Start(SetupRequest request)
    {
        if (_initialized)
            return _initialized;

        var httpClient = _httpClientFactory.CreateApiClient();
        var setupClient = new SetupClient(httpClient);

        var initResponse = await setupClient.IsInitializedAsync();

        _initialized = initResponse.Data;

        if (_initialized)
            return _initialized;

        var response = await setupClient.StartAsync(request);

        _initialized = response.Data;
        return response.Data;
    }
}
