namespace FluentCMS.Repositories.Abstractions.Examples;

/// <summary>
/// Example data seeder for Todo items.
/// Demonstrates how to seed initial TodoItem data.
/// </summary>
public class TodoDataSeeder(ITodoDatabaseMarker context) : IDataSeeder<ITodoDatabaseMarker>
{
    /// <summary>
    /// Execution priority for this seeder. Lower numbers execute first.
    /// </summary>
    public int Priority => 1000;

    /// <summary>
    /// Checks if TodoItem data already exists.
    /// Returns true if any TodoItems exist, false otherwise.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if data exists and seeding should be skipped, false otherwise</returns>
    public async Task<bool> HasData(CancellationToken cancellationToken = default)
    {
        // Check if any TodoItems exist in the database
        var dbContext = (TodoDbContext)context;
        return await dbContext.CreateQuerySpecification<TodoItem>().Any(cancellationToken);
    }

    /// <summary>
    /// Seeds the database with initial TodoItem data.
    /// Creates sample TodoItems demonstrating different states.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        var dbContext = (TodoDbContext)context;
        var now = DateTime.UtcNow;

        // Seed sample TodoItems
        var sampleTodos = new[]
        {
            new TodoItem
            {
                Title = "Welcome to FluentCMS Repository!",
                Description = "This is a sample todo item created by the data seeder.",
                IsCompleted = true,
                CreatedAt = now.AddDays(-1),
                CompletedAt = now
            },
            new TodoItem
            {
                Title = "Learn EF Core",
                Description = "Study Entity Framework Core best practices and patterns.",
                IsCompleted = false,
                CreatedAt = now.AddHours(-2),
                CompletedAt = null
            },
            new TodoItem
            {
                Title = "Write Unit Tests",
                Description = "Implement comprehensive unit tests for the Todo repository.",
                IsCompleted = false,
                CreatedAt = now.AddHours(-1),
                CompletedAt = null
            }
        };

        // Add all sample todos to the context
        await dbContext.AddRangeAsync(sampleTodos, cancellationToken);

        // Save changes to persist the data
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
