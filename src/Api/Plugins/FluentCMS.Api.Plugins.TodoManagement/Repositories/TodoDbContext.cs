namespace FluentCMS.Api.Plugins.TodoManagement.Repositories;

internal class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options), ITodoDatabaseMarker
{
    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure the Todo entity
        modelBuilder.Entity<Todo>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<Todo>()
            .Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(150);

        modelBuilder.Entity<Todo>()
            .Property(t => t.Description)
            .HasMaxLength(500);
    }
}
