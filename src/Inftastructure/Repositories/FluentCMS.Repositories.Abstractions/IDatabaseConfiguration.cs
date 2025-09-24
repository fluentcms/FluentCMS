namespace FluentCMS.Repositories.Abstractions;

// In shared abstractions project
public interface IDatabaseConfiguration
{
    void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder);
}
