namespace FluentCMS.Api.Plugins.TodoManagement.Repositories;

internal class TodoSchemaValidator(TodoDbContext dbContext, ILogger<TodoSchemaValidator> logger) : BaseSchemaValidator<TodoDbContext>(dbContext, logger)
{
    public override int Priority => 10000;
}
