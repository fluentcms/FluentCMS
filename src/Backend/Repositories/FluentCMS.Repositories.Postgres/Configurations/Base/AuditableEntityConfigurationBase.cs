namespace FluentCMS.Repositories.Postgres.Configurations.Base;

public class AuditableEntityConfigurationBase<T> : EntityConfigurationBase<T> where T : AuditableEntity
{
    public override void Configure(EntityTypeBuilder<T> entity)
    {
        base.Configure(entity);

        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ModifiedAt).HasColumnType("timestamp with time zone");
    }
}
