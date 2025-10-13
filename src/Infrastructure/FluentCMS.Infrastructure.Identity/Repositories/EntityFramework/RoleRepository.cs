namespace FluentCMS.Infrastructure.Identity.Repositories.EntityFramework;

internal class RoleRepository<TUser, TRole>(ApplicationDbContext<TUser, TRole> context) :
    Repository<TRole, ApplicationDbContext<TUser, TRole>>(context), IRoleRepository<TRole>
    where TUser : UserBase
    where TRole : RoleBase
{
}

