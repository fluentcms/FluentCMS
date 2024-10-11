namespace FluentCMS.Repositories.RavenDB;

public class GlobalSettingsRepository : IGlobalSettingsRepository
{
    private readonly IApiExecutionContext _apiExecutionContext;
    private readonly IDocumentStore _store;

    public GlobalSettingsRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext)
    {
        _apiExecutionContext = apiExecutionContext;
        _store = RavenDbContext.Store;
    }

    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = _store.OpenAsyncSession())
        {
            return await session.Query<GlobalSettings>().SingleOrDefaultAsync(cancellationToken);
        }
    }

    public async Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using (var session = _store.OpenAsyncSession())
        {
            var dbEntity = await session.Query<GlobalSettings>().SingleOrDefaultAsync(cancellationToken);
            if (dbEntity == null)
            {
                SetAuditableFieldsForCreate(settings);

                await session.StoreAsync(settings);

                dbEntity = settings;
            }
            else
            {
                settings.CopyProperties(dbEntity);

                SetAuditableFieldsForUpdate(dbEntity);
            }

            await session.SaveChangesAsync(cancellationToken);

            return dbEntity;
        }
    }

    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await Get(cancellationToken);

        return existing?.Initialized ?? false;
    }

    private async Task<GlobalSettings?> Create(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        SetAuditableFieldsForCreate(settings);
        using (var session = _store.OpenAsyncSession())
        {
            await session.StoreAsync(settings);

            await session.SaveChangesAsync(cancellationToken);
        }
        
        return settings;
    }

    private void SetAuditableFieldsForCreate(GlobalSettings settings)
    {
        settings.Id = Guid.NewGuid();
        settings.CreatedAt = DateTime.UtcNow;
        settings.CreatedBy = _apiExecutionContext.Username;
    }

    private void SetAuditableFieldsForUpdate(GlobalSettings settings)
    {
        settings.ModifiedAt = DateTime.UtcNow;
        settings.ModifiedBy = _apiExecutionContext.Username;
    }
}
