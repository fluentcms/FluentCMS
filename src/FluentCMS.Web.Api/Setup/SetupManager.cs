using FluentCMS.Web.Api.Setup.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace FluentCMS.Web.Api.Setup;

public class SetupManager
{
    private static bool? _initialized;

    private readonly ISiteService _siteService;
    private readonly IRoleService _roleService;
    private readonly IGlobalSettingsService _globalSettingsService;
    private readonly IPluginDefinitionService _pluginDefinitionService;
    private readonly ILayoutService _layoutService;
    private readonly IUserService _userService;
    private readonly IPageService _pageService;
    private readonly IPluginService _pluginService;
    private readonly IAppTemplateService _appTemplateService;

    public const string ADMIN_TEMPLATE_PHYSICAL_PATH = "Templates/Admin";
    public const string APP_TEMPLATE_PHYSICAL_PATH = "Templates/App";

    private SetupRequest _setupRequest = default!;
    private AdminTemplate _adminTemplate = default!;
    private GlobalSettings _globalSettings = default!;
    private Site _site = default!;
    private readonly List<PluginDefinition> _pluginDefinitions = [];
    private readonly List<Layout> _layouts = [];
    private readonly List<Page> _pages = [];
    private User _superAdmin = default!;
    private Guid _defaultLayoutId;

    public SetupManager(
        IConfiguration configuration,
        ISiteService siteService,
        IRoleService roleService,
        IGlobalSettingsService globalSettingsService,
        IPluginDefinitionService pluginDefinitionService,
        ILayoutService layoutService,
        IUserService userService,
        IPageService pageService,
        IPluginService pluginService,
        IAppTemplateService appTemplateService,
        IHostEnvironment env)
    {
        if (env == null)
            throw new AppException(ExceptionCodes.SetupSettingsHostingEnvironmentIsNull);

        _siteService = siteService;
        _roleService = roleService;
        _globalSettingsService = globalSettingsService;
        _pluginDefinitionService = pluginDefinitionService;
        _layoutService = layoutService;
        _userService = userService;
        _pageService = pageService;
        _pluginService = pluginService;
        _appTemplateService = appTemplateService;

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

        _initialized = true;

        return true;
    }

    public Task<PageFullDetailResponse> GetSetupPage()
    {
        if (_initialized.HasValue && _initialized.Value)
            throw new AppException(ExceptionCodes.SetupSettingsAlreadyInitialized);

        var page = new PageFullDetailResponse
        {
            Title = "Setup",
            Layout = new LayoutDetailResponse
            {
                Body = File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, "AuthLayout.body.html")),
                Head = File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, "AuthLayout.head.html"))
            },
            Site = new(),
            Sections = new Dictionary<string, List<PluginDetailResponse>>
            {
                ["Main"] =
                [
                    new() {
                        Definition = new PluginDefinitionDetailResponse
                        {
                            Name = "Setup",
                            Description = "Setup View Plugin",
                            Types =
                            [
                                new PluginDefinitionType
                                {
                                    IsDefault = true,
                                    Name="Setup",
                                    Type = "SetupViewPlugin"
                                }
                            ]
                        }
                    }
                ]
            }
        };
        return Task.FromResult(page);
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

    private async Task InitializeAppTemplates()
    {
        foreach (var folder in Directory.GetDirectories(APP_TEMPLATE_PHYSICAL_PATH))
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
        var appTemplateFile = Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, "manifest.json");

        if (!File.Exists(appTemplateFile))
            return;

        var adminTemplate = await JsonSerializer.DeserializeAsync<AdminTemplate>(File.OpenRead(appTemplateFile));

        if (adminTemplate == null)
            return;

        _adminTemplate = adminTemplate;

        if (_adminTemplate == null)
            return;

        _globalSettings = _adminTemplate.GlobalSettings;

        await InitializeGlobalSettings();
        await InitLayouts();
        await InitPluginDefinitions();
        await InitSite();
        await InitRoles();
        await InitPages();

    }
    private async Task InitializeGlobalSettings()
    {
        // Creating default global settings
        _globalSettings.SuperUsers = [_setupRequest.Username];

        _globalSettings = await _globalSettingsService.Init(_globalSettings);
    }

    private async Task InitRoles()
    {
        foreach (var role in _adminTemplate.Roles)
        {
            await _roleService.Create(role);
        }
    }

    private async Task InitSite()
    {
        _adminTemplate.Site.Urls = [_setupRequest.AdminDomain];
        _adminTemplate.Site.LayoutId = _defaultLayoutId;
        _site = await _siteService.Create(_adminTemplate.Site);
    }

    private async Task InitPluginDefinitions()
    {
        foreach (var pluginDefTemplate in _adminTemplate.PluginDefinitions)
        {
            var pluginDef = new PluginDefinition
            {
                Name = pluginDefTemplate.Name,
                Description = pluginDefTemplate.Description,
                Types = pluginDefTemplate.Types
            };
            _pluginDefinitions.Add(await _pluginDefinitionService.Create(pluginDef));
        }
    }

    private async Task InitLayouts()
    {
        foreach (var layoutTemplate in _adminTemplate.Layouts)
        {
            var layout = new Layout
            {
                Name = layoutTemplate.Name,
                Body = File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, $"{layoutTemplate.Name}.body.html")),
                Head = File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, $"{layoutTemplate.Name}.head.html"))
            };
            _layouts.Add(await _layoutService.Create(layout));
            if (layoutTemplate.IsDefault)
                _defaultLayoutId = layout.Id;
        }
    }

    private async Task InitPages()
    {
        var order = 0;
        foreach (var pageTemplate in _adminTemplate.Pages)
        {
            var _page = await _pageService.Create(GetPage(pageTemplate, null, order));
            order++;
            await InitPagePlugins(pageTemplate, _page.Id);
            _pages.Add(_page);
            var childOrder = 0;
            foreach (var child in pageTemplate.Children)
            {
                var childPage = await _pageService.Create(GetPage(child, _page.Id, childOrder));
                _pages.Add(childPage);
                await InitPagePlugins(child, childPage.Id);
                childOrder++;
            }
        }
    }

    private async Task InitPagePlugins(PageTemplate pageTemplate, Guid pageId)
    {
        var order = 0;
        foreach (var pluginTemplate in pageTemplate.Plugins)
        {
            var pluginDefinition = _pluginDefinitions.Where(p => p.Name.Equals(pluginTemplate.Definition, StringComparison.InvariantCultureIgnoreCase)).Single();
            var plugin = new Plugin
            {
                Order = order,
                Section = pluginTemplate.Section,
                DefinitionId = pluginDefinition.Id,
                PageId = pageId,
                SiteId = _site.Id
            };
            order++;
            await _pluginService.Create(plugin);
        }
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
