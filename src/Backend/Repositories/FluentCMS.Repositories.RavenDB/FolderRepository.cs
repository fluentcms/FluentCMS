namespace FluentCMS.Repositories.RavenDB;

public class FolderRepository : AuditableEntityRepository<Folder>, IFolderRepository
{
    public FolderRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : base(RavenDbContext, apiExecutionContext)
    {
    }
}
