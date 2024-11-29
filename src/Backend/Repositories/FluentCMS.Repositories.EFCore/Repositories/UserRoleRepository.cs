namespace FluentCMS.Repositories.EFCore;

public class UserRoleRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<UserRole, UserRoleModel>(dbContext, mapper, apiExecutionContext), IUserRoleRepository
{
    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        var dbEntities = await DbContext.UserRoles.Where(x => x.SiteId == siteId && x.UserId == userId).ToListAsync(cancellationToken);
        return Mapper.Map<IEnumerable<UserRole>>(dbEntities);
    }

    public async Task<IEnumerable<UserRole>> GetByRoleId(Guid roleId, CancellationToken cancellationToken = default)
    {
        var dbEntities = await DbContext.UserRoles.Where(x => x.RoleId == roleId).ToListAsync(cancellationToken);
        return Mapper.Map<IEnumerable<UserRole>>(dbEntities);
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        var dbEntities = await DbContext.UserRoles.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        return Mapper.Map<IEnumerable<UserRole>>(dbEntities);
    }
}
