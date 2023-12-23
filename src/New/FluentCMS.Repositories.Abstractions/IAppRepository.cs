using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IAppRepository : IAppAssociatedRepository<App>
{
    Task<App?> GetBySlug(string slug, CancellationToken cancellationToken = default);
}
