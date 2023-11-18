using FluentCMS.Entities;

namespace FluentCMS.Services;

public abstract class BaseService
{
    protected readonly IApplicationContext AppContext;
    protected ICurrentContext Current => AppContext.Current;

    public BaseService(IApplicationContext appContext)
    {
        AppContext = appContext;
    }

}

public abstract class BaseService<TEntity> : BaseService where TEntity : IAuditEntity
{
    public BaseService(IApplicationContext appContext) : base(appContext)
    {
    }
}
