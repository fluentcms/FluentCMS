namespace FluentCMS.Services;

public interface IContentTypeService : IAutoRegisterService
{
    Task<IEnumerable<ContentType>> GetAll(CancellationToken cancellationToken = default);
    Task<ContentType> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default);
    Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default);
    Task<ContentType> Update(ContentType contentType, CancellationToken cancellationToken = default);
    Task<ContentType> Delete(Guid contentTypeId, CancellationToken cancellationToken = default);
    Task<ContentType> SetField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default);
    Task<ContentType> DeleteField(Guid contentTypeId, string name, CancellationToken cancellationToken = default);
    Task<ContentType> GetById(Guid id, CancellationToken cancellationToken);
}

public class ContentTypeService(IContentTypeRepository contentTypeRepository) : IContentTypeService
{
    public Task<IEnumerable<ContentType>> GetAll(CancellationToken cancellationToken = default)
    {
        return contentTypeRepository.GetAll(cancellationToken);
    }

    public async Task<ContentType> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        return await contentTypeRepository.GetBySlug(contentTypeSlug, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);
    }

    public async Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default)
    {
        await CheckDuplicateSlug(contentType);

        return await contentTypeRepository.Create(contentType, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToCreate);
    }

    public async Task<ContentType> Delete(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        return await contentTypeRepository.Delete(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToDelete);
    }

    public async Task<ContentType> Update(ContentType contentType, CancellationToken cancellationToken = default)
    {
        await CheckDuplicateSlug(contentType);

        // only allow name and description to be updated
        var original = await contentTypeRepository.GetById(contentType.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        original.Title = contentType.Title;
        original.Description = contentType.Description;

        return await contentTypeRepository.Update(original, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
    }

    private async Task CheckDuplicateSlug(ContentType contentType)
    {
        var originalBySlug = await contentTypeRepository.GetBySlug(contentType.Slug);
        if (originalBySlug != null && originalBySlug.Id != contentType.Id) throw new AppException(ExceptionCodes.ContentTypeDuplicateSlug);
    }

    public async Task<ContentType> SetField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        // load the content type
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        // check the field exists
        var originalField = contentType.Fields.FirstOrDefault(f => f.Name == field.Name);

        if (originalField == null)
            contentType.Fields.Add(field);
        else
        {
            originalField.Type = field.Type;
            originalField.Settings = field.Settings;
            originalField.Name = field.Name;
            originalField.Required = field.Required;
            originalField.Unique = field.Unique;
            originalField.Label = field.Label;
        }

        return await contentTypeRepository.Update(contentType, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
    }

    public async Task<ContentType> DeleteField(Guid contentTypeId, string name, CancellationToken cancellationToken = default)
    {
        // load the content type
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        // check the field exists
        var original = contentType.Fields.FirstOrDefault(f => f.Name == name) ??
            throw new AppException(ExceptionCodes.ContentTypeFieldNotFound);

        // remove the field
        contentType.Fields.Remove(original);

        //apply changes
        return await contentTypeRepository.Update(contentType, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
    }

    public async Task<ContentType> GetById(Guid id, CancellationToken cancellationToken)
    {
        return await contentTypeRepository.GetById(id, cancellationToken) ??
               throw new AppException(ExceptionCodes.ContentTypeNotFound);
    }
}
