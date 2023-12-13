using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IContentTypeService
{
    Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default);
    Task<IEnumerable<ContentType>> GetAll(CancellationToken cancellationToken = default);
    Task<ContentType> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<ContentType> GetByName(string name, CancellationToken cancellationToken = default);
    Task<ContentType> Update(ContentType contentType, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
}

public class ContentTypeService(IContentTypeRepository contentTypeRepository) : IContentTypeService
{
    public async Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default)
    {
        // name should be normalized
        contentType.Name = contentType.Name.ToLowerInvariant();

        // check type name to be unique
        _ = await contentTypeRepository.GetByName(contentType.Name, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNameMustBeUnique);

        return await contentTypeRepository.Create(contentType, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToCreate);
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
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
}
