using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories;

public abstract class DataSeeder<TDbContext>(TDbContext dbContext, ILogger<DataSeeder<TDbContext>> logger) : IDataSeeder
    where TDbContext : DbContext
{
    protected readonly TDbContext DbContext = dbContext;
    public abstract int Priority { get; }
    public abstract Task<bool> HasData(CancellationToken cancellationToken = default);
    public abstract Task SeedData(CancellationToken cancellationToken = default);
}

