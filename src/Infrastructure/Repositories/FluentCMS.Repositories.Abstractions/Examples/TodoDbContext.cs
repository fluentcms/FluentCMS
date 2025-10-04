using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.Abstractions.Examples;

// Custom DbContext for Todo area with direct EF usage
public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    // Define DbSets for entities in this context
    public DbSet<TodoItem> Todos => Set<TodoItem>();

    // Configure model if needed
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Configure entities specific to Todo area
    }
}
