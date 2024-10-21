using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations;

public class SettingsConfiguration : AuditableEntityConfigurationBase<Settings>
{
    public override void Configure(EntityTypeBuilder<Settings> entity)
    {
        base.Configure(entity);

        entity.OwnsOne(x => x.Values, builder => { builder.ToJson(); });
    }
}
