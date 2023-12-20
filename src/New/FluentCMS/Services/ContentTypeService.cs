using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IContentTypeService
{
    Task<IEnumerable<ContentType>> GetAll(string appSlug, CancellationToken cancellationToken = default);
    Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default);
    Task<ContentType> Update(ContentType contentType, CancellationToken cancellationToken = default);
    Task<ContentType> Delete(string appSlug, string contentTypeSlug, CancellationToken cancellationToken = default);
    Task<ContentType> AddField(string appSlug, string contentTypeSlug, ContentTypeField field, CancellationToken cancellationToken = default);
    Task<ContentType> UpdateField(string appSlug, string contentTypeSlug, ContentTypeField field, CancellationToken cancellationToken = default);
    Task<ContentType> RemoveField(string appSlug, string contentTypeSlug, string fieldSlug, CancellationToken cancellationToken = default);
}

public class ContentTypeService(IContentTypeRepository contentTypeRepository) : IContentTypeService
{
    public async Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default)
    {
        return await contentTypeRepository.Create(contentType, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToCreate);
    }

    public async Task<ContentType> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await contentTypeRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        //TODO: delete all contents for the type

        await contentTypeRepository.Delete(id, cancellationToken);
    }

    public async Task<ContentType> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await contentTypeRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);
    }

    public async Task<ContentType> Update(ContentType contentType, CancellationToken cancellationToken = default)
    {
        // name should be normalized
        contentType.Name = contentType.Name.ToLowerInvariant();

        // only allow name and description to be updated
        var original = await contentTypeRepository.GetById(contentType.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        if (original.Name != contentType.Name)
            throw new AppException(ExceptionCodes.ContentTypeNameCannotBeChanged);

        original.Title = contentType.Title;
        original.Description = contentType.Description;

        return await contentTypeRepository.Update(original, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
    }

    public async Task<ContentType> GetByName(string name, CancellationToken cancellationToken = default)
    {
        return await contentTypeRepository.GetByName(name.ToLowerInvariant(), cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);
    }

    public Task<IEnumerable<ContentType>> GetAll(CancellationToken cancellationToken = default)
    {
        return contentTypeRepository.GetAll(cancellationToken);
    }

    public async Task<ContentType> SetField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        // field name should be normalized
        field.Name = field.Name.ToLowerInvariant();

        // load the content type
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        // check the field exists
        var original = contentType.Fields.FirstOrDefault(f => f.Name == field.Name);

        if (original == null)
        {
            // add the field
            return await contentTypeRepository.AddField(contentTypeId, field, cancellationToken) ??
                throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
        }
        else
        {
            // TODO: convert all content field values
            // update the field
            return await contentTypeRepository.UpdateField(contentTypeId, field, cancellationToken) ??
                throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
        }

    }

    public async Task<ContentType> RemoveField(Guid contentTypeId, string fieldName, CancellationToken cancellationToken = default)
    {
        // field name should be normalized
        fieldName = fieldName.ToLowerInvariant();

        // load the content type
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        // check the field exists
        var original = contentType.Fields.FirstOrDefault(f => f.Name == fieldName) ??
            throw new AppException(ExceptionCodes.ContentTypeFieldNotFound);

        // TODO: delete all content field values

        // remove the field
        return await contentTypeRepository.RemoveField(contentTypeId, fieldName, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);
    }
}
