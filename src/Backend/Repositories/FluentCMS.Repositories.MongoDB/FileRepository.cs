namespace FluentCMS.Repositories.MongoDB;

public class FileRepository : AuditableEntityRepository<File>, IFileRepository
{
    public FileRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }
}

