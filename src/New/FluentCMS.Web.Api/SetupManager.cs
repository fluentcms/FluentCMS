using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace FluentCMS.Web.Api;

public class SetupManager
{
    private static bool? _initialized;

    private readonly SetupSettings _setupSettings;
    private readonly ISiteService _siteService;
    private readonly IGlobalSettingsService _globalSettingsService;
    private readonly IPluginDefinitionService _pluginDefinitionService;
    private readonly ILayoutService _layoutService;
    private readonly IUserService _userService;
    private readonly IAppTemplateService _appTemplateService;
    private readonly IHostEnvironment _env;
    private readonly string _appTemplatePhysicalPath;
    private readonly string _siteTemplatePhysicalPath;
    private readonly string _adminTemplatePhysicalPath;

    public SetupManager(
        IConfiguration configuration,
        ISiteService siteService,
        IGlobalSettingsService globalSettingsService,
        IPluginDefinitionService pluginDefinitionService,
        ILayoutService layoutService,
        IUserService userService,
        IAppTemplateService appTemplateService,
        IHostEnvironment env)
    {
        if (env == null)
            throw new AppException(ExceptionCodes.SetupSettingsHostingEnvironmentIsNull);

        _setupSettings = configuration.GetInstance<SetupSettings>("SetupSettings") ??
            throw new AppException(ExceptionCodes.SetupSettingsNotDefined);

        if (_setupSettings.AppTemplatePath == null)
            throw new AppException(ExceptionCodes.SetupSettingsAppTemplatesPathNotDefined);

        _appTemplatePhysicalPath = Path.Combine(env.ContentRootPath, _setupSettings.AppTemplatePath);

        if (!Directory.Exists(_appTemplatePhysicalPath))
            throw new AppException(ExceptionCodes.SetupSettingsAppTemplatesFolderNotFound);

        if (_setupSettings.SiteTemplatePath == null)
            throw new AppException(ExceptionCodes.SetupSettingsSiteTemplatesPathNotDefined);

        _siteTemplatePhysicalPath = Path.Combine(env.ContentRootPath, _setupSettings.SiteTemplatePath);

        if (!Directory.Exists(_siteTemplatePhysicalPath))
            throw new AppException(ExceptionCodes.SetupSettingsSiteTemplatesFolderNotFound);


        if (_setupSettings.AdminTemplatePath == null)
            throw new AppException(ExceptionCodes.SetupSettingsAdminTemplatesPathNotDefined);

        _adminTemplatePhysicalPath = Path.Combine(env.ContentRootPath, _setupSettings.AdminTemplatePath);

        if (!Directory.Exists(_adminTemplatePhysicalPath))
            throw new AppException(ExceptionCodes.SetupSettingsAdminTemplatesFolderNotFound);
        _siteService = siteService;
        _globalSettingsService = globalSettingsService;
        _pluginDefinitionService = pluginDefinitionService;
        _layoutService = layoutService;
        _userService = userService;
        _appTemplateService = appTemplateService;
        _siteTemplatePhysicalPath = _setupSettings.SiteTemplatePath;
        _adminTemplatePhysicalPath = _setupSettings.AdminTemplatePath;

        _env = env;
    }

    public async Task<bool> IsInitialized()
    {
        // Check initialization status only once, in the beginning
        if (!_initialized.HasValue)
            _initialized = await InitCondition();

        return _initialized.Value;
    }

    public async Task<GlobalSettings> Start(string username, string email, string password, string domain)
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

        await InitializeAdminUI(domain);

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
        foreach (var folder in Directory.GetDirectories(_appTemplatePhysicalPath))
        {

            var appTemplateFile = Path.Combine(folder, "manifest.json");

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

    private async Task InitializeAdminUI(string domain)
    {
        var appTemplateFile = Path.Combine(_adminTemplatePhysicalPath, "manifest.json");
        if (!File.Exists(appTemplateFile))
            return;

        var adminTemplate = await JsonSerializer.DeserializeAsync<AdminTemplate>(File.OpenRead(appTemplateFile));
        if (adminTemplate == null)
            return;

        adminTemplate.Site.Urls = [domain];
        var site = await InitSite(adminTemplate.Site);

        await InitPluginDefinitions(adminTemplate.PluginDefinitions);
        await InitLayouts(site.Id, adminTemplate.Layouts);
    }

    private async Task<Site> InitSite(Site site)
    {
        return await _siteService.Create(site);
    }

    private async Task<List<PluginDefinition>> InitPluginDefinitions(List<PluginDefinition> pluginDefinitions)
    {
        var pluginDefList = new List<PluginDefinition>();

        foreach (var pluginDef in pluginDefinitions)
        {
            pluginDefList.Add(await _pluginDefinitionService.Create(pluginDef));
        }

        return pluginDefList;
    }

    private async Task<List<Layout>> InitLayouts(Guid siteId, List<Layout> layouts)
    {
        var layoutList = new List<Layout>();

        foreach (var layout in layouts)
        {
            layout.Body = File.ReadAllText(Path.Combine(_adminTemplatePhysicalPath, $"{layout.Name}.body.html"));
            layout.Head = File.ReadAllText(Path.Combine(_adminTemplatePhysicalPath, $"{layout.Name}.head.html"));
            layout.SiteId = siteId;
            layoutList.Add(await _layoutService.Create(layout));
        }

        return layoutList;
    }

    private async Task<bool> InitCondition()
    {
        // Check if there is any user in the system.
        // Also, check if the system is already initialized or not.
        return await _globalSettingsService.Get() != null && await _userService.Any();
    }

    #endregion
}

public class AdminTemplate
{
    public Site Site { get; set; } = default!;
    public List<Layout> Layouts { get; set; } = [];
    public List<PluginDefinition> PluginDefinitions { get; set; } = [];
}
