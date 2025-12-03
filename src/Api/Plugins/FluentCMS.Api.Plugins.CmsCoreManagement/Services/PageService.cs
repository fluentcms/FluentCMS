using FluentCMS.Api.Core.Models;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Services;

public interface IPageService
{
    Task<IEnumerable<Page>> GetAll(CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    // Task<Page> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<Page> Add(Page page, CancellationToken cancellationToken = default);
    Task<Page> Update(Page page, CancellationToken cancellationToken = default);
    Task<Page> Remove(Guid id, CancellationToken cancellationToken = default);
}

internal class PageService(IPageRepository pageRepository, IEventPublisher eventPublisher) : IPageService
{
    public const string VALID_DOMAIN_NAME_REGEX = @"^(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*([A-Za-z0-9]|[A-Za-z0-9][A-Za-z0-9\-]*[A-Za-z0-9])(:\d{1,5})?$";

    public async Task<IEnumerable<Page>> GetAll(CancellationToken cancellationToken = default)
    {
        return await pageRepository.GetAll(cancellationToken);
    }

    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await pageRepository.GetById(id, cancellationToken);
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await pageRepository.GetAllForSite(siteId, cancellationToken);
    }

    // public async Task<Page> GetByUrl(string url, CancellationToken cancellationToken = default)
    // {
    //     url = ValidateAndFormatUrl(url);
    //     // no need to check permissions
    //     return await pageRepository.GetByUrl(url, cancellationToken) ??
    //         throw new EntityNotFoundException<Page>();
    // }

    public async Task<Page> Add(Page page, CancellationToken cancellationToken = default)
    {
        // ValidateAndFormatUrls(page);

        // check if page url are unique
        // var allPages = await pageRepository.GetAll(cancellationToken);
        // if (allPages.Any(x => x.Urls.Any(y => page.Urls.Contains(y))))
        //     throw new EnhancedException(MessageCodes.PageUrlMustBeUnique);

        await pageRepository.Add(page, cancellationToken);

        await eventPublisher.Publish(new PageAddedEvent(page), cancellationToken);

        return page;
    }

    public async Task<Page> Update(Page page, CancellationToken cancellationToken = default)
    {
        // ValidateAndFormatUrls(page);

        // check if page url is unique
        // var allPages = await pageRepository.GetAll(cancellationToken);
        // if (allPages.Any(x => x.Id != page.Id && x.Urls.Any(y => page.Urls.Contains(y))))
        //     throw new EnhancedException(MessageCodes.PageUrlMustBeUnique);

        await pageRepository.Update(page, cancellationToken);

        await eventPublisher.Publish(new PageUpdatedEvent(page), cancellationToken);

        return page;
    }

    public async Task<Page> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        var deletedPage = await pageRepository.Remove(id, cancellationToken);

        await eventPublisher.Publish(new PageRemovedEvent(deletedPage), cancellationToken);

        return deletedPage;
    }


    // private static void ValidateAndFormatUrls(Page page)
    // {
    //     page.Urls = [.. page.Urls.Select(ValidateAndFormatUrl)];
    // }


    // private static string ValidateAndFormatUrl(string url)
    // {
    //     // Trim spaces
    //     url = url.Trim();

    //     // Check if URL is empty
    //     if (string.IsNullOrEmpty(url))
    //         throw new EnhancedException(MessageCodes.PageUrlCannotBeEmpty);

    //     // Convert to lowercase
    //     url = url.ToLowerInvariant();

    //     // if the url ends with a slash, remove it
    //     if (url.EndsWith('/'))
    //         url = url[..^1];

    //     // Validate URL format using regex
    //     if (!Regex.IsMatch(url, VALID_DOMAIN_NAME_REGEX))
    //         throw new EnhancedException(MessageCodes.PageUrlIsInvalid);

    //     // Return the formatted URL if valid
    //     return url;
    // }

}
