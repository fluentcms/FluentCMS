namespace FluentCMS.Repositories.EFCore;

public class RoleRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Role, RoleModel>(dbContext, mapper, apiExecutionContext), IRoleRepository;
