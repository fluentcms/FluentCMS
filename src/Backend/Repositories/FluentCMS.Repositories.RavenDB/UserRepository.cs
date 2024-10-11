using System.Security.Claims;

namespace FluentCMS.Repositories.RavenDB;

public class UserRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<User>(RavenDbContext, apiExecutionContext), IUserRepository
{
    override public async Task<User?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            return await session.Query<User>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        }
    }

    override public async Task<User?> Update(User entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var dbEntity = await session.Query<User>().SingleOrDefaultAsync(x => x.Id == entity.Id, cancellationToken);
            if (dbEntity == null)
            {
                SetAuditableFieldsForCreate(entity);

                await session.StoreAsync(entity);

                dbEntity = entity;
            }
            else
            {
                entity.CopyProperties(dbEntity);
        
                SetAuditableFieldsForUpdate(entity, dbEntity);
            }

            await session.SaveChangesAsync(cancellationToken);
        
            return dbEntity;
        }
    }

    public async Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var users = session.Query<User>().Where(u => u.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value)).ToListAsync();

            return await users;
        }
    }

    public async Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        using (var session = Store.OpenAsyncSession())
        {
            return await session.Query<User>().SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail);
        }
    }

    public async Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            return await session.Query<User>().FirstOrDefaultAsync(user => user.Logins.Any(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey));
        }
    }

    public async Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        using (var session = Store.OpenAsyncSession())
        {
            return await session.Query<User>().FirstOrDefaultAsync(x => x.NormalizedUserName == normalizedUserName);
        }
    }

    public IQueryable<User> AsQueryable()
    {
        using (var session = Store.OpenSession())
        {
            // TODO: Not good to load all user to list and the query them. But difficult to use sessions otherwise.
            var entities = session.Query<User>().ToList();
            return entities.AsQueryable();
        }
    }

}
