namespace FluentCMS.Repositories.Abstractions;

public interface IAppRepository : IAppAssociatedRepository<App>
{
    Task<App?> GetBySlug(string slug, CancellationToken cancellationToken = default);
}
