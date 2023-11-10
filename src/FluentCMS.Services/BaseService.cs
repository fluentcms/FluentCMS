using FluentCMS.Entities;

namespace FluentCMS.Services;

public abstract class BaseService
{
    protected readonly IApplicationContext AppContext;
    protected readonly ICurrentContext Current;

    public BaseService(IApplicationContext appContext)
    {
        AppContext = appContext;
        Current = appContext.Current;
    }

}

public abstract class BaseService<TEntity> : BaseService where TEntity : IAuditEntity
{
    public BaseService(IApplicationContext appContext) : base(appContext)
    {
    }

    public void PrepareForCreate(TEntity entity)
    {
        entity.CreatedBy = Current.UserName;
        entity.LastUpdatedBy = Current.UserName;
    }

    public void PrepareForUpdate(TEntity entity)
    {
        entity.LastUpdatedBy = Current.UserName;
    }

}