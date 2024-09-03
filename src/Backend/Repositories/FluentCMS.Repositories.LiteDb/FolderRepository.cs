namespace FluentCMS.Repositories.LiteDb;

public class FolderRepository : AuditableEntityRepository<Folder>, IFolderRepository
{
    public FolderRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : base(liteDbContext, apiExecutionContext)
    {
    }
}
