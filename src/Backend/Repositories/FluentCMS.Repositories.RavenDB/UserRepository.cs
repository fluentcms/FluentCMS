using System.IO.Compression;
using System.Security.Claims;

namespace FluentCMS.Repositories.RavenDB;

public class UserRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<User>(RavenDbContext, apiExecutionContext), IUserRepository
{
    // override public async Task<User?> GetById(Guid id, CancellationToken cancellationToken = default)
    // {
    //     cancellationToken.ThrowIfCancellationRequested();

    //     using (var session = Store.OpenAsyncSession())
    //     {
    //         var dbEntity = await session.Query<RavenEntity<User>>().SingleOrDefaultAsync(x => x.Data.Id == id, cancellationToken);

    //         return dbEntity?.Data;
    //     }
    // }

    // override public async Task<User?> Update(User entity, CancellationToken cancellationToken = default)
    // {
    //     cancellationToken.ThrowIfCancellationRequested();

    //     using (var session = Store.OpenAsyncSession())
    //     {
    //         var dbEntity = await session.Query<User>().SingleOrDefaultAsync(x => x.Id == entity.Id, cancellationToken);
    //         if (dbEntity == null)
    //         {
    //             SetAuditableFieldsForCreate(entity);

    //             await session.StoreAsync(entity, entity.Id.ToString(), cancellationToken);

    //             dbEntity = entity;
    //         }
    //         else
    //         {
    //             entity.CopyProperties(dbEntity);
        
    //             SetAuditableFieldsForUpdate(entity, dbEntity);
    //         }

    //         await session.SaveChangesAsync(cancellationToken);
        
    //         return dbEntity;
    //     }
    // }

    public async Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var users = session.Query<RavenEntity<User>>()
                                .Where(u => u.Data.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value))
                                .Select(x => x.Data)
                                .ToListAsync();

            return await users;
        }
    }

    public async Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        using (var session = Store.OpenAsyncSession())
        {
            var dbEntity = await session.Query<RavenEntity<User>>().SingleOrDefaultAsync(x => x.Data.NormalizedEmail == normalizedEmail);

            return dbEntity?.Data;
        }
    }

    public async Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var dbEntity = await session.Query<RavenEntity<User>>()
                                        .FirstOrDefaultAsync(user => user.Data.Logins.Any(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey));

            return dbEntity?.Data;
        }
    }

    public async Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        using (var session = Store.OpenAsyncSession())
        {
            var dbEntity = await session.Query<RavenEntity<User>>().FirstOrDefaultAsync(x => x.Data.NormalizedUserName == normalizedUserName);

            return dbEntity?.Data;
        }
    }

    public IQueryable<User> AsQueryable()
    {
        using (var session = Store.OpenSession())
        {
            // TODO: Not good to load all user to list and the query them. But difficult to use sessions otherwise.
            var entities = session.Query<RavenEntity<User>>().Select(x => x.Data).ToList();
            
            return entities.AsQueryable();
        }
    }

}
