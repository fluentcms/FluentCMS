namespace FluentCMS.Repositories.MongoDB;

public class FileRepository:AuditableEntityRepository<FileEntity>,IFileRepository
{
    public FileRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }
}
