using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations;

public class ContentConfiguration : AuditableEntityConfigurationBase<Content>
{
    public override void Configure(EntityTypeBuilder<Content> entity)
    {
        base.Configure(entity);

        entity.OwnsOne(x => x.Data, builder => {builder.ToJson(); });
    }
}
