using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations;

public class GlobalSettingsConfiguration : AuditableEntityConfigurationBase<GlobalSettings>
{
    public override void Configure(EntityTypeBuilder<GlobalSettings> entity)
    {
        base.Configure(entity);

        entity.OwnsOne(x => x.FileUpload, builder => { builder.ToJson(); });

    }
}
