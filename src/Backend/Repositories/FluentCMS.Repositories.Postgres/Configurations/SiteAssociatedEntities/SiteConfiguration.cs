using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations.SiteAssociatedEntities;

public class SiteConfiguration : AuditableEntityConfigurationBase<Site>
{
    public override void Configure(EntityTypeBuilder<Site> entity)
    {
        base.Configure(entity);

        entity.OwnsOne(x => x.Urls, builder => builder.ToJson());

        entity.HasOne<Layout>()
            .WithMany()
            .HasForeignKey(x => x.LayoutId);

        entity.HasOne<Layout>()
            .WithMany()
            .HasForeignKey(x => x.DetailLayoutId);

        entity.HasOne<Layout>()
            .WithMany()
            .HasForeignKey(x => x.EditLayoutId);
    }
}
