namespace FluentCMS.Services;

public interface IContentTypeService : IService
{
    Task<IEnumerable<ContentType>> GetAll(Guid appId, CancellationToken cancellationToken = default);
    Task<ContentType> GetBySlug(Guid appId, string contentTypeSlug, CancellationToken cancellationToken = default);
    Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default);
    Task<ContentType> Update(ContentType contentType, CancellationToken cancellationToken = default);
    Task<ContentType> Delete(Guid appId, Guid contentTypeId, CancellationToken cancellationToken = default);
    Task<ContentType> SetField(Guid appId, Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default);
    Task<ContentType> DeleteField(Guid appId, Guid contentTypeId, string fieldSlug, CancellationToken cancellationToken = default);
}

public class ContentTypeService(IContentTypeRepository contentTypeRepository) : IContentTypeService
{
    public Task<IEnumerable<ContentType>> GetAll(Guid appId, CancellationToken cancellationToken = default)
    {
        return contentTypeRepository.GetAll(appId, cancellationToken);
    }

    public async Task<ContentType> GetBySlug(Guid appId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        return await contentTypeRepository.GetBySlug(appId, contentTypeSlug, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);
    }

    public async Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default)
    {
        return await contentTypeRepository.Create(contentType, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToCreate);
    }

    public async Task<ContentType> Delete(Guid appId, Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        if (contentType.AppId != appId)
            throw new AppException(ExceptionCodes.ContentTypeInvalidAppId);

        return await contentTypeRepository.Delete(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToDelete);
    }

    public async Task<ContentType> Update(ContentType contentType, CancellationToken cancellationToken = default)
    {
        // only allow name and description to be updated
        var original = await contentTypeRepository.GetById(contentType.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        if (original.AppId != contentType.AppId)
            throw new AppException(ExceptionCodes.ContentTypeInvalidAppId);

        original.Title = contentType.Title;
        original.Description = contentType.Description;

        return await contentTypeRepository.Update(original, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
    }

    public async Task<ContentType> SetField(Guid appId, Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        // load the content type
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        if (contentType.AppId != appId)
            throw new AppException(ExceptionCodes.ContentTypeInvalidAppId);

        // check the field exists
        var original = contentType.Fields.FirstOrDefault(f => f.Slug == field.Slug);

        if (original == null)
        {
            // add the field
            return await contentTypeRepository.AddField(contentTypeId, field, cancellationToken) ??
                throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
        }
        else
        {
            // update the field
            return await contentTypeRepository.UpdateField(contentTypeId, field, cancellationToken) ??
                throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
        }

    }

    public async Task<ContentType> DeleteField(Guid appId, Guid contentTypeId, string fieldSlug, CancellationToken cancellationToken = default)
    {
        // load the content type
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        if (contentType.AppId != appId)
            throw new AppException(ExceptionCodes.ContentTypeInvalidAppId);

        // check the field exists
        var original = contentType.Fields.FirstOrDefault(f => f.Slug == fieldSlug) ??
            throw new AppException(ExceptionCodes.ContentTypeFieldNotFound);

        // remove the field
        return await contentTypeRepository.RemoveField(contentTypeId, fieldSlug, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
    }
}
