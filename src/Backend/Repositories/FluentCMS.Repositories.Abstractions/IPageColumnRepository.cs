namespace FluentCMS.Repositories.Abstractions;

public interface IPageColumnRepository : ISiteAssociatedRepository<PageColumn>
{
    Task<IEnumerable<PageColumn>> GetBySectionId(Guid sectionId, CancellationToken cancellationToken = default);
}
