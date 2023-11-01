using FluentCMS.Entities;
using FluentCMS.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Application;

public abstract class GenericCrudService<T> : BasicGenericCrudService<T>
    where T : IEntity
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGenericRepository<T> _repository;

    public GenericCrudService(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _repository = serviceProvider.GetRequiredService<IGenericRepository<T>>();
    }
    public virtual Task BeforeCreateAndUpdate(T entity) { return Task.CompletedTask; }
    public virtual Task AfterCreateAndUpdate(T entity) { return Task.CompletedTask; }
    public virtual Task BeforeCreate(T entity) { return BeforeCreateAndUpdate(entity); }
    public virtual Task AfterCreate(T entity) { return AfterCreateAndUpdate(entity); }
    public virtual Task BeforeUpdate(T entity) { return BeforeCreateAndUpdate(entity); }
    public virtual Task AfterUpdate(T entity) { return AfterCreateAndUpdate(entity); }
    public virtual Task BeforeDelete(Guid id) { return Task.CompletedTask; }
    public virtual Task AfterDelete(Guid id) { return Task.CompletedTask; }
    internal override async Task CreateAsync(T entity)
    {
        await BeforeCreate(entity);
        await base.CreateAsync(entity);
        await AfterCreate(entity);
    }
    internal override async Task UpdateAsync(T entity)
    {
        await BeforeUpdate(entity);
        await base.UpdateAsync(entity);
        await AfterUpdate(entity);
    }
    internal override async Task DeleteAsync(Guid id)
    {
        await BeforeDelete(id);
        await base.DeleteAsync(id);
        await AfterDelete(id);
    }

}
