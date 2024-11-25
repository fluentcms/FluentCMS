using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FluentCMS.Repositories.EFCore;

public class FluentCmsDbContext(DbContextOptions<FluentCmsDbContext> options) : DbContext(options)
{

    #region DbSets

    // DbSets for all entities
    public DbSet<ApiTokenModel> ApiTokens { get; set; } = default!;
    public DbSet<ApiTokenPolicyModel> ApiTokenPolicies { get; set; } = default!;
    public DbSet<BlockModel> Blocks { get; set; } = default!;
    public DbSet<ContentModel> Contents { get; set; } = default!;
    public DbSet<ContentTypeModel> ContentTypes { get; set; } = default!;
    public DbSet<ContentTypeFieldModel> ContentTypeFields { get; set; } = default!;
    public DbSet<FileModel> Files { get; set; } = default!;
    public DbSet<FolderModel> Folders { get; set; } = default!;
    public DbSet<GlobalSettingsModel> GlobalSettings { get; set; } = default!;
    public DbSet<LayoutModel> Layouts { get; set; } = default!;
    public DbSet<PageModel> Pages { get; set; } = default!;
    public DbSet<PermissionModel> Permissions { get; set; } = default!;
    public DbSet<PluginModel> Plugins { get; set; } = default!;
    public DbSet<PluginContentModel> PluginContents { get; set; } = default!;
    public DbSet<PluginDefinitionModel> PluginDefinitions { get; set; } = default!;
    public DbSet<PluginDefinitionTypeModel> PluginDefinitionTypes { get; set; } = default!;
    public DbSet<RoleModel> Roles { get; set; } = default!;
    public DbSet<SiteModel> Sites { get; set; } = default!;
    public DbSet<SettingsModel> Settings { get; set; } = default!;
    public DbSet<SettingValuesModel> SettingValues { get; set; } = default!;
    public DbSet<UserModel> Users { get; set; } = default!;
    public DbSet<UserRoleModel> UserRoles { get; set; } = default!;

    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var jsonSerializerOptions = new JsonSerializerOptions();
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        modelBuilder.Entity<ApiTokenModel>(entity =>
        {
            entity.HasMany(e => e.Policies)
                .WithOne(p => p.ApiToken)
                .HasForeignKey(p => p.ApiTokenId)
                .OnDelete(DeleteBehavior.Cascade); // Cascade delete on related Policies

            entity.Navigation(e => e.Policies).AutoInclude();
        });

        modelBuilder.Entity<ApiTokenPolicyModel>(entity =>
        {
            entity.HasOne(e => e.ApiToken)
                .WithMany(e => e.Policies)
                .HasForeignKey(e => e.ApiTokenId);
        });

        modelBuilder.Entity<ContentTypeModel>(entity =>
        {
            entity.HasMany(e => e.Fields)
                .WithOne(f => f.ContentType)
                .HasForeignKey(f => f.ContentTypeId)
                .OnDelete(DeleteBehavior.Cascade); // Cascade delete on related Fields

            entity.Navigation(e => e.Fields).AutoInclude();
        });

        modelBuilder.Entity<PluginDefinitionModel>(entity =>
        {
            entity.HasMany(e => e.Types)
                .WithOne(f => f.PluginDefinition)
                .HasForeignKey(f => f.PluginDefinitionId)
                .OnDelete(DeleteBehavior.Cascade); // Cascade delete on related Fields

            entity.Navigation(e => e.Types).AutoInclude();
        });

        modelBuilder.Entity<SettingsModel>(entity =>
        {
            // Configure the relationship with SettingValuesModel
            entity.HasMany(e => e.Values) // One SettingsModel has many SettingValuesModel
                .WithOne(e => e.Setting) // Each SettingValuesModel belongs to one SettingsModel
                .HasForeignKey(e => e.SettingId) // Foreign key in SettingValuesModel
                .OnDelete(DeleteBehavior.Cascade); // Cascade delete

            // Enable automatic inclusion of navigation property
            entity.Navigation(e => e.Values).AutoInclude(); // Always include Values when querying SettingsModel
        });

        // Configure SettingValuesModel
        modelBuilder.Entity<SettingValuesModel>(entity =>
        {
            // Define the primary key
            entity.HasKey(e => e.Id);

            // Define the foreign key to SettingsModel
            entity.HasOne(e => e.Setting) // Each SettingValuesModel belongs to one SettingsModel
                .WithMany(e => e.Values) // One SettingsModel has many SettingValuesModel
                .HasForeignKey(e => e.SettingId) // Foreign key in SettingValuesModel
                .OnDelete(DeleteBehavior.Cascade); // Cascade delete on SettingsModel deletion

        });

        modelBuilder.Entity<UserModel>(entity =>
        {

            entity.Property(u => u.Tokens)
                .HasConversion(
                    tokens => JsonSerializer.Serialize(tokens, jsonSerializerOptions),
                    tokens => JsonSerializer.Deserialize<List<IdentityUserToken<Guid>>>(tokens, jsonSerializerOptions) ?? new List<IdentityUserToken<Guid>>());

            entity.Property(u => u.Logins)
                .HasConversion(
                    logins => JsonSerializer.Serialize(logins, jsonSerializerOptions),
                    logins => JsonSerializer.Deserialize<List<IdentityUserLogin<Guid>>>(logins, jsonSerializerOptions) ?? new List<IdentityUserLogin<Guid>>());

            entity.Property(u => u.RecoveryCodes)
                .HasConversion(
                    codes => JsonSerializer.Serialize(codes, jsonSerializerOptions),
                    codes => JsonSerializer.Deserialize<List<UserTwoFactorRecoveryCode>>(codes, jsonSerializerOptions) ?? new List<UserTwoFactorRecoveryCode>());

            entity.Property(u => u.Claims)
                .HasConversion(
                    claims => JsonSerializer.Serialize(claims, jsonSerializerOptions),
                    claims => JsonSerializer.Deserialize<List<IdentityUserClaim<Guid>>>(claims, jsonSerializerOptions) ?? new List<IdentityUserClaim<Guid>>());
        });

        base.OnModelCreating(modelBuilder);
    }

    //protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    //{
    //    // Configure the DbContext to use NoTracking by default
    //    optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    //}
}
