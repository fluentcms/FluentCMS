namespace FluentCMS.Repositories.Abstractions;

public interface IPageSectionRepository : ISiteAssociatedRepository<PageSection>
{
    Task<IEnumerable<PageSection>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default);
}
