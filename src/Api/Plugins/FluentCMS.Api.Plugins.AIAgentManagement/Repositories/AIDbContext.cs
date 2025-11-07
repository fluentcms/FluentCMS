namespace FluentCMS.Api.Plugins.AIAgentManagement.Repositories;

internal class AIDbContext(DbContextOptions<AIDbContext> options) : DbContext(options), IAIAgentDatabaseMarker
{
    public DbSet<AgentInfo> Agents => Set<AgentInfo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure the Todo entity
        modelBuilder.Entity<AgentInfo>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<AgentInfo>()
            .Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(150);

        modelBuilder.Entity<AgentInfo>()
            .Property(t => t.Description)
            .HasMaxLength(500);
    }
}
