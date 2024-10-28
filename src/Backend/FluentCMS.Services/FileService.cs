using FluentCMS.Providers.FileStorageProviders;

namespace FluentCMS.Services;

public interface IFileService : IAutoRegisterService
{
    Task<File> Create(File file, System.IO.Stream fileContent, CancellationToken cancellationToken = default);
    Task<File> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<File> GetByName(Guid folderId, string fileName, CancellationToken cancellationToken = default);
    Task<File> Update(File file, CancellationToken cancellationToken = default);
    Task<File> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<System.IO.Stream> GetStream(Guid id, CancellationToken cancellationToken = default);
    //Task<System.IO.Stream> GetStream(string fileUrl, CancellationToken cancellationToken = default);
    Task<IEnumerable<File>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
}

public class FileService(IFileRepository fileRepository, IFolderRepository folderRepository, IFileStorageProvider fileStorageProvider) : IFileService
{
    public async Task<File> Create(File file, System.IO.Stream fileContent, CancellationToken cancellationToken = default)
    {
        var folder = await folderRepository.GetById(file.FolderId, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderNotFound);

        if (folder.SiteId != file.SiteId)
            throw new AppException(ExceptionCodes.FolderNotFound);

        await fileRepository.Create(file, cancellationToken);

        await fileStorageProvider.Upload(file.Id.ToString(), fileContent, cancellationToken);

        return file;
    }

    public async Task<File> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await fileRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileUnableToDelete);
    }

    public async Task<IEnumerable<File>> GetAll(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await fileRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<File> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileNotFound);

        return file;
    }

    public async Task<System.IO.Stream> GetStream(Guid id, CancellationToken cancellationToken = default)
    {
        return await fileStorageProvider.Download(id.ToString(), cancellationToken) ??
            throw new AppException(ExceptionCodes.FileNotFound);
    }

    //public async Task<System.IO.Stream> GetStream(string fileUrl, CancellationToken cancellationToken = default)
    //{

    //}

    public async Task<File> Update(File file, CancellationToken cancellationToken = default)
    {
        // check if parent folder exists
        if (file.FolderId != null)
        {
            _ = await folderRepository.GetById(file.FolderId, cancellationToken) ??
                throw new AppException(ExceptionCodes.FolderNotFound);
        }

        return await fileRepository.Update(file, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileUnableToUpdate);

    }
}
