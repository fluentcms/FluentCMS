namespace FluentCMS.Services;

public interface IFolderService : IAutoRegisterService
{
    Task<IEnumerable<Folder>> GetAll(CancellationToken cancellationToken = default);
    Task<Folder> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Folder> Create(Folder folder, CancellationToken cancellationToken = default);
    Task<Folder> Update(Folder folder, CancellationToken cancellationToken = default);
    Task<Folder> Delete(Guid id, CancellationToken cancellationToken = default);
}


public class FolderService(IFolderRepository folderRepository) : IFolderService
{
    public async Task<Folder> Create(Folder folder, CancellationToken cancellationToken = default)
    {
        return await folderRepository.Create(folder, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToCreate);
    }

    public async Task<Folder> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await folderRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToDelete);
    }

    public async Task<Folder> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await folderRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderNotFound);
    }

    public async Task<IEnumerable<Folder>> GetAll(CancellationToken cancellationToken = default)
    {
        return await folderRepository.GetAll(cancellationToken);
    }

    public async Task<Folder> Update(Folder folder, CancellationToken cancellationToken = default)
    {
        return await folderRepository.Update(folder, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToUpdate);
    }
}
