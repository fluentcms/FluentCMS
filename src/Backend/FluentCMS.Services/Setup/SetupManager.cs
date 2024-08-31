using FluentCMS.Services.Setup.Models;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Services.Setup;

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

    public const string ADMIN_TEMPLATE_PHYSICAL_PATH = "Template";

    private SetupModel _setupRequest = default!;
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

        _setupRequest = request;

        await InitializeApiToken();
        await InitializeSuperAdmin();

        var manifestFile = Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, "manifest.json");

        if (!System.IO.File.Exists(manifestFile))
            throw new AppException("manifest.json doesn't exist!");

        var jsonSerializerOptions = new JsonSerializerOptions { };
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        _adminTemplate = await JsonSerializer.DeserializeAsync<AdminTemplate>(System.IO.File.OpenRead(manifestFile), jsonSerializerOptions) ??
            throw new AppException("Failed to deserialize manifest.json");

        _globalSettings = _adminTemplate.GlobalSettings;

        await InitializeGlobalSettings();
        await InitLayouts();
        await InitPluginDefinitions();
        await InitSite();
        await InitPages();
        await InitContentTypes();

        _initialized = true;

        return true;
    }

    public async Task Reset()
    {
        await _globalSettingsService.Reset();
        _initialized = false;
    }

    #region Private

    private async Task InitializeApiToken()
    {
        var appSettingsFilePath = Path.Combine("appsettings.json");
        var text = System.IO.File.ReadAllText(appSettingsFilePath);
        var jsonNode = JsonNode.Parse(text)!;

        // Creating full access api token 
        ApiToken apiToken = await CreateDefaultApiToken();

        // set api token to appsettings.json
        jsonNode!["ApiSettings"]!["Key"] = apiToken.Key + ":" + apiToken.Secret;

        var output = JsonSerializer.Serialize(jsonNode, new JsonSerializerOptions() { WriteIndented = true });
        System.IO.File.WriteAllText(appSettingsFilePath, output);
    }

    private async Task<ApiToken> CreateDefaultApiToken()
    {
        var apiToken = new ApiToken
        {
            Name = "Full Access",
            Description = "Full Access Token",
            ExpireAt = null,
            Policies = [new Policy { Area = "Global", Actions = ["All Actions"] }],
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
        _globalSettings.SuperAdmins = [_superAdmin!.UserName!];
        _globalSettings = await _globalSettingsService.Init(_globalSettings);
    }

    private async Task InitSite()
    {
        Guid? layoutId = _layouts.Where(l => l.Name.Equals(_adminTemplate.Site.Layout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();
        Guid? editLayoutId = _layouts.Where(l => l.Name.Equals(_adminTemplate.Site.EditLayout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();
        Guid? detailLayoutId = _layouts.Where(l => l.Name.Equals(_adminTemplate.Site.DetailLayout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();

        if (layoutId == Guid.Empty || layoutId == null)
            layoutId = _defaultLayoutId;

        if (editLayoutId == Guid.Empty || editLayoutId == null)
            editLayoutId = _defaultLayoutId;

        if (detailLayoutId == Guid.Empty || detailLayoutId == null)
            detailLayoutId = _defaultLayoutId;

        var site = new Site
        {
            Name = _adminTemplate.Site.Name,
            Description = _adminTemplate.Site.Description,
            Urls = [_setupRequest.AdminDomain],
            LayoutId = layoutId.Value,
            EditLayoutId = editLayoutId.Value,
            DetailLayoutId = detailLayoutId.Value,
        };
        _site = await _siteService.Create(site);
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
                Settings = pluginTemplate.Settings
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
        Guid? editLayoutId = _layouts.Where(l => l.Name.Equals(pageTemplate?.EditLayout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();
        Guid? detailLayoutId = _layouts.Where(l => l.Name.Equals(pageTemplate?.DetailLayout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();

        if (layoutId == Guid.Empty)
            layoutId = null;

        if (editLayoutId == Guid.Empty)
            editLayoutId = null;

        if (detailLayoutId == Guid.Empty)
            detailLayoutId = null;

        var page = new Page
        {
            Title = pageTemplate.Title,
            Path = pageTemplate.Path,
            LayoutId = layoutId,
            EditLayoutId = editLayoutId,
            DetailLayoutId = detailLayoutId,
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
