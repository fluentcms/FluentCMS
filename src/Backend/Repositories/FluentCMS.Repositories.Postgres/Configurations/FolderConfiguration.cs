using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations;

public class FolderConfiguration : AuditableEntityConfigurationBase<Folder>
{
    public override void Configure(EntityTypeBuilder<Folder> entity)
    {
        base.Configure(entity);

        entity.HasOne<Folder>()
            .WithMany()
            .HasForeignKey(x => x.FolderId);

    }
}
