namespace FluentCMS.Api.Plugins.TodoManagement.Controllers;

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
            //CreatedAt = todo.CreatedAt,
            //Version = todo.Version
        };
    }

    [HttpGet]
    public async Task<ApiListResponse<TodoResponseDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var todos = await service.GetAll(cancellationToken);
        var todoResponses = todos.Select(MapToResponseDto).ToList();
        return SuccessList(todoResponses, 1, todoResponses.Count, todoResponses.Count);
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResponse<TodoResponseDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var todo = await service.GetById(id, cancellationToken) ??
            throw new KeyNotFoundException($"Todo with ID {id} not found.");
        return Success(MapToResponseDto(todo));
    }

    // POST: api/todos
    [HttpPost]
    public async Task<ApiResponse<TodoResponseDto>> Create(TodoCreateDto todoDto, CancellationToken cancellationToken = default)
    {
        var todo = new Todo
        {
            Title = todoDto.Title,
            Description = todoDto.Description,
            IsCompleted = todoDto.IsCompleted,
            DueDate = todoDto.DueDate
        };

        var created = await service.Add(todo, cancellationToken);
        return Success(MapToResponseDto(created));
    }

    // PUT: api/todos/5
    [HttpPut("{id:guid}")]
    public async Task<ApiResponse<TodoResponseDto>> Update(Guid id, TodoUpdateDto todoDto, CancellationToken cancellationToken = default)
    {
        var existingTodo = await service.GetById(id, cancellationToken) ??
            throw new KeyNotFoundException($"Todo with ID {id} not found.");

        existingTodo.Title = todoDto.Title;
        existingTodo.Description = todoDto.Description;
        existingTodo.IsCompleted = todoDto.IsCompleted;
        existingTodo.DueDate = todoDto.DueDate;
        //existingTodo.Version++;

        var updated = await service.Update(existingTodo, cancellationToken);
        return Success(MapToResponseDto(updated));
    }

    // DELETE: api/todos/5
    [HttpDelete("{id:guid}")]
    public async Task<ApiResponse> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await service.Remove(id, cancellationToken);
        return Success();
    }
}
