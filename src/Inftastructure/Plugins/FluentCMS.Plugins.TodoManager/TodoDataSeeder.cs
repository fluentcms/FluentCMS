using FluentCMS.Database.Abstractions;
using FluentCMS.DataSeeding.Abstractions;
using FluentCMS.Plugins.TodoManagement.Models;
using FluentCMS.Plugins.TodoManager.Repositories;

namespace FluentCMS.Plugins.TodoManager;

public class TodoDataSeeder(TodoDbContext dbContext, IDatabaseManager<ITodoDatabaseMarker> databaseManager) : IDataSeeder
{
    public int Priority => 10000;

    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        await dbContext.Todos.AddRangeAsync([
            new Todo
            {
                Title = "Complete EF Core tutorial",
                Description = "Finish the Entity Framework Core getting started tutorial",
                IsCompleted = false,
                DueDate = DateTime.Now.AddDays(3)
            },
            new Todo
            {
                Title = "Buy groceries",
                Description = "Milk, eggs, bread, and vegetables",
                IsCompleted = false,
                DueDate = DateTime.Now.AddDays(1)
            }
        ], cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasData(CancellationToken cancellationToken = default)
    {
        return !await databaseManager.TablesEmpty(["Todos"], cancellationToken);
    }
}
