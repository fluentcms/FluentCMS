﻿namespace FluentCMS.Repositories.MongoDB;

public class UserRoleRepository : SiteAssociatedRepository<UserRole>, IUserRoleRepository
{
    public UserRoleRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userRoles = await Collection.Find(x => x.SiteId == siteId && x.UserId == userId).ToListAsync(cancellationToken);

        return userRoles.Select(x => x.RoleId).ToList();
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.UserId == userId).ToListAsync(cancellationToken);
    }
}
