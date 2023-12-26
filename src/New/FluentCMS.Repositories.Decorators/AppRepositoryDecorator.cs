namespace FluentCMS.Repositories.Decorators;

public class AppRepositoryDecorator : AppAssociatedRepositoryDecorator<App>, IAppRepository
{
    private readonly IAppRepository _decorator;

    public AppRepositoryDecorator(IAuthContext authContext, IAppRepository decorator) : base(authContext, decorator)
    {
        _decorator = decorator;
    }

    public Task<App?> GetBySlug(string slug, CancellationToken cancellationToken = default)
    {
        return _decorator.GetBySlug(slug, cancellationToken);
    }
}
