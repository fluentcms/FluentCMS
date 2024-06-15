namespace FluentCMS.Repositories.LiteDb;

public class FolderRepository : AuditableEntityRepository<Folder>, IFolderRepository
{
    public FolderRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }
}
