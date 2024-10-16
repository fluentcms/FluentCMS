using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations.SiteAssociatedEntities;

public class RoleConfiguration : SiteAssociatedEntityConfigurationBase<Role>
{
    public override void Configure(EntityTypeBuilder<Role> entity)
    {
        base.Configure(entity);

        entity.Property(x => x.Type).HasConversion<string>();
    }
}
