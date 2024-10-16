using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations;

public class PolicyConfiguration : ShadowKeyEntityBaseConfiguration<Policy>
{
    public override void Configure(EntityTypeBuilder<Policy> entity)
    {
        base.Configure(entity);

        entity.OwnsOne(x => x.Actions, builder => { builder.ToJson(); });
    }
}
