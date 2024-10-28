namespace FluentCMS.Repositories.LiteDb;

public class FileRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<File>(liteDbContext, apiExecutionContext), IFileRepository
{
}
