using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations;

public class ContentTypeConfiguration : AuditableEntityConfigurationBase<ContentType>
{
    public override void Configure(EntityTypeBuilder<ContentType> entity)
    {
        base.Configure(entity);

    }
}
