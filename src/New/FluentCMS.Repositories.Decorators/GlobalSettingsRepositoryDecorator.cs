namespace FluentCMS.Repositories.Decorators;

public class GlobalSettingsRepositoryDecorator : IGlobalSettingsRepository
{
    private readonly IGlobalSettingsRepository _decorator;
    private readonly IAuthContext _authContext;

    public GlobalSettingsRepositoryDecorator(IGlobalSettingsRepository decorator, IAuthContext authContext)
    {
        _decorator = decorator;
        _authContext = authContext;
    }

    public Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        return _decorator.Get(cancellationToken);
    }

    public Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        SetAuditableFields(settings);
        return _decorator.Update(settings, cancellationToken);
    }

    public Task<bool> Reset(CancellationToken cancellationToken = default)
    {
        return _decorator.Reset(cancellationToken);
    }

    private void SetAuditableFields(GlobalSettings settings)
    {
        if (settings.Id == Guid.Empty)
        {
            settings.CreatedAt = DateTime.UtcNow;
            settings.CreatedBy = _authContext.Username;
        }
        else
        {
            settings.ModifiedAt = DateTime.UtcNow;
            settings.ModifiedBy = _authContext.Username;
        }
    }
}
