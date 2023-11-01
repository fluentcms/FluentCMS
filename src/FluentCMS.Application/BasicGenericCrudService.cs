using AutoMapper;
using FluentCMS.Application.Sites;
using FluentCMS.Entities;
using FluentCMS.Repository;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;

namespace FluentCMS.Application;

public abstract class BasicGenericCrudService<T>
    where T : IEntity
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGenericRepository<T> _repository;

    public BasicGenericCrudService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _repository = serviceProvider.GetRequiredService<IGenericRepository<T>>();
    }
    internal virtual async Task<T> GetByIdAsync(Guid id) => await _repository.GetById(id) ?? throw new EntityNotFoundException<T>(id);
    internal virtual async Task CreateAsync(T entity) => await _repository.Create(entity);
    internal virtual async Task UpdateAsync(T entity) => await _repository.Update(entity);
    internal virtual async Task DeleteAsync(Guid id) => await _repository.Delete(id);
    internal virtual async Task<List<T>> GetAllAsync() => (await _repository.GetAll()).ToList();
    internal virtual async Task<List<T>> GetByFilterAsync(Expression<Func<T, bool>> filter) => (await _repository.GetAll(filter)).ToList();

}
