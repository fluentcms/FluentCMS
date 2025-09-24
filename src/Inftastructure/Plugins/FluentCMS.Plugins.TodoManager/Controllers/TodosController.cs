using FluentCMS.Plugins.TodoManagement.Models;
using FluentCMS.Plugins.TodoManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Plugins.TodoManager.Controllers;

public class TodosController(ITodoService service) : BaseController
{
    private static TodoResponseDto MapToResponseDto(Todo todo)
    {
        return new TodoResponseDto
        {
            Id = todo.Id,
            Title = todo.Title,
            Description = todo.Description,
            IsCompleted = todo.IsCompleted,
            DueDate = todo.DueDate,
            CreatedAt = todo.CreatedAt,
            Version = todo.Version
        };
    }

    [HttpGet]
    public async Task<ApiPagedResult<TodoResponseDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var todos = await service.GetAll(cancellationToken);
        var todoResponses = todos.Select(MapToResponseDto).ToList();
        return OkPaged(todoResponses);
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResult<TodoResponseDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var todo = await service.GetById(id, cancellationToken);
        return Ok(MapToResponseDto(todo));
    }

    // POST: api/todos
    [HttpPost]
    public async Task<ApiResult<TodoResponseDto>> Create(TodoCreateDto todoDto, CancellationToken cancellationToken = default)
    {
        var todo = new Todo
        {
            Title = todoDto.Title,
            Description = todoDto.Description,
            IsCompleted = todoDto.IsCompleted,
            DueDate = todoDto.DueDate
        };

        var created = await service.Add(todo, cancellationToken);
        return Ok(MapToResponseDto(created));
    }

    // PUT: api/todos/5
    [HttpPut("{id:guid}")]
    public async Task<ApiResult<TodoResponseDto>> Update(Guid id, TodoUpdateDto todoDto, CancellationToken cancellationToken = default)
    {
        var existingTodo = await service.GetById(id, cancellationToken);

        existingTodo.Title = todoDto.Title;
        existingTodo.Description = todoDto.Description;
        existingTodo.IsCompleted = todoDto.IsCompleted;
        existingTodo.DueDate = todoDto.DueDate;
        //existingTodo.Version++;

        var updated = await service.Update(existingTodo, cancellationToken);
        return Ok(MapToResponseDto(updated));
    }

    // DELETE: api/todos/5
    [HttpDelete("{id:guid}")]
    public async Task<ApiResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await service.Remove(id, cancellationToken);
        return Ok();
    }
}
