namespace FluentCMS.Repositories.Postgres.Repositories;

public class RoleRepository(PostgresDbContext context) : SiteAssociatedRepository<Role>(context), IRoleRepository, IService
{
}
