namespace FluentCMS.Repositories.LiteDb;

public class FileRepository : AuditableEntityRepository<File>, IFileRepository
{
    public FileRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }
}
