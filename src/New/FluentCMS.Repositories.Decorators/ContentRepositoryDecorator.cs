namespace FluentCMS.Repositories.Decorators;

public class ContentRepositoryDecorator : AppAssociatedRepositoryDecorator<Content>, IContentRepository
{
    private readonly IContentRepository _decorator;

    public ContentRepositoryDecorator(IAuthContext authContext, IContentRepository decorator) : base(authContext, decorator)
    {
        _decorator = decorator;
    }

    public Task<IEnumerable<Content>> GetAll(Guid appId, Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        return _decorator.GetAll(appId, contentTypeId, cancellationToken);
    }
}
