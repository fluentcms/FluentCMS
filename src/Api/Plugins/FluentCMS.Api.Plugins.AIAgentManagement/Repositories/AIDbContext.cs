namespace FluentCMS.Api.Plugins.AIAgentManagement.Repositories;

internal class AIDbContext(DbContextOptions<AIDbContext> options) : DbContext(options), IAIAgentDatabaseMarker
{
    public DbSet<AIThread> Threads => Set<AIThread>();
    public DbSet<Agent> Agents => Set<Agent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure the Agent entity
        modelBuilder.Entity<Agent>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<Agent>()
            .Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(150);

        modelBuilder.Entity<Agent>()
            .Property(t => t.Model)
            .IsRequired()
            .HasMaxLength(150);

        modelBuilder.Entity<Agent>()
            .Property(t => t.Description)
            .HasMaxLength(500);

        modelBuilder.Entity<Agent>()
            .Property(t => t.SystemPrompt)
            .HasMaxLength(5000);


        // Configure the Thread entity
        modelBuilder.Entity<AIThread>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<AIThread>()
            .Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(150);

        modelBuilder.Entity<AIThread>()
            .Property(t => t.Model)
            .IsRequired()
            .HasMaxLength(150);

        modelBuilder.Entity<AIThread>()
            .Property(t => t.Description)
            .HasMaxLength(500);

        modelBuilder.Entity<AIThread>()
            .Property(t => t.SystemPrompt)
            .HasMaxLength(5000);
    }
}
