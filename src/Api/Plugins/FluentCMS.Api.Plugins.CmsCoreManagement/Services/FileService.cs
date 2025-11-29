namespace FluentCMS.Api.Plugins.CmsCoreManagement.Services;

public interface IFileService 
{
    Task<List<FluentCMS.Api.Core.Models.File>> GetByFolderId(Guid folderId, CancellationToken cancellationToken = default);

    Task<Core.Models.File> Create(Core.Models.File file, System.IO.Stream fileContent, CancellationToken cancellationToken = default);
//     Task<Core.Models.File> GetById(Guid id, CancellationToken cancellationToken = default);
    // Task<Core.Models.File> GetByName(Guid folderId, string fileName, CancellationToken cancellationToken = default);
    Task<Core.Models.File> Rename(Guid id, string name, CancellationToken cancellationToken = default);
    Task<Core.Models.File> Remove(Guid id, CancellationToken cancellationToken = default);
    Task<Core.Models.File> Move(Guid id, Guid folderId, CancellationToken cancellationToken = default);
//     Task<string> GetFilePath(File file, CancellationToken cancellationToken = default);
//     Task<System.IO.Stream> GetStream(Guid id, CancellationToken cancellationToken = default);
//     Task<IEnumerable<File>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
}

internal class FileService(IFileRepository fileRepository, IFolderRepository folderRepository, IFolderService folderService) : IFileService
{
//     // TODO: use IFileStorageProvider fileStorageProvider. currently files will not be saved.
    public async Task<Core.Models.File> Create(Core.Models.File file, System.IO.Stream fileContent, CancellationToken cancellationToken = default)
    {
        var folder = await folderRepository.GetById(file.FolderId, cancellationToken) ??
            throw new EnhancedException(MessageCodes.FolderNotFound);

        // if (folder.SiteId != file.SiteId)
        //     throw new EnhancedException(MessageCodes.FolderNotFound);

        file.NormalizedName = GetNormalizedFileName(file.Name);

        // check if file with the same name already exists
        var existingFile = await fileRepository.GetByName(folder.Id, file.NormalizedName, cancellationToken);
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
                existingFile = await fileRepository.GetByName(folder.Id, file.NormalizedName, cancellationToken);
                suffix++;
            } while (existingFile != null);
        }

        await fileRepository.Add(file, cancellationToken);

        var uploadsFolder = "files";
        Directory.CreateDirectory(uploadsFolder); // ensure folder exists

        var extension = Path.GetExtension(file.Name);
        var filePath = Path.Combine(uploadsFolder, file.Id + extension);

        using var fileStream = System.IO.File.Create(filePath);
        await fileContent.CopyToAsync(fileStream, cancellationToken);

        return file;
    }

    public async Task<Core.Models.File> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        return await fileRepository.Remove(id, cancellationToken) ??
            throw new EnhancedException(MessageCodes.FileUnableToDelete);
    }

//     public async Task<IEnumerable<File>> GetAll(Guid siteId, CancellationToken cancellationToken = default)
//     {
//         return await fileRepository.GetAllForSite(siteId, cancellationToken);
//     }

    public async Task<List<FluentCMS.Api.Core.Models.File>> GetByFolderId(Guid folderId, CancellationToken cancellationToken)
    {
        return await fileRepository.GetByFolderId(folderId, cancellationToken);
    }

//     public async Task<Core.Models.File> GetById(Guid id, CancellationToken cancellationToken = default)
//     {
//         var file = await fileRepository.GetById(id, cancellationToken) ??
//             throw new AppException(ExceptionCodes.FileNotFound);

//         return file;
//     }

//     public async Task<Core.Models.File> GetByName(Guid folderId, string fileName, CancellationToken cancellationToken = default)
//     {
//         var folder = await folderRepository.GetById(folderId, cancellationToken) ??
//             throw new AppException(ExceptionCodes.FolderNotFound);

//         var normalizedFileName = GetNormalizedFileName(fileName);

//         return await fileRepository.GetByName(folder.SiteId, folder.Id, normalizedFileName, cancellationToken) ??
//             throw new AppException(ExceptionCodes.FileNotFound);
//     }

//     public async Task<string> GetFilePath(File file, CancellationToken cancellationToken = default)
//     {
//         var folders = await folderService.GetParentFolders(file.FolderId, cancellationToken) ??
//             throw new AppException(ExceptionCodes.FolderNotFound);

//         return string.Join("/", folders.Select(x => x.Name)) + "/" + file.Name;
//     }

//     public async Task<System.IO.Stream> GetStream(Guid id, CancellationToken cancellationToken = default)
//     {
//         return await fileStorageProvider.Download(id.ToString(), cancellationToken) ??
//             throw new AppException(ExceptionCodes.FileNotFound);
//     }

    public async Task<Core.Models.File> Rename(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var normalizedFileName = GetNormalizedFileName(name);

        var file = await fileRepository.GetById(id, cancellationToken) ??
            throw new EnhancedException(MessageCodes.FileNotFound);

        if (!IsValidFileName(normalizedFileName))
            throw new EnhancedException(MessageCodes.FileInvalidName);

        // check if file with the same name already exists
        var existingFile = await fileRepository.GetByName(file.FolderId, normalizedFileName, cancellationToken);
        if (existingFile != null)
            throw new EnhancedException(MessageCodes.FileAlreadyExists);

        file.Name = name;
        file.NormalizedName = normalizedFileName;

        return await fileRepository.Update(file, cancellationToken) ??
            throw new EnhancedException(MessageCodes.FileUnableToUpdate);
    }

    public async Task<Core.Models.File> Move(Guid id, Guid folderId, CancellationToken cancellationToken = default)
    {
        var folder = await folderRepository.GetById(folderId, cancellationToken) ??
            throw new EnhancedException(MessageCodes.FolderNotFound);

        var file = await fileRepository.GetById(id, cancellationToken) ??
            throw new EnhancedException(MessageCodes.FileNotFound);

        if (file.SiteId != folder.SiteId)
            throw new EnhancedException(MessageCodes.FolderNotFound);

        // check if file with the same name already exists
        var exisitingFile = await fileRepository.GetByName(folder.Id, file.NormalizedName, cancellationToken);
        if (exisitingFile != null)
            throw new EnhancedException(MessageCodes.FileAlreadyExists);

        file.FolderId = folderId;

        return await fileRepository.Update(file, cancellationToken) ??
            throw new EnhancedException(MessageCodes.FileUnableToUpdate);
    }

    private static readonly Regex _fileNameRegex = new(@"[a-zA-Z0-9_\-\.\(\)\s]+(\.[a-zA-Z0-9]{2,6})?");

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
