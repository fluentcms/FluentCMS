namespace FluentCMS.Repositories.Postgres.Configurations.UserAssociatedEntities;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {

        builder.HasMany(x => x.RecoveryCodes)
            .WithOne();
    }
}
