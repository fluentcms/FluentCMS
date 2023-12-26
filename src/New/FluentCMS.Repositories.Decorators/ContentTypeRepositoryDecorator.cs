namespace FluentCMS.Repositories.Decorators;

public class ContentTypeRepositoryDecorator : AppAssociatedRepositoryDecorator<ContentType>, IContentTypeRepository
{
    private readonly IContentTypeRepository _decorator;

    public ContentTypeRepositoryDecorator(IAuthContext authContext, IContentTypeRepository decorator) : base(authContext, decorator)
    {
        _decorator = decorator;
    }

    public Task<ContentType?> AddField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        return _decorator.AddField(contentTypeId, field, cancellationToken);
    }

    public Task<ContentType?> GetBySlug(Guid appId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        return _decorator.GetBySlug(appId, contentTypeSlug, cancellationToken);
    }

    public Task<ContentType?> RemoveField(Guid contentTypeId, string fieldSlug, CancellationToken cancellationToken = default)
    {
        return _decorator.RemoveField(contentTypeId, fieldSlug, cancellationToken);
    }

    public Task<ContentType?> UpdateField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        return _decorator.UpdateField(contentTypeId, field, cancellationToken);
    }
}
