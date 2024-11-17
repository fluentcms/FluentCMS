using System.Text.RegularExpressions;
using FluentCMS.Providers.FileStorageProviders;

namespace FluentCMS.Services;

public interface IFileService : IAutoRegisterService
{
    Task<File> Create(File file, System.IO.Stream fileContent, CancellationToken cancellationToken = default);
    Task<File> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<File> GetByName(Guid folderId, string fileName, CancellationToken cancellationToken = default);
    Task<File> Rename(Guid id, string name, CancellationToken cancellationToken = default);
    Task<File> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<File> Move(Guid id, Guid folderId, CancellationToken cancellationToken = default);
    Task<string> GetFilePath(File file, CancellationToken cancellationToken = default);
    Task<System.IO.Stream> GetStream(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<File>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
}

public class FileService(IFileRepository fileRepository, IFolderRepository folderRepository, IFolderService folderService, IFileStorageProvider fileStorageProvider) : IFileService
{
    public async Task<File> Create(File file, System.IO.Stream fileContent, CancellationToken cancellationToken = default)
    {
        var folder = await folderRepository.GetById(file.FolderId, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderNotFound);

        if (folder.SiteId != file.SiteId)
            throw new AppException(ExceptionCodes.FolderNotFound);

        file.NormalizedName = GetNormalizedFileName(file.Name);

        // check if file with the same name already exists
        var existingFile = await fileRepository.GetByName(folder.SiteId, folder.Id, file.NormalizedName, cancellationToken);
        if (existingFile != null)
        {
            // add a suffix to the new file's name to avoid conflicts with preiously uploaded files
            var fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(file.Name);
            var fileExtension = System.IO.Path.GetExtension(file.Name);
            var suffix = 1;
            do
            {
                file.Name = $"{fileNameWithoutExtension} ({suffix}){fileExtension}";
                file.NormalizedName = GetNormalizedFileName(file.Name);
                existingFile = await fileRepository.GetByName(folder.SiteId, folder.Id, file.NormalizedName, cancellationToken);
                suffix++;
            } while (existingFile != null);
        }

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

    public async Task<File> GetByName(Guid folderId, string fileName, CancellationToken cancellationToken = default)
    {
        var folder = await folderRepository.GetById(folderId, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderNotFound);

        var normalizedFileName = GetNormalizedFileName(fileName);

        return await fileRepository.GetByName(folder.SiteId, folder.Id, normalizedFileName, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileNotFound);
    }

    public async Task<string> GetFilePath(File file, CancellationToken cancellationToken = default)
    {
        var folders = await folderService.GetParentFolders(file.FolderId, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderNotFound);

        return string.Join("/", folders.Select(x => x.Name)) + "/" + file.Name;
    }

    public async Task<System.IO.Stream> GetStream(Guid id, CancellationToken cancellationToken = default)
    {
        return await fileStorageProvider.Download(id.ToString(), cancellationToken) ??
            throw new AppException(ExceptionCodes.FileNotFound);
    }

    public async Task<File> Rename(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var normalizedFileName = GetNormalizedFileName(name);

        var file = await fileRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileNotFound);

        if (!IsValidFileName(normalizedFileName))
            throw new AppException(ExceptionCodes.FileInvalidName);

        // check if file with the same name already exists
        var existingFile = await fileRepository.GetByName(file.SiteId, file.FolderId, normalizedFileName, cancellationToken);
        if (existingFile != null)
            throw new AppException(ExceptionCodes.FileAlreadyExists);

        file.Name = name;
        file.NormalizedName = normalizedFileName;

        return await fileRepository.Update(file, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileUnableToUpdate);
    }

    public async Task<File> Move(Guid id, Guid folderId, CancellationToken cancellationToken = default)
    {
        var folder = await folderRepository.GetById(folderId, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderNotFound);

        var file = await fileRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileNotFound);

        if (file.SiteId != folder.SiteId)
            throw new AppException(ExceptionCodes.FolderNotFound);

        // check if file with the same name already exists
        var exisitingFile = await fileRepository.GetByName(folder.SiteId, folder.Id, file.NormalizedName, cancellationToken);
        if (exisitingFile != null)
            throw new AppException(ExceptionCodes.FileAlreadyExists);

        file.FolderId = folderId;

        return await fileRepository.Update(file, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileUnableToUpdate);
    }

    private static readonly Regex _fileNameRegex = new(@"^[\w\-]+(\.[A-Za-z]{2,6})?$");

    private static bool IsValidFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false; // Folder name should not be empty or whitespace

        return _fileNameRegex.IsMatch(fileName);
    }

    private static string GetNormalizedFileName(string fileName)
    {
        var normalized = fileName.Trim().ToLower();
        return normalized;
    }
}
