namespace FluentCMS.Repositories.RavenDB;

public class FileRepository : AuditableEntityRepository<File>, IFileRepository
{
    public FileRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : base(RavenDbContext, apiExecutionContext)
    {
    }
}

