using FluentCMS.Repositories.EntityFramework;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.TodoManager.Repositories;

internal class TodoSchemaValidator(TodoDbContext dbContext, ILogger<TodoSchemaValidator> logger) : EfSchemaValidator<TodoDbContext>(dbContext, logger)
{
    public override int Priority => 10000;
}
