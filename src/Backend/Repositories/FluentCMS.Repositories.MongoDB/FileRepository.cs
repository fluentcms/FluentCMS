namespace FluentCMS.Repositories.MongoDB;

public class FileRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<File>(mongoDbContext, apiExecutionContext), IFileRepository
{
}

