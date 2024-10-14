using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations;

public class ContentTypeFieldConfiguration : ShadowKeyEntityBaseConfiguration<ContentTypeField>
{
    public override void Configure(EntityTypeBuilder<ContentTypeField> entity)
    {
        base.Configure(entity);

        entity.Property(x => x.Settings).HasColumnType("jsonb");

        entity.HasOne<ContentType>()
            .WithMany()
            .HasForeignKey("TypeId");

    }
}
