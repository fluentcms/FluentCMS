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
            var entity = await session.Query<RavenEntity<GlobalSettings>>().SingleOrDefaultAsync(cancellationToken);

            return entity?.Data;
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
            var dbEntity = await session.Query<RavenEntity<GlobalSettings>>().SingleOrDefaultAsync(cancellationToken);
            if (dbEntity == null)
            {
                SetAuditableFieldsForCreate(settings);

                dbEntity = new RavenEntity<GlobalSettings>(settings);

                await session.StoreAsync(dbEntity, cancellationToken);
            }
            else
            {
                settings.CopyProperties(dbEntity.Data);

                SetAuditableFieldsForUpdate(dbEntity.Data);
            }

            await session.SaveChangesAsync(cancellationToken);

            return dbEntity?.Data;
        }
    }

    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await Get(cancellationToken);

        return existing?.Initialized ?? false;
    }

    // private async Task<GlobalSettings?> Create(GlobalSettings settings, CancellationToken cancellationToken = default)
    // {
    //     cancellationToken.ThrowIfCancellationRequested();
        
    //     SetAuditableFieldsForCreate(settings);
    //     using (var session = _store.OpenAsyncSession())
    //     {
    //         await session.StoreAsync(settings, settings.Id.ToString(), cancellationToken);

    //         await session.SaveChangesAsync(cancellationToken);
    //     }
        
    //     return settings;
    // }

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
