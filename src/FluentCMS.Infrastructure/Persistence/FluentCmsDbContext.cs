using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Persistence;

public sealed class FluentCmsDbContext(DbContextOptions<FluentCmsDbContext> options) : DbContext(options);
