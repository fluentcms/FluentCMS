namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class RoleRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Role>(dbContext), IRoleRepository;
