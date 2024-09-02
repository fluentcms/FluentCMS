namespace FluentCMS.Repositories.MongoDB;

public class FolderRepository : AuditableEntityRepository<Folder>, IFolderRepository
{
    public FolderRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : base(mongoDbContext, apiExecutionContext)
    {
    }
}
