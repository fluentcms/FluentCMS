using AutoMapper;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Services;

public interface ISiteService : IAutoRegisterService
{
    Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default);
    Task<Site> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<Site> Create(SiteTemplate siteTemplate, CancellationToken cancellationToken = default);
    Task<Site> Update(Site site, CancellationToken cancellationToken = default);
    Task<Site> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<string> GetSitemap(Site site, Uri uri, CancellationToken cancellationToken = default);
}

public class SiteService(ISiteRepository siteRepository, IPluginDefinitionRepository pluginDefinitionRepository, ISettingsService settingsService, IPageService pageService, IMessagePublisher messagePublisher, IPermissionManager permissionManager, IMapper mapper) : ISiteService
{
    public async Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await siteRepository.GetAll(cancellationToken);
        return await permissionManager.GetAccessible(sites, SitePermissionAction.SiteAdmin, cancellationToken);
    }

    public async Task<Site> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(id, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        return await siteRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);
    }

    public async Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        url = ValidateAndFormatUrl(url);
        // no need to check permissions
        return await siteRepository.GetByUrl(url, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);
    }

    public async Task<Site> Create(SiteTemplate siteTemplate, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var layouts = siteTemplate.Layouts;

        var siteFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, siteTemplate.Template, ServiceConstants.SetupSiteTemplateFile);

        if (!System.IO.File.Exists(siteFilePath))
            throw new AppException($"{ServiceConstants.SetupSiteTemplateFile} doesn't exist!");

        var jsonSerializerOptions = new JsonSerializerOptions();
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        var jsonSiteTemplate = await JsonSerializer.DeserializeAsync<SiteTemplate>(System.IO.File.OpenRead(siteFilePath), jsonSerializerOptions, cancellationToken) ??
               throw new AppException($"Failed to read/deserialize {ServiceConstants.SetupSiteTemplateFile}");

        jsonSiteTemplate.Url = siteTemplate.Url;
        jsonSiteTemplate.Template = siteTemplate.Template;

        if (!string.IsNullOrEmpty(siteTemplate.Name))
            jsonSiteTemplate.Name = siteTemplate.Name;

        if (!string.IsNullOrEmpty(siteTemplate.Description))
            jsonSiteTemplate.Description = siteTemplate.Description;

        mapper.Map(jsonSiteTemplate, siteTemplate);

        // loading layout data from files
        foreach (var layout in siteTemplate.Layouts)
        {
            var bodyLayoutFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, siteTemplate.Template, ServiceConstants.SetupLayoutsFolder, $"{layout.Name}.body.html");
            var headLayoutFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, siteTemplate.Template, ServiceConstants.SetupLayoutsFolder, $"{layout.Name}.head.html");
            layout.Body = await System.IO.File.ReadAllTextAsync(bodyLayoutFilePath, cancellationToken);
            layout.Head = await System.IO.File.ReadAllTextAsync(headLayoutFilePath, cancellationToken);
        }

        foreach (var block in siteTemplate.Blocks)
        {
            var blockContentFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, siteTemplate.Template, ServiceConstants.SetupBlocksFolder, block.Category, $"{block.Name}.html");
            block.Content = await System.IO.File.ReadAllTextAsync(blockContentFilePath, cancellationToken);
        }

        var pluginDefinitions = await pluginDefinitionRepository.GetAll(cancellationToken);
        SetIds(siteTemplate, pluginDefinitions);

        var site = mapper.Map<Site>(siteTemplate);

        site.Urls = [ValidateAndFormatUrl(siteTemplate.Url)];
        site.LayoutId = layouts.Where(x => x.Name == siteTemplate.Layout).Single().Id;
        site.EditLayoutId = layouts.Where(x => x.Name == siteTemplate.EditLayout).Single().Id;
        site.DetailLayoutId = layouts.Where(x => x.Name == siteTemplate.DetailLayout).Single().Id;

        ValidateAndFormatUrls(site);

        // check if site url are unique
        var allSites = await siteRepository.GetAll(cancellationToken);
        if (allSites.Any(x => x.Urls.Any(y => site.Urls.Contains(y))))
            throw new AppException(ExceptionCodes.SiteUrlMustBeUnique);

        // create the site or throw an exception if it fails
        var newSite = await siteRepository.Create(site, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteUnableToCreate);

        await messagePublisher.Publish(new Message<SiteTemplate>(ActionNames.SiteCreated, siteTemplate), cancellationToken);
        await messagePublisher.Publish(new Message<SiteTemplate>(ActionNames.SetupInitializePlugins, siteTemplate), cancellationToken);

        return newSite;
    }

    public async Task<Site> Update(Site site, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(site.Id, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        ValidateAndFormatUrls(site);

        // check if site url is unique
        var allSites = await siteRepository.GetAll(cancellationToken);
        if (allSites.Any(x => x.Id != site.Id && x.Urls.Any(y => site.Urls.Contains(y))))
            throw new AppException(ExceptionCodes.SiteUrlMustBeUnique);

        var updateSite = await siteRepository.Update(site, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteUnableToUpdate);

        await messagePublisher.Publish(new Message<Site>(ActionNames.SiteUpdated, updateSite), cancellationToken);

        return updateSite;
    }

    public async Task<Site> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var deletedSite = await siteRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteUnableToDelete);

        await messagePublisher.Publish(new Message<Site>(ActionNames.SiteDeleted, deletedSite), cancellationToken);

        return deletedSite;
    }

    public async Task<string> GetSitemap(Site site, Uri uri, CancellationToken cancellationToken = default)
    {
        var pages = await pageService.GetBySiteId(site.Id, cancellationToken);
        var settings = await settingsService.GetByIds(pages.Select(x => x.Id), cancellationToken);

        var result = "";

        result += "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
        result += "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n";

        foreach (var page in pages)
        {
            var pageSettings = settings.FirstOrDefault(x => x.Id == page.Id);
            if (pageSettings is null)
                continue;

            pageSettings.Values.TryGetValue("Index", out var index);

            if (index == "true")
            {
                result += "    <url>\n";
                result += $"        <loc>{uri.Scheme}://{uri.Host}{page.FullPath}</loc>\n";
                if (page.ModifiedAt.HasValue)
                    result += $"        <lastmod>{page.ModifiedAt.Value.ToString("yyyy-MM-dd")}</lastmod>\n";
                else
                    result += $"        <lastmod>{page.CreatedAt.ToString("yyyy-MM-dd")}</lastmod>\n";
                result += "    </url>\n";
            }
        }

        result += "</urlset>";
        return result;
    }

    private static void ValidateAndFormatUrls(Site site)
    {
        site.Urls = site.Urls.Select(ValidateAndFormatUrl).ToList();
    }

    public const string VALID_DOMAIN_NAME_REGEX = @"^(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*([A-Za-z0-9]|[A-Za-z0-9][A-Za-z0-9\-]*[A-Za-z0-9])(:\d{1,5})?$";

    private static string ValidateAndFormatUrl(string url)
    {
        // Trim spaces
        url = url.Trim();

        // Check if URL is empty
        if (string.IsNullOrEmpty(url))
            throw new AppException(ExceptionCodes.SiteUrlIsEmpty);

        // Convert to lowercase
        url = url.ToLowerInvariant();

        // if the url ends with a slash, remove it
        if (url.EndsWith('/'))
            url = url[..^1];

        // Validate URL format using regex
        if (!Regex.IsMatch(url, VALID_DOMAIN_NAME_REGEX))
            throw new AppException(ExceptionCodes.SiteUrlIsInvalid);

        // Return the formatted URL if valid
        return url;
    }

    private static void SetIds(SiteTemplate siteTemplate, IEnumerable<PluginDefinition> pluginDefinitions)
    {
        // set site, page, layout, etc. ids
        siteTemplate.Id = Guid.NewGuid();

        foreach (var layout in siteTemplate.Layouts)
        {
            layout.Id = Guid.NewGuid();
            layout.SiteId = siteTemplate.Id;
        }

        foreach (var role in siteTemplate.Roles)
        {
            role.Id = Guid.NewGuid();
            role.SiteId = siteTemplate.Id;
        }

        foreach (var page in siteTemplate.Pages)
        {
            page.Template = siteTemplate.Template;
            SetIds(siteTemplate, page, pluginDefinitions);
        }
    }

    private static void SetIds(SiteTemplate siteTemplate, PageTemplate page, IEnumerable<PluginDefinition> pluginDefinitions)
    {
        page.Id = Guid.NewGuid();
        page.SiteId = siteTemplate.Id;
        foreach (var plugin in page.Plugins)
        {
            plugin.Id = Guid.NewGuid();
            plugin.DefinitionId = pluginDefinitions.Where(p => p.Name.Equals(plugin.Definition, StringComparison.InvariantCultureIgnoreCase)).Single().Id;
        }
        foreach (var childPage in page.Children)
        {
            childPage.Template = siteTemplate.Template;
            SetIds(siteTemplate, childPage, pluginDefinitions);
        }
    }

}
