using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

public class AppTemplateRepository : AuditableEntityRepository<AppTemplate>, IAppTemplateRepository
{
    public AppTemplateRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }
}
