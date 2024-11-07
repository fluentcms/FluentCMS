using System.Text.Json;

namespace FluentCMS.Repositories.EFCore;

public class FluentCmsDbContext : DbContext
{
    public DbSet<GlobalSettings> GlobalSettings { get; set; }
    public DbSet<SettingValue> SettingValues { get; set; } = default!; // Explicit DbSet for the shadow entity

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        #region Settings and SettingValues

        modelBuilder.Entity<SettingValue>().HasKey(sv => new { sv.SettingsId, sv.Key });

        modelBuilder.Entity<SettingValue>()
            .HasOne<Settings>()
            .WithMany()
            .HasForeignKey(sv => sv.SettingsId);

        // Configure cascade delete if deleting a Settings entity should delete related SettingValues
        modelBuilder.Entity<SettingValue>()
            .HasOne<Settings>()
            .WithMany()
            .HasForeignKey(sv => sv.SettingsId)
            .OnDelete(DeleteBehavior.Cascade);

        #endregion

        modelBuilder.Entity<GlobalSettings>(entity =>
        {
            // Configure FileUpload as an owned type
            entity.OwnsOne(gs => gs.FileUpload, fileUpload =>
            {
                fileUpload.Property(f => f.MaxSize).HasColumnName("FileUpload_MaxSize");
                fileUpload.Property(f => f.MaxCount).HasColumnName("FileUpload_MaxCount");
                fileUpload.Property(f => f.AllowedExtensions).HasColumnName("FileUpload_AllowedExtensions");
            });

            // Configure SuperAdmins as a JSON column if supported by the database provider
            entity.Property(gs => gs.SuperAdmins)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                      v => JsonSerializer.Deserialize<IEnumerable<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                  .HasColumnType("json"); // For databases like SQLite or PostgreSQL that support JSON types
        });

        modelBuilder.Entity<Settings>(entity =>
        {
            // Configure Id as the primary key
            entity.HasKey(e => e.Id);

            // Configure Values as a JSON column (if supported by the database)
            entity.Property(e => e.Values)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                      v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>())
                  .HasColumnType("json"); // Use "jsonb" for PostgreSQL, omit for databases that don't support JSON natively

            // Additional configurations, e.g., indexes on frequently queried columns
            entity.HasIndex(e => e.Id).IsUnique();

            // Configure CreatedAt and ModifiedAt as required fields if needed
            entity.Property(e => e.CreatedAt)
                  .IsRequired();
            entity.Property(e => e.CreatedBy)
                  .IsRequired();
            entity.Property(e => e.ModifiedAt)
                  .IsRequired(false);
            entity.Property(e => e.ModifiedBy)
                  .IsRequired(false);
        });

    }
}
