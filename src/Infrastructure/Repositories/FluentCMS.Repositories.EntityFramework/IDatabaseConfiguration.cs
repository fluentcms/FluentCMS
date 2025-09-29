using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.EntityFramework;

// In shared abstractions project
public interface IDatabaseConfiguration
{
    void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder);
}
