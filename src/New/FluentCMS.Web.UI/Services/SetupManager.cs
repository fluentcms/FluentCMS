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

        var response = await _setupClient.IsInitializedAsync();

        _initialized = response.Data;

        return _initialized;
    }
}
