using FluentCMS.Plugins.TodoManager.Models;
using FluentCMS.Repositories.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.TodoManager.Repositories;

internal class TodoDataSeeder(TodoDbContext dbContext, ILogger<TodoDataSeeder> logger) : IDataSeeder
{
    public int Priority => 10000;

    public Task<bool> HasData(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Checking for existing todo items in the database...");
        return Task.FromResult(dbContext.Todos.Any());
    }

    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Seeding initial todo items into the database...");
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
}
