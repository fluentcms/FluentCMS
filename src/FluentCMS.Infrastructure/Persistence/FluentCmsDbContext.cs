using FluentCMS.Infrastructure.Agents;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Checks;
using FluentCMS.Infrastructure.Content;
using FluentCMS.Infrastructure.Entities;
using FluentCMS.Infrastructure.Jobs;
using FluentCMS.Infrastructure.Models;
using FluentCMS.Infrastructure.Schema;
using FluentCMS.Infrastructure.Sites;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Persistence;

// One model, one provider-specific subclass per database so each keeps its own migrations (ADR-0005).
public abstract class FluentCmsDbContext(DbContextOptions options) : IdentityDbContext<FluentCmsUser, FluentCmsRole, Guid>(options)
{
    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    public DbSet<SiteRow> Sites => Set<SiteRow>();

    public DbSet<ContentTypeRow> ContentTypes => Set<ContentTypeRow>();

    public DbSet<ContentTypeVersionRow> ContentTypeVersions => Set<ContentTypeVersionRow>();

    public DbSet<ChangeSetRow> ChangeSets => Set<ChangeSetRow>();

    public DbSet<ChangeSetOperationRow> ChangeSetOperations => Set<ChangeSetOperationRow>();

    public DbSet<AgentCredentialRow> AgentCredentials => Set<AgentCredentialRow>();

    public DbSet<ToolCallRow> ToolCalls => Set<ToolCallRow>();

    public DbSet<EntryRow> Entries => Set<EntryRow>();

    public DbSet<EntryVersionRow> EntryVersions => Set<EntryVersionRow>();

    public DbSet<EntryReferenceRow> EntryReferences => Set<EntryReferenceRow>();

    public DbSet<EntityFactRow> EntityFacts => Set<EntityFactRow>();

    public DbSet<CheckRow> Checks => Set<CheckRow>();

    public DbSet<CheckVersionRow> CheckVersions => Set<CheckVersionRow>();

    public DbSet<JobRow> Jobs => Set<JobRow>();

    public DbSet<ModelCallRow> ModelCalls => Set<ModelCallRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new AuditEventConfiguration());
        builder.ApplyConfiguration(new SiteConfiguration());
        builder.ApplyConfiguration(new ContentTypeConfiguration());
        builder.ApplyConfiguration(new ContentTypeVersionConfiguration());
        builder.ApplyConfiguration(new ChangeSetConfiguration());
        builder.ApplyConfiguration(new ChangeSetOperationConfiguration());
        builder.ApplyConfiguration(new AgentCredentialConfiguration());
        builder.ApplyConfiguration(new ToolCallConfiguration());
        builder.ApplyConfiguration(new EntryConfiguration());
        builder.ApplyConfiguration(new EntryVersionConfiguration());
        builder.ApplyConfiguration(new EntryReferenceConfiguration());
        builder.ApplyConfiguration(new EntityFactConfiguration());
        builder.ApplyConfiguration(new CheckConfiguration());
        builder.ApplyConfiguration(new CheckVersionConfiguration());
        builder.ApplyConfiguration(new JobConfiguration());
        builder.ApplyConfiguration(new ModelCallConfiguration());

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

public sealed class PostgreSqlFluentCmsDbContext(DbContextOptions<PostgreSqlFluentCmsDbContext> options) : FluentCmsDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Definitions and entry fields are JSON documents, stored as jsonb where the provider has it (ADR-0005).
        builder.Entity<ContentTypeVersionRow>().Property(version => version.Definition).HasColumnType("jsonb");
        builder.Entity<EntryVersionRow>().Property(version => version.Fields).HasColumnType("jsonb");
        builder.Entity<CheckVersionRow>().Property(version => version.Definition).HasColumnType("jsonb");
    }
}
