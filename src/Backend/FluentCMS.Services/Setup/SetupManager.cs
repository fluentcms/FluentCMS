using FluentCMS.Services.Setup.Models;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Text.Json;

namespace FluentCMS.Services.Setup;

public class SetupManager : ISetupManager
{
    private static bool? _initialized;
    private readonly IGlobalSettingsService _globalSettingsService;
    private readonly IUserService _userService;
    private readonly IEnumerable<BaseSetupHandler> _setupHandlers;

    private const string ADMIN_TEMPLATE_PHYSICAL_PATH = "Template";

    public SetupManager(
        IGlobalSettingsService globalSettingsService,
        IHostEnvironment env,
        IEnumerable<BaseSetupHandler> setupHandlers,
        IUserService userService)
    {
        if (env == null)
            throw new AppException(ExceptionCodes.SetupSettingsHostingEnvironmentIsNull);

        _globalSettingsService = globalSettingsService;
        _setupHandlers = setupHandlers;
        _userService = userService;
    }

    public async Task<bool> IsInitialized()
    {
        // Check initialization status only once, in the beginning
        if (!_initialized.HasValue)
            _initialized = await InitCondition();

        return _initialized.Value;
    }

    public async Task<bool> Start(SetupModel request)
    {
        // Check if this is the first time setup or not
        if (_initialized.HasValue && _initialized.Value)
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);

        if (await InitCondition())
        {
            _initialized = true;
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);
        }

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


        apiTokenHandler
        .SetNext(superAdminHandler)
        .SetNext(globalSettingsHandler)
        .SetNext(layoutHandler)
        .SetNext(blockHandler)
        .SetNext(pluginHandler)
        .SetNext(siteHandler)
        .SetNext(pageHandler)
        .SetNext(contentTypeHandler);

        await apiTokenHandler.Handle(setupContext);

        _initialized = true;

        return true;
    }

    public async Task Reset()
    {
        await _globalSettingsService.Reset();
        _initialized = false;
    }

    private async Task<bool> InitCondition()
    {
        // Check if there is any user in the system.
        // Also, check if the system is already initialized or not.
        return await _globalSettingsService.Get() != null && await _userService.Any();
    }
}
