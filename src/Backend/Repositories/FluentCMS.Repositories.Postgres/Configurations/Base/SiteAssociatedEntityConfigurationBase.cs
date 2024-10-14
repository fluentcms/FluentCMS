namespace FluentCMS.Repositories.Postgres.Configurations.Base;

public class SiteAssociatedEntityConfigurationBase<T> : AuditableEntityConfigurationBase<T> where T : SiteAssociatedEntity
{
    public override void Configure(EntityTypeBuilder<T> entity)
    {
        base.Configure(entity);

        entity.HasOne<Site>()
            .WithMany()
            .HasForeignKey(x => x.SiteId);
    }
}
