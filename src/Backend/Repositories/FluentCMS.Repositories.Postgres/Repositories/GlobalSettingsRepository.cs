namespace FluentCMS.Repositories.Postgres.Repositories;

public class GlobalSettingsRepository(PostgresDbContext context) : AuditableEntityRepository<GlobalSettings>(context), IGlobalSettingsRepository, IService
{

    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await GetAll(cancellationToken)).SingleOrDefault();
    }


    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await Get(cancellationToken);

        return existing?.Initialized ?? false;
    }




}
