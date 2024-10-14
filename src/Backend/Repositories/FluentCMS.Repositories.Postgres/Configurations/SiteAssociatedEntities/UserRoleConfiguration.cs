using FluentCMS.Repositories.Postgres.Configurations.Base;

namespace FluentCMS.Repositories.Postgres.Configurations.SiteAssociatedEntities;

public class UserRoleConfiguration : SiteAssociatedEntityConfigurationBase<UserRole>
{
    public override void Configure(EntityTypeBuilder<UserRole> entity)
    {
        base.Configure(entity);

        entity.HasOne<Entities.User>()
            .WithMany()
            .HasForeignKey(x => x.UserId);

        entity.HasOne<Role>()
            .WithMany()
            .HasForeignKey(x => x.RoleId);
    }
}
