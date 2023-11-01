using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Repository;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq.Expressions;

namespace FluentCMS.Application;

public abstract class GenericCrudServiceWithMapping<T, TDto> : GenericCrudService<T>
    where T : IEntity
    where TDto : class
{
    private readonly IGenericRepository<T> _repository;
    private readonly IMapper _mapper;

    public GenericCrudServiceWithMapping(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _repository = serviceProvider.GetRequiredService<IGenericRepository<T>>();
        _mapper = serviceProvider.GetRequiredService<IMapper>();
    }

    protected virtual async Task<TDto> MapToDto(T entity) { await Task.CompletedTask; return _mapper.Map<TDto>(entity); }
    protected virtual async Task<T> MapToEntity(TDto dto) { await Task.CompletedTask; return _mapper.Map<T>(dto); }
    protected virtual async Task<T> MapToEntity(Guid id,JsonPatchDocument<TDto> dto) {
        var entity = await _repository.GetById(id) ?? throw new EntityNotFoundException<T>(id);
        var originalDto = await MapToDto(entity);
        dto.ApplyTo(originalDto);
        return await MapToEntity(originalDto);
    }
    public new virtual async Task<TDto> GetByIdAsync(Guid id) => await MapToDto(await base.GetByIdAsync(id));
    public virtual async Task CreateAsync(TDto dto) => await base.CreateAsync(await MapToEntity(dto));
    public virtual async Task UpdateAsync(TDto dto) => await base.UpdateAsync(await MapToEntity(dto));
    public virtual async Task UpdateAsync(Guid id,JsonPatchDocument<TDto> dto) => await base.UpdateAsync(await MapToEntity(id, dto));
    public new virtual async Task<List<TDto>> GetAllAsync() => (await Task.WhenAll((await base.GetAllAsync()).Select(x => MapToDto(x)))).ToList();
    public new virtual async Task<List<TDto>> GetByFilterAsync(Expression<Func<T, bool>> filter) => (await Task.WhenAll((await base.GetByFilterAsync(filter)).Select(x => MapToDto(x)))).ToList();
    public new virtual async Task DeleteAsync(Guid id) => await base.DeleteAsync(id);
}

public abstract class GenericCrudServiceWithMapping<T, TCreateDto, TEditDto, TSingleDto, TListDto> : GenericCrudService<T>
    where T : IEntity
    where TCreateDto : class
    where TEditDto : class
    where TSingleDto : class
    where TListDto : class
{
    private readonly IGenericRepository<T> _repository;
    private readonly IMapper _mapper;
    private readonly IServiceProvider _serviceProvider;

    public GenericCrudServiceWithMapping(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _repository = serviceProvider.GetRequiredService<IGenericRepository<T>>();
        _mapper = serviceProvider.GetRequiredService<IMapper>();
        _serviceProvider = serviceProvider;
    }

    protected virtual async Task<TSingleDto> MapToSingleDto(T entity) { await Task.CompletedTask; return _mapper.Map<TSingleDto>(entity); }
    protected virtual async Task<TListDto> MapToListDto(T entity) { await Task.CompletedTask; return _mapper.Map<TListDto>(entity); }
    protected virtual async Task<T> MapToFromCreateDto(TCreateDto dto) { await Task.CompletedTask; return _mapper.Map<T>(dto); }
    protected virtual async Task<T> MapToFromEditDto(TEditDto dto) { await Task.CompletedTask; return _mapper.Map<T>(dto); }
    protected virtual async Task<T> MapToEntity(Guid id, JsonPatchDocument<TEditDto> dto)
    {
        var entity = await _repository.GetById(id) ?? throw new EntityNotFoundException<T>(id);
        var originalDto = await MapToEditDto(entity);
        dto.ApplyTo(originalDto);
        return await MapToFromEditDto(originalDto);
    }

    private async Task<TEditDto> MapToEditDto(T entity)
    {
        await Task.CompletedTask;
        return _mapper.Map<TEditDto>(entity);
    }

    public new virtual async Task<TSingleDto> GetByIdAsync(Guid id) => await MapToSingleDto(await base.GetByIdAsync(id));
    public virtual async Task CreateAsync(TCreateDto dto) => await base.CreateAsync(await MapToFromCreateDto(dto));
    public virtual async Task UpdateAsync(TEditDto dto) => await base.UpdateAsync(await MapToFromEditDto(dto));
    public virtual async Task UpdateAsync(Guid id, JsonPatchDocument<TEditDto> dto) => await base.UpdateAsync(await MapToEntity(id, dto));
    public new virtual async Task<List<TListDto>> GetAllAsync() => (await Task.WhenAll((await base.GetAllAsync()).Select(x => MapToListDto(x)))).ToList();
    public new virtual async Task<List<TListDto>> GetByFilterAsync(Expression<Func<T, bool>> filter) => (await Task.WhenAll((await base.GetByFilterAsync(filter)).Select(x => MapToListDto(x)))).ToList();
    public new virtual async Task  DeleteAsync(Guid id) => await base.DeleteAsync(id);
}

