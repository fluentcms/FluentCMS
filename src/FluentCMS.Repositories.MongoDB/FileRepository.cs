using File = FluentCMS.Entities.File;

namespace FluentCMS.Repositories.MongoDB;
public class FileRepository(IMongoDBContext mongoDbContext, IAuthContext authContext)
    : AuditableEntityRepository<File>(mongoDbContext, authContext), IFileRepository
{
}
