namespace FluentCMS.Repositories.MongoDB;

public class PermissionRepository(
    IMongoDBContext mongoDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<Permission>(mongoDbContext, authContext),
    IPermissionRepository
{
    public async Task<IEnumerable<Permission>> GetByRole(Guid roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Permission>.Filter.Eq(x => x.RoleId, roleId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteByRole(Guid roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Permission>.Filter.Eq(x => x.RoleId, roleId);
        _ = await Collection.FindOneAndDeleteAsync(filter, null, cancellationToken);
        return true;
    }

}
