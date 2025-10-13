namespace FluentCMS.Infrastructure.Identity.Repositories.EntityFramework;

internal class UserRepository<TUser, TRole>(ApplicationDbContext<TUser, TRole> context) :
    Repository<TUser, ApplicationDbContext<TUser, TRole>>(context), IUserRepository<TUser>
    where TUser : UserBase
    where TRole : RoleBase
{
}
