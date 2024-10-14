namespace FluentCMS.Repositories.Postgres.Configurations;

public class PluginDefinitionTypeConfiguration : IEntityTypeConfiguration<PluginDefinitionType>
{
    public void Configure(EntityTypeBuilder<PluginDefinitionType> builder)
    {
        builder.HasKey(x => new{x.Type, x.Name});

        builder.HasOne<PluginDefinition>()
            .WithMany()
            .HasForeignKey("DefinitionId");
    }
}
