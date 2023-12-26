namespace FluentCMS.Repositories.Decorators;

public class RoleRepositoryDecorator : AppAssociatedRepositoryDecorator<Role>, IRoleRepository
{
    public RoleRepositoryDecorator(IAuthContext authContext, IRoleRepository decorator) : base(authContext, decorator)
    {
    }
}
