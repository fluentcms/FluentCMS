namespace FluentCMS.Repositories.Postgres.Repositories;

public class PageRepository(PostgresDbContext context) : SiteAssociatedRepository<Page>(context), IPageRepository, IService;
