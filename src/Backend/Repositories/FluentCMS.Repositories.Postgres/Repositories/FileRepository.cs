namespace FluentCMS.Repositories.Postgres.Repositories;

public class FileRepository(PostgresDbContext context) : AuditableEntityRepository<File>(context), IFileRepository, IService;

