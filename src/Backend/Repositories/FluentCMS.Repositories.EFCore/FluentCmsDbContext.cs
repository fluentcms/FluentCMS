using Microsoft.AspNetCore.Identity;
using System.Text.Json;

namespace FluentCMS.Repositories.EFCore;

public class FluentCmsDbContext(DbContextOptions<FluentCmsDbContext> options) : DbContext(options)
{

    #region DbSets

    public DbSet<ApiToken> ApiTokens { get; set; } = default!;
    public DbSet<Block> Blocks { get; set; } = default!;
    public DbSet<Content> Contents { get; set; } = default!;
    public DbSet<ContentType> ContentTypes { get; set; } = default!;
    public DbSet<ContentTypeField> ContentTypeFields { get; set; } = default!;
    public DbSet<File> Files { get; set; } = default!;
    public DbSet<Folder> Folders { get; set; } = default!;
    public DbSet<GlobalSettings> GlobalSettings { get; set; } = default!;
    public DbSet<Layout> Layouts { get; set; } = default!;
    public DbSet<Page> Pages { get; set; } = default!;
    public DbSet<Permission> Permissions { get; set; } = default!;
    public DbSet<PluginContent> PluginContents { get; set; } = default!;
    public DbSet<PluginDefinition> PluginDefinitions { get; set; } = default!;
    public DbSet<Plugin> Plugins { get; set; } = default!;
    public DbSet<Role> Roles { get; set; } = default!;
    public DbSet<Settings> Settings { get; set; } = default!;
    public DbSet<Site> Sites { get; set; } = default!;
    public DbSet<User> Users { get; set; } = default!;
    public DbSet<UserRole> UserRoles { get; set; } = default!;

    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        #region Settings

