using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace FluentCMS.Web.Api;

public class SetupManager
{
    private static bool? _initialized;

    private readonly SetupSettings _setupSettings;
    private readonly IGlobalSettingsService _globalSettingsService;
    private readonly IUserService _userService;
    private readonly IAppTemplateService _appTemplateService;
    private readonly IHostEnvironment _env;
    private readonly string _templatesPhysicalPath;

    public SetupManager(
        IConfiguration configuration,
        IGlobalSettingsService globalSettingsService,
        IUserService userService,
        IAppTemplateService appTemplateService,
        IHostEnvironment env)
    {
        _setupSettings = configuration.GetInstance<SetupSettings>("SetupSettings") ??
            throw new AppException(ExceptionCodes.SetupSettingsNotDefined);

        if (_setupSettings.TemplatesPath == null)
            throw new AppException(ExceptionCodes.SetupSettingsTemplatesPathNotDefined);

        if (env == null)
            throw new AppException(ExceptionCodes.SetupSettingsHostingEnvironmentIsNull);

        _templatesPhysicalPath = Path.Combine(env.ContentRootPath, _setupSettings.TemplatesPath);

        if (!Directory.Exists(_templatesPhysicalPath))
            throw new AppException(ExceptionCodes.SetupSettingsTemplatesFolderNotFound);

        _globalSettingsService = globalSettingsService;
        _userService = userService;
        _appTemplateService = appTemplateService;
        _env = env;
    }

    public async Task<bool> IsInitialized()
    {
        // Check initialization status only once, in the beginning
        if (!_initialized.HasValue)
            _initialized = await InitCondition();

        return _initialized.Value;
    }

    public async Task<GlobalSettings> Start(string username, string email, string password)
    {
        // Check if this is the first time setup or not
        if (_initialized.HasValue && _initialized.Value)
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);

        if (await InitCondition())
        {
            _initialized = true;
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);
        }

        await InitializeSuperAdmin(username, email, password);

        await InitializeAppTemplates();

        var globalSettings = await InitializeGlobalSettings(username);

        _initialized = true;

        return globalSettings;
    }

    public async Task Reset()
    {
        await _globalSettingsService.Reset();
        _initialized = false;
    }

    #region Private

    private async Task InitializeSuperAdmin(string username, string email, string password)
    {
        // Creating super admin user
        var superAdmin = new User
        {
            UserName = username,
            Email = email,
        };

        await _userService.Create(superAdmin, password);
    }

    private async Task<GlobalSettings> InitializeGlobalSettings(string username)
    {
        // Creating default global settings
        var settings = new GlobalSettings
        {
            SuperUsers = [username],
        };

        return await _globalSettingsService.Init(settings);
    }

    private async Task InitializeAppTemplates()
    {
        foreach (var folder in Directory.GetDirectories(_templatesPhysicalPath))
        {

            var appTemplateFile = Path.Combine(folder, "app-template.json");

            // check if app.json file exists
            // if not, skip this folder
            if (!File.Exists(appTemplateFile))
                continue;

            // loading json data into AppTemplate object
            var appTemplate = await JsonSerializer.DeserializeAsync<AppTemplate>(File.OpenRead(appTemplateFile));

            if (appTemplate == null)
                continue;

            await _appTemplateService.Create(appTemplate);
        }
    }

    private async Task<bool> InitCondition()
    {
        // Check if there is any user in the system.
        // Also, check if the system is already initialized or not.
        return await _globalSettingsService.Get() != null && await _userService.Any();
    }

    #endregion
}
