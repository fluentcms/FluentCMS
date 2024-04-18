namespace FluentCMS.Web.UI.Services;

public class SetupManager
{
    private readonly SetupClient _setupClient;
    private readonly AuthStateProvider _authStateProvider;

    private static bool _initialized = false;

    public SetupManager(SetupClient setupClient, AuthStateProvider authStateProvider)
    {
        _setupClient = setupClient;
        _authStateProvider = authStateProvider;
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
        var response = await _setupClient.GetSetupPageAsync();

        return response.Data;
    }
}
