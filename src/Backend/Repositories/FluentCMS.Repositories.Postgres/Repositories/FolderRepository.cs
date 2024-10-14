namespace FluentCMS.Repositories.Postgres.Repositories;

public class FolderRepository(PostgresDbContext context) : AuditableEntityRepository<Folder>(context), IFolderRepository, IService;