        modelBuilder.Entity<Settings>(entity =>
        {
            // Configure the Values dictionary to be stored as a JSON string
            entity.Property(e => e.Values)
                .HasConversion(
                    // Serialize the dictionary to JSON when saving to the database
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    // Deserialize the JSON string back to a dictionary when reading from the database
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions)null)
                );
        });

        #endregion

        #region GlobalSettings

        // Configure SuperAdmins as a comma-separated string
        modelBuilder.Entity<GlobalSettings>()
            .Ignore(gs => gs.SuperAdmins)
            .Ignore(gs => gs.FileUpload)
            .Property(gs => gs.SuperAdmins)
            .HasConversion(
                v => string.Join(",", v), // Convert IEnumerable<string> to string for storage
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() // Convert string to IEnumerable<string>
            );

        #endregion

        #region ContentType and ContentTypeField

        modelBuilder.Entity<ContentType>(entity =>
        {
            // Define one-to-many relationship between ContentType and ContentTypeField
            entity.HasMany(c => c.Fields)
                  .WithOne()
                  .HasForeignKey("ContentTypeId") // Shadow property for the foreign key
                  .OnDelete(DeleteBehavior.Cascade); // Configure cascade delete if needed
        });

        modelBuilder.Entity<ContentTypeField>(entity =>
        {
            // Configure Settings to be stored as JSON
            entity.Property(e => e.Settings)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null), // Convert to JSON when saving
                    v => JsonSerializer.Deserialize<Dictionary<string, object?>>(v, (JsonSerializerOptions)null) // Convert back to dictionary when reading
                );

            // Define the shadow property "Id" as the primary key
            entity.Property<Guid>("Id").ValueGeneratedOnAdd();
            entity.HasKey("Id");
        });

        #endregion

        #region Content

        modelBuilder.Entity<Content>(entity =>
        {
            // Configure the Data property to be stored as a JSON string
            entity.Property(e => e.Data)
                .HasConversion(
                    // Serialize the dictionary to JSON when saving to the database
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    // Deserialize the JSON string back to a dictionary when reading from the database
                    v => JsonSerializer.Deserialize<Dictionary<string, object?>>(v, (JsonSerializerOptions)null)
                );
        });

        #endregion

        #region Plugin Content

        modelBuilder.Entity<PluginContent>(entity =>
        {
            // Configure the Data property to be stored as a JSON string
            entity.Property(e => e.Data)
                .HasConversion(
                    // Serialize the dictionary to JSON when saving to the database
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    // Deserialize the JSON string back to a dictionary when reading from the database
                    v => JsonSerializer.Deserialize<Dictionary<string, object?>>(v, (JsonSerializerOptions)null)
                );
        });

        #endregion

        #region ApiToken and Policy

        modelBuilder.Entity<ApiToken>(entity =>
        {
            entity.HasMany(e => e.Policies)
              .WithOne()
              .HasForeignKey("ApiTokenId") // Shadow foreign key property in Policy table
              .OnDelete(DeleteBehavior.Cascade); // Cascade delete policies if ApiToken is deleted
        });

        // Configure the Policy entity
        modelBuilder.Entity<Policy>(entity =>
        {
            // Store Actions as a comma-separated string
            entity.Property(e => e.Actions)
                .HasConversion(
                    v => string.Join(",", v), // Convert list to comma-separated string when saving
                    v => v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList() // Convert back to list when reading
                );


            // Define the shadow property "Id" as the primary key
            entity.Property<Guid>("Id").ValueGeneratedOnAdd();
            entity.HasKey("Id");
        });

        #endregion        

        #region PluginDefition

        // Configure PluginDefinition entity
        modelBuilder.Entity<PluginDefinition>(entity =>
        {
            // Define one-to-many relationship between PluginDefinition and PluginDefinitionType
            entity.HasMany(p => p.Types)
                  .WithOne() // No navigation property back to PluginDefinition in PluginDefinitionType
                  .HasForeignKey("PluginDefinitionId") // Shadow property for the foreign key
                  .OnDelete(DeleteBehavior.Cascade); // Configure cascade delete if needed
        });

        // Configure PluginDefinitionType entity
        modelBuilder.Entity<PluginDefinitionType>(entity =>
        {
            // Define the shadow property for Id
            entity.Property<Guid>("Id")
                  .ValueGeneratedOnAdd(); // Configure it to be generated by the database

            // Configure composite key (or use Id as the primary key if preferred)
            entity.HasKey("Id"); // Using the shadow Id as the primary key
        });

        #endregion

        #region User and Role

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            // Define one-to-many relationships with shadow foreign keys for each child collection
            entity.HasMany(u => u.Logins)
                  .WithOne()
                  .HasForeignKey("UserId") // Shadow foreign key for User
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(u => u.Tokens)
                  .WithOne()
                  .HasForeignKey("UserId") // Shadow foreign key for User
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(u => u.Claims)
                  .WithOne()
                  .HasForeignKey("UserId") // Shadow foreign key for User
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(u => u.RecoveryCodes)
                  .WithOne()
                  .HasForeignKey("UserId") // Shadow foreign key for User
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserTwoFactorRecoveryCode as a separate entity
        modelBuilder.Entity<UserTwoFactorRecoveryCode>(entity =>
        {
            // Define the shadow property for Id
            entity.Property<Guid>("Id")
                  .ValueGeneratedOnAdd(); // Configure it to be generated by the database

            // Configure composite key (or use Id as the primary key if preferred)
            entity.HasKey("Id"); // Using the shadow Id as the primary key
        });

        // Configure IdentityUserLogin, IdentityUserToken, and IdentityUserClaim with composite keys
        modelBuilder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.HasKey(login => new { login.LoginProvider, login.ProviderKey });
        });

        modelBuilder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.HasKey(token => new { token.UserId, token.LoginProvider, token.Name });
        });

        modelBuilder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.HasKey(claim => claim.Id); // Id as primary key for claims
        });

        #endregion

        #region Site

        modelBuilder.Entity<Site>(entity =>
        {
            // Configure the Urls property to be stored as a comma-separated string
            entity.Property(e => e.Urls)
                .HasConversion(
                    // Convert the List<string> to a comma-separated string for storage
                    v => string.Join(",", v),
                    // Convert the comma-separated string back to a List<string> when reading
                    v => v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList()
                );
        });

        #endregion

        base.OnModelCreating(modelBuilder);
    }
}
