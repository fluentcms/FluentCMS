namespace FluentCMS.Repositories.Postgres.Repositories;

public class BlockRepository(PostgresDbContext context) : SiteAssociatedRepository<Block>(context), IBlockRepository, IService
{
}
