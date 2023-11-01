using FluentCMS.Entities;
using System.Linq.Expressions;

namespace FluentCMS.Application;

public class ConstrainedGenericCrudServiceWithMapping<T, TDto> : GenericCrudServiceWithMapping<T, TDto>
    where T : IEntity
    where TDto : class
{   

    private readonly IServiceProvider _serviceProvider;
    public ConstrainedGenericCrudServiceWithMapping(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    public virtual async Task<bool> CanCreate() => await Task.FromResult(true);
    internal override async Task CreateAsync(T entity)
    {
        if (!await CanCreate()) throw new UnauthorizedAccessException();
        await base.CreateAsync(entity);
    }
    public virtual async Task<bool> CanRead() => await Task.FromResult(true);
    public override async Task<TDto> GetByIdAsync(Guid id)
    {
        if (!await CanCreate()) throw new UnauthorizedAccessException();
        return await base.GetByIdAsync(id);
    }
    public override async Task<List<TDto>> GetByFilterAsync(Expression<Func<T, bool>> filter)
    {
        if (!await CanCreate()) throw new UnauthorizedAccessException();
        return await base.GetByFilterAsync(filter);
    }
    public virtual async Task<bool> CanUpdate() => await Task.FromResult(true);
    internal async override Task UpdateAsync(T entity)
    {
        if (!await CanUpdate()) throw new UnauthorizedAccessException();
        await base.UpdateAsync(entity);
    }
    public virtual async Task<bool> CanDelete() => await Task.FromResult(true);
    public override async Task DeleteAsync(Guid id)
    {
        if (!await CanDelete()) throw new UnauthorizedAccessException();
        await base.DeleteAsync(id);
    }
}

