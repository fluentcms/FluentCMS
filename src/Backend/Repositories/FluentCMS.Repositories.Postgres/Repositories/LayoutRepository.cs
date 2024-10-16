namespace FluentCMS.Repositories.Postgres.Repositories;

public class LayoutRepository(PostgresDbContext context ) : SiteAssociatedRepository<Layout>(context), ILayoutRepository, IService;
