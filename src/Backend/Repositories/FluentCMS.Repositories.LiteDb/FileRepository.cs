namespace FluentCMS.Repositories.LiteDb;

public class FileRepository : AuditableEntityRepository<File>, IFileRepository
{
    public FileRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : base(liteDbContext, apiExecutionContext)
    {
    }
}
