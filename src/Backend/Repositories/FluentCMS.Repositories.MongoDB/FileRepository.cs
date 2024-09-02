namespace FluentCMS.Repositories.MongoDB;

public class FileRepository : AuditableEntityRepository<File>, IFileRepository
{
    public FileRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : base(mongoDbContext, apiExecutionContext)
    {
    }
}

