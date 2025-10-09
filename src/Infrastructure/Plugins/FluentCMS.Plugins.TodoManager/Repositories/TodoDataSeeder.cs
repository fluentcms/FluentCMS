namespace FluentCMS.Plugins.TodoManager.Repositories;

internal class TodoDataSeeder(TodoDbContext dbContext, ILogger<TodoDataSeeder> logger) : BaseDataSeeder<TodoDbContext>(dbContext, logger)
{
    public override int Priority => 10000;

    public override async Task SeedData(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Seeding initial todo items into the database...");
        await DbContext.Todos.AddRangeAsync([
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

        await DbContext.SaveChangesAsync(cancellationToken);
    }
}
