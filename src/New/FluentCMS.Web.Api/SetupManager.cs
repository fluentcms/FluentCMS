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
    private readonly IPageService _pageService;
    private readonly IAppTemplateService _appTemplateService;
    private readonly IHostEnvironment _env;

    private readonly string _appTemplatePhysicalPath;
    private readonly string _siteTemplatePhysicalPath;
    private readonly string _adminTemplatePhysicalPath;

    private SetupRequest _setupRequest = default!;
    private AdminTemplate _adminTemplate = default!;
    private GlobalSettings _globalSettings = default!;
    private Site _site = default!;
    private List<PluginDefinition> _pluginDefinitions = [];
    private List<Layout> _layouts = [];
    private List<Page> _pages = [];
    private User _superAdmin = default!;


    public SetupManager(
        IConfiguration configuration,
        ISiteService siteService,
        IGlobalSettingsService globalSettingsService,
        IPluginDefinitionService pluginDefinitionService,
        ILayoutService layoutService,
        IUserService userService,
        IPageService pageService,
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
        _pageService = pageService;
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

    public async Task<bool> Start(SetupRequest request)
    {
        // Check if this is the first time setup or not
        if (_initialized.HasValue && _initialized.Value)
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);

        if (await InitCondition())
        {
            _initialized = true;
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);
        }

        _setupRequest = request;

        await InitializeSuperAdmin();

        await InitializeAppTemplates();

        await InitializeAdminUI();

        await InitializeGlobalSettings();

        _initialized = true;

        return true;
    }

    public async Task Reset()
    {
        await _globalSettingsService.Reset();
        _initialized = false;
    }

    #region Private

    private async Task InitializeSuperAdmin()
    {
        // Creating super admin user
        var superAdmin = new User
        {
            UserName = _setupRequest.Username,
            Email = _setupRequest.Email,
        };

        _superAdmin = await _userService.Create(superAdmin, _setupRequest.Password);
    }

    private async Task InitializeGlobalSettings()
    {
        // Creating default global settings
        var settings = new GlobalSettings
        {
            SuperUsers = [_setupRequest.Username]
        };

        _globalSettings = await _globalSettingsService.Init(settings);
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

    private async Task InitializeAdminUI()
    {
        var appTemplateFile = Path.Combine(_adminTemplatePhysicalPath, "manifest.json");

        if (!File.Exists(appTemplateFile))
            return;

        var adminTemplate = await JsonSerializer.DeserializeAsync<AdminTemplate>(File.OpenRead(appTemplateFile));

        if (adminTemplate == null)
            return;

        _adminTemplate = adminTemplate;

        if (_adminTemplate == null)
            return;

        await InitSite();
        await InitPluginDefinitions();
        await InitLayouts();
        await InitPages();
    }

    private async Task InitSite()
    {
        _adminTemplate.Site.Urls = [_setupRequest.AdminDomain];
        _site = await _siteService.Create(_adminTemplate.Site);
    }

    private async Task<List<PluginDefinition>> InitPluginDefinitions()
    {
        var pluginDefList = new List<PluginDefinition>();

        foreach (var pluginDef in _adminTemplate.PluginDefinitions)
        {
            pluginDefList.Add(await _pluginDefinitionService.Create(pluginDef));
        }

        return pluginDefList;
    }

    private async Task InitLayouts()
    {
        _layouts = [];

        foreach (var layout in _adminTemplate.Layouts)
        {
            layout.Body = File.ReadAllText(Path.Combine(_adminTemplatePhysicalPath, $"{layout.Name}.body.html"));
            layout.Head = File.ReadAllText(Path.Combine(_adminTemplatePhysicalPath, $"{layout.Name}.head.html"));
            layout.SiteId = _site.Id;
            _layouts.Add(await _layoutService.Create(layout));
        }
    }

    private async Task<List<Page>> InitPages()
    {
        var pagesList = new List<Page>();
        var order = 0;
        foreach (var pageTemplate in _adminTemplate.Pages)
        {
            var _page = await _pageService.Create(GetPage(pageTemplate, null, order));
            order++;
            pagesList.Add(_page);
            var childOrder = 0;
            foreach (var child in pageTemplate.Children)
            {
                pagesList.Add(await _pageService.Create(GetPage(child, _page.Id, childOrder)));
                childOrder++;
            }
        }

        return pagesList;
    }

    private Page GetPage(PageTemplate pageTemplate, Guid? parentId, int order)
    {
        Guid? layoutId = _layouts.Where(l => l.Name.Equals(pageTemplate?.Layout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();
        if (layoutId == Guid.Empty)
            layoutId = null;

        var page = new Page
        {
            Title = pageTemplate.Title,
            Path = pageTemplate.Path,
            LayoutId = layoutId,
            SiteId = _site.Id,
            ParentId = parentId,
            Order = order
        };

        return page;
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
    public List<PageTemplate> Pages { get; set; } = [];
}

public class PageTemplate
{
    public string Path { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Layout { get; set; } = default!;
    public List<PageTemplate> Children { get; set; } = [];

}
