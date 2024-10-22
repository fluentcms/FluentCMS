using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations.SiteAssociatedEntities;

public class PluginContentConfiguration : SiteAssociatedEntityConfigurationBase<PluginContent>
{
    public override void Configure(EntityTypeBuilder<PluginContent> entity)
    {
        base.Configure(entity);

        entity.HasOne<Plugin>()
            .WithMany()
            .HasForeignKey(x => x.PluginId);

        entity.OwnsOne(x => x.Data, builder => builder.ToJson());
    }
}
