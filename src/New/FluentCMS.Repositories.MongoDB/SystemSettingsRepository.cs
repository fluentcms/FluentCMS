using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class SystemSettingsRepository : ISystemSettingsRepository
{
    private readonly IMongoCollection<SystemSettings> _collection;
    private readonly IApplicationContext _applicationContext;

    public SystemSettingsRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
    {
        _collection = mongoDbContext.Database.GetCollection<SystemSettings>("system_settings");
        _applicationContext = applicationContext;
    }

    public async Task<SystemSettings?> Create(SystemSettings systemSettings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditableFieldsForCreate(systemSettings);
        await _collection.InsertOneAsync(systemSettings, null, cancellationToken);
        return systemSettings;
    }

    public async Task<SystemSettings?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _collection.Find(Builders<SystemSettings>.Filter.Empty).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SystemSettings?> Update(SystemSettings systemSettings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditableFieldsForUpdate(systemSettings);
        var idFilter = Builders<SystemSettings>.Filter.Eq(x => x.Id, systemSettings.Id);
        return await _collection.FindOneAndReplaceAsync(idFilter, systemSettings, null, cancellationToken);
    }

    private void SetAuditableFieldsForCreate(SystemSettings systemSettings)
    {
        systemSettings.Id = Guid.NewGuid();
        systemSettings.CreatedAt = DateTime.UtcNow;
        systemSettings.CreatedBy = _applicationContext.Username;
    }

    private void SetAuditableFieldsForUpdate(SystemSettings systemSettings)
    {
        systemSettings.ModifiedAt = DateTime.UtcNow;
        systemSettings.ModifiedBy = _applicationContext.Username;
    }
}
