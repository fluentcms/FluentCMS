using FluentCMS.Services.Models;
using FluentCMS.Services.Setup.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Services.Setup;

public class SetupManager : ISetupManager
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IEnumerable<BaseSetupHandler> _setupHandlers;
    private readonly ServerSettings _serverSettings;
    private readonly IGlobalSettingsService _globalSettingsService;

    private const string ADMIN_TEMPLATE_PHYSICAL_PATH = "Template";

    public SetupManager(
        IHostEnvironment hostEnvironment,
        IEnumerable<BaseSetupHandler> setupHandlers,
        IOptionsMonitor<ServerSettings> serverSettingsOptions,
        IGlobalSettingsService globalSettingsService)
    {
        _hostEnvironment = hostEnvironment;
        _setupHandlers = setupHandlers;
        _serverSettings = serverSettingsOptions.CurrentValue;

        if (_hostEnvironment == null)
            throw new AppException(ExceptionCodes.SetupSettingsHostingEnvironmentIsNull);
        _globalSettingsService = globalSettingsService;
    }

    public async Task<bool> IsInitialized()
    {
        // Check initialization status only once, in the beginning
        return _serverSettings.IsInitialized;
    }

    public async Task<bool> Start(SetupModel request)
    {
        // Check if this is the first time setup or not
        if (_serverSettings.IsInitialized)
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);

        var manifestFile = Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, "manifest.json");

        if (!System.IO.File.Exists(manifestFile))
            throw new AppException("manifest.json doesn't exist!");

        var jsonSerializerOptions = new JsonSerializerOptions { };
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        var adminTemplate = await JsonSerializer.DeserializeAsync<AdminTemplate>(System.IO.File.OpenRead(manifestFile), jsonSerializerOptions) ??
            throw new AppException("Failed to deserialize manifest.json");

        var setupContext = new SetupContext()
        {
            SetupRequest = request,
            GlobalSettings = adminTemplate.GlobalSettings,
            AdminTemplate = adminTemplate
        };

        var apiTokenHandler = _setupHandlers.Single(x => x.Step == SetupSteps.ApiToken);
        var superAdminHandler = _setupHandlers.Single(x => x.Step == SetupSteps.SuperAdmin);
        var globalSettingsHandler = _setupHandlers.Single(x => x.Step == SetupSteps.GlobalSettings);
        var layoutHandler = _setupHandlers.Single(x => x.Step == SetupSteps.Layout);
        var blockHandler = _setupHandlers.Single(x => x.Step == SetupSteps.Block);
        var pluginHandler = _setupHandlers.Single(x => x.Step == SetupSteps.Plugin);
        var siteHandler = _setupHandlers.Single(x => x.Step == SetupSteps.Site);
        var pageHandler = _setupHandlers.Single(x => x.Step == SetupSteps.Page);
        var contentTypeHandler = _setupHandlers.Single(x => x.Step == SetupSteps.ContentType);
        var setInitializedHandler = _setupHandlers.Single(x => x.Step == SetupSteps.SetInitialized);

        apiTokenHandler
        .SetNext(superAdminHandler)
        .SetNext(globalSettingsHandler)
        .SetNext(layoutHandler)
        .SetNext(pluginHandler)
        .SetNext(siteHandler)
        .SetNext(blockHandler)
        .SetNext(pageHandler)
        .SetNext(contentTypeHandler)
        .SetNext(setInitializedHandler);

        await apiTokenHandler.Handle(setupContext);

        return true;
    }

    public async Task Reset()
    {
        await _globalSettingsService.Reset();

        var appSettingsFilePath = Path.Combine($"appsettings.{_hostEnvironment.EnvironmentName}.json");
        var text = await System.IO.File.ReadAllTextAsync(appSettingsFilePath);
        var jsonNode = JsonNode.Parse(text)!;

        // set api token to appsettings.json
        jsonNode!["ServerSettings"]!["IsInitialized"] = false;

        var output = JsonSerializer.Serialize(jsonNode, new JsonSerializerOptions() { WriteIndented = true });
        System.IO.File.WriteAllText(appSettingsFilePath, output);
    }
}
