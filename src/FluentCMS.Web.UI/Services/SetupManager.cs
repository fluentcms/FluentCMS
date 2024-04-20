namespace FluentCMS.Web.UI.Services;

public class SetupManager
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AuthStateProvider _authStateProvider;

    private static bool _initialized = false;

    public SetupManager(IHttpClientFactory httpClientFactory, AuthStateProvider authStateProvider)
    {
        _httpClientFactory = httpClientFactory;
        _authStateProvider = authStateProvider;
    }

    public async Task<bool> IsInitialized()
    {
        if (_initialized)
            return _initialized;

        var initResponse = await _httpClientFactory.GetClient<SetupClient>().IsInitializedAsync();

        _initialized = initResponse.Data;

        return _initialized;
    }


    public async Task<bool> Start(SetupRequest request)
    {
        if (_initialized)
            return _initialized;

        var setupClient = _httpClientFactory.GetClient<SetupClient>();
        var initResponse = await setupClient.IsInitializedAsync();

        _initialized = initResponse.Data;

        if (_initialized)
            return _initialized;

        var response = await setupClient.StartAsync(request);

        await _authStateProvider.Login(new UserLoginRequest()
        {
            Username = request.Username,
            Password = request.Password,
        });

        _initialized = response.Data;
        return response.Data;
    }

    public async Task<PageFullDetailResponse> GetSetupPage()
    {
        var response = await _httpClientFactory.GetClient<SetupClient>().GetSetupPageAsync();

        return response.Data;
    }
}
