using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface ISiteService
{
    Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default);
    Task<Site> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<Site> Create(Site site, CancellationToken cancellationToken = default);
    Task<Site> Update(Site site, CancellationToken cancellationToken = default);
    // TODO: argument should be Guid id instead of Site site
    // TODO: it should returns Task<Site> instead of Task
    Task Delete(Site site, CancellationToken cancellationToken = default);
}

public class SiteService : ISiteService
{
    private readonly ISiteRepository _siteRepository;
    private readonly IApplicationContext _applicationContext;

    public SiteService(ISiteRepository repository, IApplicationContext applicationContext)
    {
        _siteRepository = repository;
        _applicationContext = applicationContext;
    }

    public async Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await _siteRepository.GetAll(cancellationToken);

        // Checking if the user is authenticated
        var currentUser = _applicationContext.Current?.User
            ?? throw new AppException(Errors.UserIsNotAuthenticated);

        // Checking if the user is super user
        sites = sites.Where(x => _applicationContext.Current.Host.SuperUsers.Contains(currentUser.Username));

        return sites;
    }

    public async Task<Site> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await _siteRepository.GetById(id, cancellationToken)
            ?? throw new AppException(Errors.Sites.SiteDoesNotExists);

        // Checking if the user has access to the site
        var currentUser = _applicationContext.Current?.User;

        // not authenticated
        if (currentUser == null)
        {
            if (site.Permissions.Where(x => x.Name == "VIEW").Any(x => x.Roles.Contains("*")))
                return site;
        }
        // authenticated
        else
        {
            var hasAccess = site.Permissions.Where(x => x.Name == "ADMIN" || x.Name == "VIEW").Any(x => x.Roles.Any(y => _applicationContext.Current.Roles.Select(x => x.Name).Contains(y)));
            if (!hasAccess) throw new AppException(Errors.Sites.AccessDeniedToTheSite);
        }

        throw new AppException(Errors.Sites.AccessDeniedToTheSite);
    }

    public async Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        var site = await _siteRepository.GetByUrl(url, cancellationToken)
            ?? throw new AppException(Errors.Sites.SiteDoesNotExists);
        return site;
    }

    public async Task<Site> Create(Site site, CancellationToken cancellationToken = default)
    {
        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();

        // check if the site URLs are unique in all sites
        if (await _siteRepository.CheckUrls(site.Urls, cancellationToken))
            throw new AppException(Errors.Sites.DuplicateSiteUrlFound);

        if (!_applicationContext.Current.Host.SuperUsers.Contains(_applicationContext.Current.User.Username))
            throw new AppException(Errors.Sites.OnlySuperUsersCanCreateOrUpdateOrDeleteSites);

        site.CreatedBy = _applicationContext.Current?.User?.Username ?? string.Empty;
        site.LastUpdatedBy = _applicationContext.Current?.User?.Username ?? string.Empty;

        var newSite = await _siteRepository.Create(site, cancellationToken);
        return newSite ?? throw new Exception("Site not created");
    }

    public async Task<Site> Update(Site site, CancellationToken cancellationToken = default)
    {
        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();

        // check if the site URLs are unique in all sites
        if (await _siteRepository.CheckUrls(site.Urls, cancellationToken))
            throw new AppException(Errors.Sites.DuplicateSiteUrlFound);

        // Checking if the user has access to update the site
        if (!_applicationContext.Current.Host.SuperUsers.Contains(_applicationContext.Current.User.Username))
            throw new AppException(Errors.Sites.OnlySuperUsersCanCreateOrUpdateOrDeleteSites);

        var hasAccess = site.Permissions.Where(x => x.Name == "ADMIN").Any(x => x.Roles.Any(y => _applicationContext.Current.Roles.Select(x => x.Name).Contains(y)));
        if (!hasAccess) throw new AppException(Errors.Sites.AccessDeniedToTheSite);

        site.LastUpdatedBy = _applicationContext.Current?.User?.Username ?? string.Empty;

        var updateSite = await _siteRepository.Update(site, cancellationToken);

        return updateSite ?? throw new Exception("Site not updated");
    }

    public async Task Delete(Site site, CancellationToken cancellationToken = default)
    {
        // Checking if the user is super user
        if (!_applicationContext.Current.Host.SuperUsers.Contains(_applicationContext.Current.User.Username))
            throw new AppException(Errors.Sites.OnlySuperUsersCanCreateOrUpdateOrDeleteSites);

        // TODO: all pages should be deleted either by cascade or manually

        var deletedSite = await _siteRepository.Delete(site.Id, cancellationToken);
        if (deletedSite is null) throw new Exception("Site not deleted.");
    }
}
