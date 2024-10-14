using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.Postgres.Workers;

public class DatabaseMigrator : BackgroundService
{
    readonly IServiceScope _scope;
    readonly PostgresDbContext _context;
    readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(IServiceProvider serviceProvider, ILogger<DatabaseMigrator> logger)
    {
        _scope = serviceProvider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _context.Database.MigrateAsync(stoppingToken);
    }

    public override void Dispose()
    {
        base.Dispose();
        _scope.Dispose();
    }
}
