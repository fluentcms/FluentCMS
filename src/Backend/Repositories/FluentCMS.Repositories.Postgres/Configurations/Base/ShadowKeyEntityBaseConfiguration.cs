namespace FluentCMS.Repositories.Postgres.Configurations.Base;

public class ShadowKeyEntityBaseConfiguration<T> : IEntityTypeConfiguration<T> where T : class
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.Property<Guid>("Id").ValueGeneratedOnAdd();

        builder.HasKey("Id");
    }
}
