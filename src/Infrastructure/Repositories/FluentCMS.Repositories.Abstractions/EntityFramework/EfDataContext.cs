using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.EntityFramework;

public class EfDataContext(DbContextOptions options) : DbContext(options), IDataContext
{

    // Persist changes using EF Core SaveChangesAsync
    async Task<int> IDataContext.SaveChanges(CancellationToken cancellationToken)
    {
        return await base.SaveChangesAsync(cancellationToken);
    }

    // Get entity set for the given entity type
    IEntitySet<TEntity> IDataContext.Set<TEntity>() where TEntity : class
    {
        var dbSet = base.Set<TEntity>();
        return new EfEntitySet<TEntity>(dbSet);
    }
}
