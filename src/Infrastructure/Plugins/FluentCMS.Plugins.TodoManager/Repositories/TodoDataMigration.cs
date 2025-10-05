using FluentCMS.Repositories;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.TodoManager.Repositories;

internal class TodoDataMigration(TodoDbContext dbContext, ILogger<DataMigration<TodoDbContext>> logger) : DataMigration<TodoDbContext>(dbContext, logger)
{
    public override int Priority => 1000;
}
