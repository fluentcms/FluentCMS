namespace FluentCMS.Infrastructure.Identity.Repositories.EntityFramework;

internal class UserRoleRepository<TUser, TRole>(ApplicationDbContext<TUser, TRole> context) :
    Repository<UserRole, ApplicationDbContext<TUser, TRole>>(context), IUserRoleRepository
    where TUser : UserBase
    where TRole : RoleBase
{
}
