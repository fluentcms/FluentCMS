//using FluentCMS.Entities;

//namespace FluentCMS.Repositories.LiteDb;

//internal class RoleRepository : GenericRepository<Role>, IRoleRepository
//{
//    public RoleRepository(LiteDbContext dbContext, IApplicationContext applicationContext) : base(dbContext, applicationContext)
//    {
//    }

//    public async Task<IEnumerable<Role>> GetAll(Guid siteId, CancellationToken cancellationToken = default)
//    {
//        cancellationToken.ThrowIfCancellationRequested();

//        var roles = await Collection.FindAsync(x => x.SiteId == siteId);

//        return roles;
//    }
//}
