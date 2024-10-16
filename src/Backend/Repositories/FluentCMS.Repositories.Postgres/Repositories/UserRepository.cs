using System.Security.Claims;

namespace FluentCMS.Repositories.Postgres.Repositories;

public class UserRepository :  IUserRepository, IService
{
    readonly PostgresDbContext _context;
    readonly DbSet<User> _table;

    public UserRepository(PostgresDbContext context)
    {
        _context = context;
        _table = context.Users;
    }
    public IQueryable<User> AsQueryable()
    {
        return _table.AsQueryable();
    }

    public Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var users = AsQueryable().Where(u => u.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value)).ToList();
        return Task.FromResult((IList<User>)users);
    }

    public Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = AsQueryable().SingleOrDefault(x => x.NormalizedEmail == normalizedEmail);
        return Task.FromResult(user);
    }

    public Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = AsQueryable().FirstOrDefault(user => user.Logins.Any(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey));
        return Task.FromResult(user);
    }

    public Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = AsQueryable().FirstOrDefault(x => x.NormalizedUserName == normalizedUserName);
        return Task.FromResult(user);
    }

    public async Task<User?> Create(User entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _table.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return result.Entity;
    }

    public async Task<IEnumerable<User>> CreateMany(IEnumerable<User> entities, CancellationToken cancellationToken = default)
    {
        var enumerable = entities.ToList();
        await _table.AddRangeAsync(enumerable,  cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return enumerable;
    }

    public async Task<User?> Update(User entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _table.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return result.Entity;
    }

    public async Task<User?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await GetById( id, cancellationToken);
        if (entity == null)
        {
            return null;
        }

        _table.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);


        return null;
    }

    public async Task<IEnumerable<User>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entities = await GetByIds(ids, cancellationToken);
        var entityIds = entities.Select(x => x.Id).ToList();
        await _table.Where(x => entityIds.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        return entities;
    }

    public async Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default)
    {
        return await AsQueryable().ToListAsync(cancellationToken);
    }

    public async Task<User?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await AsQueryable().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<User>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idsFilter = ids.ToList();
        return await _table.Where(x => idsFilter.Contains(x.Id)).ToListAsync(cancellationToken);
    }
}
