using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations;

public class FileConfiguration : AuditableEntityConfigurationBase<File>
{
    public override void Configure(EntityTypeBuilder<File> entity)
    {
        base.Configure(entity);

        entity.HasOne<Folder>()
            .WithMany()
            .HasForeignKey(x => x.FolderId);
    }
}
