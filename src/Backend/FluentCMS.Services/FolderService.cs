namespace FluentCMS.Services;

public interface IFolderService : IAutoRegisterService
{
    Task<Folder> Create(Folder folder, CancellationToken cancellationToken = default);
    Task<IEnumerable<Folder>> GetAll(CancellationToken cancellationToken = default);
    Task<Folder> Update(Folder folder, CancellationToken cancellationToken = default);
    Task<Folder> Delete(Guid id, CancellationToken cancellationToken = default);
}


public class FolderService(IFolderRepository folderRepository) : IFolderService
{
    public async Task<Folder> Create(Folder folder, CancellationToken cancellationToken = default)
    {
        // check if parent folder exists
        if (folder.FolderId != null)
        {
            _ = await folderRepository.GetById(folder.FolderId.Value, cancellationToken) ??
                throw new AppException(ExceptionCodes.FolderParentFolderNotFound);
        }

        return await folderRepository.Create(folder, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToCreate);
    }

    public async Task<IEnumerable<Folder>> GetAll(CancellationToken cancellationToken = default)
    {
        return await folderRepository.GetAll(cancellationToken);
    }

    public async Task<Folder> Update(Folder folder, CancellationToken cancellationToken = default)
    {
        // check if parent folder exists
        if (folder.FolderId != null)
        {
            _ = await folderRepository.GetById(folder.FolderId.Value, cancellationToken) ??
                throw new AppException(ExceptionCodes.FolderParentFolderNotFound);
        }

        return await folderRepository.Update(folder, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToUpdate);
    }

    public async Task<Folder> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await folderRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToDelete);
    }
}
