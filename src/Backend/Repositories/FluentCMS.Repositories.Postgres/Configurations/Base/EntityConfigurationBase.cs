namespace FluentCMS.Repositories.Postgres.Configurations.Base;

public class EntityConfigurationBase<T> : IEntityTypeConfiguration<T> where T : Entity
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(x => x.Id);
    }


}

