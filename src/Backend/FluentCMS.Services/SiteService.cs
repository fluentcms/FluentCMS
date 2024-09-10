﻿using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services;

public interface ISiteService : IAutoRegisterService
{
    Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default);
    Task<Site> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<Site> Create(SiteTemplate siteTemplate, CancellationToken cancellationToken = default);
    Task<Site> Update(Site site, CancellationToken cancellationToken = default);
    Task<Site> Delete(Guid id, CancellationToken cancellationToken = default);
}

public class SiteService(ISiteRepository siteRepository, IMessagePublisher messagePublisher, IPermissionManager permissionManager) : ISiteService
{
    public async Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await siteRepository.GetAll(cancellationToken);
        return sites;
    }

    public async Task<Site> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        //if (!await permissionManager.HasAccess(site, PermissionActionNames.SiteAdmin, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        return site;
    }

    public async Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.GetByUrl(url, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        return site;
    }

    public async Task<Site> Create(SiteTemplate siteTemplate, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var layouts = siteTemplate.Layouts;

        var site = new Site
        {
            Id = siteTemplate.Id,
            Name = siteTemplate.Name,
            Description = siteTemplate.Description,
            Urls = [siteTemplate.Url],
            LayoutId = layouts.Where(x => x.Name == siteTemplate.Layout).Single().Id,
            EditLayoutId = layouts.Where(x => x.Name == siteTemplate.EditLayout).Single().Id,
            DetailLayoutId = layouts.Where(x => x.Name == siteTemplate.DetailLayout).Single().Id
        };

        PrepareSite(site);

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
        //if (!await permissionManager.HasAccess(site, PermissionActionNames.SiteAdmin, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        PrepareSite(site);

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
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var originalSite = await siteRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        var deletedSite = await siteRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteUnableToDelete);

        await messagePublisher.Publish(new Message<Site>(ActionNames.SiteDeleted, deletedSite), cancellationToken);

        return deletedSite;
    }

    private static void PrepareSite(Site site)
    {
        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();
    }
}
