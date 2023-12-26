namespace FluentCMS.Repositories.Decorators;

public class SiteRepositoryDecorator : EntityRepositoryDecorator<Site>, ISiteRepository
{
    private readonly ISiteRepository _decorator;

    public SiteRepositoryDecorator(IAuthContext authContext, ISiteRepository decorator) : base(authContext, decorator)
    {
        _decorator = decorator;
    }

    public Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        return _decorator.GetByUrl(url, cancellationToken);
    }
}
