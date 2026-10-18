using FluentCMS.Infrastructure.Audit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Persistence;

// One model, one provider-specific subclass per database so each keeps its own migrations (ADR-0005).
public abstract class FluentCmsDbContext(DbContextOptions options) : IdentityDbContext<FluentCmsUser, FluentCmsRole, Guid>(options)
{
    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new AuditEventConfiguration());

        builder.Entity<FluentCmsUser>(user =>
        {
            user.ToTable("Users");
            user.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
        });
        builder.Entity<FluentCmsRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
    }
}

public sealed class SqliteFluentCmsDbContext(DbContextOptions<SqliteFluentCmsDbContext> options) : FluentCmsDbContext(options);

public sealed class PostgreSqlFluentCmsDbContext(DbContextOptions<PostgreSqlFluentCmsDbContext> options) : FluentCmsDbContext(options);
