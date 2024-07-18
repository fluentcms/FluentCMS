namespace FluentCMS.Repositories.Abstractions;

public interface IPageRowRepository : ISiteAssociatedRepository<PageRow>
{
    Task<IEnumerable<PageRow>> GetBySectionId(Guid sectionId, CancellationToken cancellationToken = default);
}
