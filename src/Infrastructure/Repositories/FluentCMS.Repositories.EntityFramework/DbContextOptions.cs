using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.EntityFramework;

public class DbContextOptions<TDbContext, TMarker> : DbContextOptions<TDbContext>
    where TDbContext : DbContext
    where TMarker : IDatabaseScopeMarker
{
}
