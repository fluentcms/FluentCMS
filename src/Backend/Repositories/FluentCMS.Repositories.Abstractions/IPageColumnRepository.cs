namespace FluentCMS.Repositories.Abstractions;

public interface IPageColumnRepository : ISiteAssociatedRepository<PageColumn>
{
    Task<IEnumerable<PageColumn>> GetByRowId(Guid rowId, CancellationToken cancellationToken = default);
}
