using FluentCMS.Services.Permissions;
using FluentCMS.Web.Api.Filters;
using System.IO;

namespace FluentCMS.Web.Api.Controllers;

public class PageController(ISiteService siteService, IPageService pageService, IPermissionService permissionService, IPluginDefinitionService pluginDefinitionService, IPluginService pluginService, ILayoutService layoutService, IRoleService roleService, IUserRoleService userRoleService, ISetupService setupService, ISettingsService settingsService, IGlobalSettingsService globalSettingsService, IApiExecutionContext apiExecutionContext, IMapper mapper) : BaseGlobalController
{

    public const string AREA = "Page Management";
    public const string READ = $"Read";
    public const string UPDATE = $"Update";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete";

    [HttpGet("{siteId}")]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<PageDetailResponse>> GetAll([FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var pages = await pageService.GetBySiteId(siteId, cancellationToken);
        var pagesResponse = mapper.Map<List<PageDetailResponse>>(pages.ToList());
        return OkPaged(pagesResponse);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<PageDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var page = await pageService.GetById(id, cancellationToken);
        var pageResponse = mapper.Map<PageDetailResponse>(page);
        pageResponse.Settings = (await settingsService.GetById(id, cancellationToken)).Values;
        pageResponse.ViewRoleIds = (await permissionService.Get(page.SiteId, page.Id, PagePermissionAction.PageView, cancellationToken)).Select(x => x.RoleId).ToList();
        pageResponse.AdminRoleIds = (await permissionService.Get(page.SiteId, page.Id, PagePermissionAction.PageAdmin, cancellationToken)).Select(x => x.RoleId).ToList();

        return Ok(pageResponse);
    }

    [HttpGet]
    [DecodeQueryParam]
    [Policy(AREA, READ)]
    public async Task<IApiResult<PageFullDetailResponse>> GetByUrl([FromQuery] string url, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(url);
        var domain = uri.Authority;
        var path = uri.AbsolutePath;

        if (!await setupService.IsInitialized(cancellationToken))
            return Ok(GetSetupPage());

        return await GetPageResponse(domain, path, cancellationToken);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<PageDetailResponse>> Create(PageCreateRequest request, CancellationToken cancellationToken = default)
    {
        var page = mapper.Map<Page>(request);
        var newPage = await pageService.Create(page, cancellationToken);

        // TODO: Refactor this to a PermissionsMessageHandler
        await permissionService.Set(page.SiteId, newPage.Id, PagePermissionAction.PageView, request.ViewRoleIds, cancellationToken);
        await permissionService.Set(page.SiteId, newPage.Id, PagePermissionAction.PageAdmin, request.AdminRoleIds, cancellationToken);

        var pageResponse = mapper.Map<PageDetailResponse>(newPage);
        pageResponse.Settings = (await settingsService.GetById(pageResponse.Id, cancellationToken)).Values;
        return Ok(pageResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<PageDetailResponse>> Update(PageUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var page = mapper.Map<Page>(request);
        var updatedPage = await pageService.Update(page, cancellationToken);

        // TODO: Refactor this to a PermissionsMessageHandler
        await permissionService.Set(page.SiteId, updatedPage.Id, PagePermissionAction.PageView, request.ViewRoleIds, cancellationToken);
        await permissionService.Set(page.SiteId, updatedPage.Id, PagePermissionAction.PageAdmin, request.AdminRoleIds, cancellationToken);

        var entityResponse = mapper.Map<PageDetailResponse>(updatedPage);
        entityResponse.Settings = (await settingsService.GetById(page.Id, cancellationToken)).Values;
        return Ok(entityResponse);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await pageService.Delete(id, cancellationToken);

        return Ok(true);
    }

    #region Private Methods

    private async Task<IApiResult<PageFullDetailResponse>> GetPageResponse(string domain, string path, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetByUrl(domain, cancellationToken);
        var page = await pageService.GetByFullPath(site.Id, path, cancellationToken);
        var pluginDefinitions = (await pluginDefinitionService.GetAll(cancellationToken)).ToDictionary(x => x.Id);
        var layoutsDict = (await layoutService.GetAll(cancellationToken)).ToDictionary(x => x.Id);
        var plugins = await pluginService.GetByPageId(page.Id, cancellationToken);
        var siteSettings = await settingsService.GetById(site.Id, cancellationToken);
        var pageSettings = await settingsService.GetById(page.Id, cancellationToken);
        var roles = await roleService.GetAllForSite(site.Id, cancellationToken) ?? [];

        var layoutId = page.LayoutId ?? site.LayoutId;
        var editLayoutId = page.EditLayoutId ?? site.EditLayoutId;
        var detailLayoutId = page.DetailLayoutId ?? site.DetailLayoutId;

        var pageResponse = mapper.Map<PageFullDetailResponse>(page);
        pageResponse.Site = mapper.Map<SiteDetailResponse>(site);
        pageResponse.Site.AllRoles = mapper.Map<List<RoleDetailResponse>>(roles);
        pageResponse.Site.Settings = siteSettings.Values;
        pageResponse.Layout = mapper.Map<LayoutDetailResponse>(layoutsDict[layoutId]);
        pageResponse.EditLayout = mapper.Map<LayoutDetailResponse>(layoutsDict[editLayoutId]);
        pageResponse.DetailLayout = mapper.Map<LayoutDetailResponse>(layoutsDict[detailLayoutId]);
        pageResponse.Sections = [];
        pageResponse.Settings = pageSettings.Values;

        // set site permissions
        pageResponse.Site.AdminRoleIds = (await permissionService.Get(site.Id, SitePermissionAction.SiteAdmin, cancellationToken)).Select(x=> x.RoleId).ToList();
        pageResponse.Site.ContributorRoleIds = (await permissionService.Get(site.Id, SitePermissionAction.SiteAdmin, cancellationToken)).Select(x => x.RoleId).ToList();

        // set page permissions
        pageResponse.ViewRoleIds = (await permissionService.Get(site.Id, page.Id, PagePermissionAction.PageView, cancellationToken)).Select(x => x.RoleId).ToList();
        pageResponse.AdminRoleIds = (await permissionService.Get(site.Id, page.Id, PagePermissionAction.PageAdmin, cancellationToken)).Select(x => x.RoleId).ToList();

        // setting current user details and permissions
        pageResponse.User = mapper.Map<UserRoleDetailResponse>(apiExecutionContext);
        var userRoleIds = await userRoleService.GetUserRoleIds(apiExecutionContext.UserId, site.Id, cancellationToken);
        pageResponse.User.Roles = pageResponse.Site.AllRoles.Where(x => userRoleIds.Contains(x.Id)).ToList();

        // setting super admin flag
        var globalSettings = await globalSettingsService.Get(cancellationToken);
        if (globalSettings != null)
            pageResponse.User.IsSuperAdmin = globalSettings.SuperAdmins.Contains(pageResponse.User.Username);

        // setting the plugin details
        var pluginSettingsDict = (await settingsService.GetByIds(plugins.Select(x => x.Id), cancellationToken)).ToDictionary(x => x.Id, x => x.Values);
        foreach (var plugin in plugins)
        {
            if (!pageResponse.Sections.ContainsKey(plugin.Section))
                pageResponse.Sections.Add(plugin.Section, []);

            var pluginResponse = mapper.Map<PluginDetailResponse>(plugin);

            // set settings for the plugin if exists in the settings dictionary
            if (pluginSettingsDict.TryGetValue(plugin.Id, out Dictionary<string, string>? value))
                pluginResponse.Settings = value;

            pluginResponse.Definition = mapper.Map<PluginDefinitionDetailResponse>(pluginDefinitions[plugin.DefinitionId]);
            pageResponse.Sections[plugin.Section].Add(pluginResponse);
        }

        return Ok(pageResponse);
    }

    private static PageFullDetailResponse GetSetupPage()
    {
        var page = new PageFullDetailResponse
        {
            Title = "Setup",
            Locked = true,
            Layout = new LayoutDetailResponse
            {
                Body = System.IO.File.ReadAllText(Path.Combine(ServiceConstants.DefaultTemplateFolder, "AuthLayout.body.html")),
                Head = System.IO.File.ReadAllText(Path.Combine(ServiceConstants.DefaultTemplateFolder, "AuthLayout.head.html"))
            },
            Site = new(),
            User = new(),
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
        return page;
    }

    #endregion
}
