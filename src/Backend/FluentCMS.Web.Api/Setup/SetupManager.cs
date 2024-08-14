using FluentCMS.Web.Api.Setup.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace FluentCMS.Web.Api.Setup;

public class SetupManager : ISetupManager
{
    private static bool? _initialized;

    private readonly IApiTokenService _apiTokenService;
    private readonly ISiteService _siteService;
    private readonly IRoleService _roleService;
    private readonly IGlobalSettingsService _globalSettingsService;
    private readonly IPluginDefinitionService _pluginDefinitionService;
    private readonly ILayoutService _layoutService;
    private readonly IUserService _userService;
    private readonly IPageService _pageService;
    private readonly IPluginService _pluginService;
    private readonly IPluginContentService _pluginContentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly IContentService _contentService;
    private readonly IConfigManager _configManager;

    public const string ADMIN_TEMPLATE_PHYSICAL_PATH = "Template";

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
        IApiTokenService apiTokenService,
        ISiteService siteService,
        IRoleService roleService,
        IGlobalSettingsService globalSettingsService,
        IPluginDefinitionService pluginDefinitionService,
        ILayoutService layoutService,
        IUserService userService,
        IPageService pageService,
        IPluginService pluginService,
        IPluginContentService pluginContentService,
        IContentTypeService contentTypeService,
        IContentService contentService,
        IConfigManager configManager,
        IHostEnvironment env)
    {
        if (env == null)
            throw new AppException(ExceptionCodes.SetupSettingsHostingEnvironmentIsNull);

        _apiTokenService = apiTokenService;
        _siteService = siteService;
        _roleService = roleService;
        _globalSettingsService = globalSettingsService;
        _pluginDefinitionService = pluginDefinitionService;
        _layoutService = layoutService;
        _userService = userService;
        _pageService = pageService;
        _pluginService = pluginService;
        _pluginContentService = pluginContentService;
        _contentTypeService = contentTypeService;
        _contentService = contentService;
        _configManager = configManager;
    }

    public async Task<bool> IsInitialized()
    {
        // Check initialization status only once, in the beginning
        if (!_initialized.HasValue)
            _initialized = await InitCondition();

        return _initialized.Value;
    }

    public async Task<bool> Start(SetupRequest request, string host)
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


        // await InitializeDatabase();
        await InitializeApiToken(host);
        await InitializeSuperAdmin();

        var manifestFile = Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, "manifest.json");

        if (!System.IO.File.Exists(manifestFile))
            throw new AppException("manifest.json doesn't exist!");

        var jsonSerializerOptions = new JsonSerializerOptions { };
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        _adminTemplate = await System.Text.Json.JsonSerializer.DeserializeAsync<AdminTemplate>(System.IO.File.OpenRead(manifestFile), jsonSerializerOptions) ??
            throw new AppException("Failed to deserialize manifest.json");

        _globalSettings = _adminTemplate.GlobalSettings;

        await InitializeGlobalSettings();
        await InitLayouts();
        await InitPluginDefinitions();
        await InitSite();
        await InitRoles();
        await InitPages();
        await InitContentTypes();

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
            Locked = true,
            Layout = new LayoutDetailResponse
            {
                Body = System.IO.File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, "AuthLayout.body.html")),
                Head = System.IO.File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, "AuthLayout.head.html"))
            },
            Site = new(),
            Sections = new Dictionary<string, List<PluginDetailResponse>>
            {
                ["Main"] =
                [
                    new()
                    {
                        Locked = true,
                        Section = "Main",
                        Definition = new PluginDefinitionDetailResponse
                        {
                            Name = "Setup",
                            Description = "Setup View Plugin",
                            Assembly = "FluentCMS.Web.Plugins.Admin.dll",
                            Locked = true,
                            Types =
                            [
                                new PluginDefinitionType
                                {
                                    IsDefault = true,
                                    Name = "Setup",
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

    private async Task InitializeDatabase()
    {
        var apiServerConfig = new ApiServerConfig
        {
            Database = new ApiServerConfig.DatabaseConfig
            {
                Provider = "LiteDB",
                ConnectionString = "./LiteDb.db"
            }
        };

        _configManager.UpdateApiServerConfig(apiServerConfig);

        await Task.CompletedTask;
    }

    private async Task InitializeApiToken(string host)
    {
        var appSettingsFilePath = Path.Combine("appsettings.json");
        var text = System.IO.File.ReadAllText(appSettingsFilePath);
        var jsonNode = JsonNode.Parse(text)!;

        // Creating full access api token 
        ApiToken apiToken = await CreateDefaultApiToken();

        // set api token to appsettings.json
        jsonNode!["ApiSettings"]!["Key"] = apiToken.Key + ":" + apiToken.Secret;
        jsonNode!["ApiSettings"]!["Url"] = host;

        var output = JsonSerializer.Serialize(jsonNode);
        System.IO.File.WriteAllText(appSettingsFilePath, output);
    }

    private async Task<ApiToken> CreateDefaultApiToken()
    {
        var apiToken = new ApiToken
        {
            Name = "Full Access",
            Description = "Full Access Token",
            ExpireAt = null,
            Policies = [new Policy { Area = "Global", Actions = ["All"] }],
            Enabled = true
        };

        await _apiTokenService.Create(apiToken);
        return apiToken;
    }

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
        _globalSettings = await _globalSettingsService.Init(_globalSettings);
    }

    private async Task InitRoles()
    {
        var adminRoles = new List<Role>();
        foreach (var role in _adminTemplate.Roles)
        {
            await _roleService.Create(role);
            if (role.ReadOnly)
                adminRoles.Add(role);
        }

        _superAdmin.RoleIds.AddRange(adminRoles.Select(x => x.Id));
        await _userService.Update(_superAdmin);
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
                Types = pluginDefTemplate.Types,
                Assembly = pluginDefTemplate.Assembly,
                Locked = pluginDefTemplate.Locked,
                Category = pluginDefTemplate.Category
            };
            _pluginDefinitions.Add(await _pluginDefinitionService.Create(pluginDef));
        }
    }

    private async Task InitContentTypes()
    {
        foreach (var contentTypeTemplate in _adminTemplate.ContentTypes)
        {
            var contentType = new ContentType
            {
                Slug = contentTypeTemplate.Slug,
                Title = contentTypeTemplate.Title,
                Description = contentTypeTemplate.Description,
                Fields = contentTypeTemplate.Fields
            };

            await _contentTypeService.Create(contentType);
            foreach (var contentDictionary in contentTypeTemplate.Contents)
            {
                var content = new Content
                {
                    TypeId = contentType.Id,
                    Data = contentDictionary
                };
                await _contentService.Create(content);
            }
        }
    }


    private async Task InitLayouts()
    {
        foreach (var layoutTemplate in _adminTemplate.Layouts)
        {
            var layout = new Layout
            {
                Name = layoutTemplate.Name,
                Body = System.IO.File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, $"{layoutTemplate.Name}.body.html")),
                Head = System.IO.File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, $"{layoutTemplate.Name}.head.html"))
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
                SiteId = _site.Id,
                Locked = pluginTemplate.Locked,
                Cols = pluginTemplate.Cols,
                ColsMd = pluginTemplate.ColsMd,
                ColsLg = pluginTemplate.ColsLg,
            };
            order++;
            var pluginResponse = await _pluginService.Create(plugin);
            if (pluginTemplate.Content != null)
            {
                foreach (var pluginContentTemplate in pluginTemplate.Content)
                {
                    var pluginContent = new PluginContent
                    {
                        PluginId = pluginResponse.Id,
                        Type = pluginTemplate.Type,
                        Data = pluginContentTemplate
                    };

                    await _pluginContentService.Create(pluginContent);
                }
            }
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
            Order = order,
            Locked = pageTemplate.Locked
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
