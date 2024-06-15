namespace FluentCMS.Repositories.MongoDB;

public class FolderRepository : AuditableEntityRepository<Folder>, IFolderRepository
{
    public FolderRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }
}
