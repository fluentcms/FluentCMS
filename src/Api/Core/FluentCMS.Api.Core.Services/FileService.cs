//using File = FluentCMS.Api.Core.Models.File;

//namespace FluentCMS.Api.Core.Services;

//public interface IFileService
//{
//    Task<File> Add(File file, Stream fileContent, CancellationToken cancellationToken = default);
//    Task<File> Rename(Guid id, string name, CancellationToken cancellationToken = default);
//    Task<File> Remove(Guid id, CancellationToken cancellationToken = default);
//    Task<File> Move(Guid id, Guid folderId, CancellationToken cancellationToken = default);
//    Task<string> GetFilePath(File file, CancellationToken cancellationToken = default);
//    Task<Stream> GetStream(Guid id, CancellationToken cancellationToken = default);
//    Task<IEnumerable<File>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
//    Task<File> GetById(Guid id, CancellationToken cancellationToken = default);
//    Task<File> GetByName(Guid folderId, string fileName, CancellationToken cancellationToken = default);
//}

//internal class FileService(IFileRepository fileRepository, IFolderRepository folderRepository, IFolderService folderService, IFileStorageProvider fileStorageProvider, IEventPublisher eventPublisher) : IFileService
//{
//    public async Task<File> Add(File file, Stream fileContent, CancellationToken cancellationToken = default)
//    {
//        var folder = await folderRepository.GetById(file.FolderId, cancellationToken);

//        file.NormalizedName = GetNormalizedFileName(file.Name);

//        // check if file with the same name already exists
//        var existingFile = await fileRepository.GetByName(folder.SiteId, folder.Id, file.NormalizedName, cancellationToken);
//        if (existingFile != null)
//        {
//            // add a suffix to the new file's name to avoid conflicts with previously uploaded files
//            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);
//            var fileExtension = Path.GetExtension(file.Name);
//            var suffix = 1;
//            do
//            {
//                file.Name = $"{fileNameWithoutExtension} ({suffix}){fileExtension}";
//                file.NormalizedName = GetNormalizedFileName(file.Name);
//                existingFile = await fileRepository.GetByName(folder.SiteId, folder.Id, file.NormalizedName, cancellationToken);
//                suffix++;
//            } while (existingFile != null);
//        }

//        await fileRepository.Add(file, cancellationToken);

//        await fileStorageProvider.Upload(file.Id.ToString(), fileContent, cancellationToken);

//        await eventPublisher.Publish(new FileAddedEvent(file), cancellationToken);

//        return file;
//    }

//    public async Task<File> Remove(Guid id, CancellationToken cancellationToken = default)
//    {
//        var file = await fileRepository.Remove(id, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FileFailedToRemove);

//        await fileStorageProvider.Delete(id.ToString(), cancellationToken);

//        await eventPublisher.Publish(new FileRemovedEvent(file), cancellationToken);

//        return file;
//    }

//    public async Task<File> Rename(Guid id, string name, CancellationToken cancellationToken = default)
//    {
//        var normalizedFileName = GetNormalizedFileName(name);

//        var file = await fileRepository.GetById(id, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FileNotFound);

//        if (!IsValidFileName(normalizedFileName))
//            throw new EnhancedException(ExceptionCodes.FileInvalidName);

//        // check if file with the same name already exists
//        var existingFile = await fileRepository.GetByName(file.SiteId, file.FolderId, normalizedFileName, cancellationToken);
//        if (existingFile != null)
//            throw new EnhancedException(ExceptionCodes.FileAlreadyExists);

//        file.Name = name;
//        file.NormalizedName = normalizedFileName;

//        var result = await fileRepository.Update(file, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FileFailedToUpdate);

//        await eventPublisher.Publish(new FileRenamedEvent(result), cancellationToken);

//        return result;
//    }

//    public async Task<File> Move(Guid id, Guid folderId, CancellationToken cancellationToken = default)
//    {
//        var folder = await folderRepository.GetById(folderId, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FolderNotFound);

//        var file = await fileRepository.GetById(id, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FileNotFound);

//        if (file.SiteId != folder.SiteId)
//            throw new EnhancedException(ExceptionCodes.FolderNotFound);

//        // check if file with the same name already exists
//        var exisitingFile = await fileRepository.GetByName(folder.SiteId, folder.Id, file.NormalizedName, cancellationToken);
//        if (exisitingFile != null)
//            throw new EnhancedException(ExceptionCodes.FileAlreadyExists);

//        file.FolderId = folderId;

//        var result = await fileRepository.Update(file, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FileFailedToUpdate);

//        await eventPublisher.Publish(new FileMovedEvent(result), cancellationToken);

//        return result;
//    }

//    public async Task<IEnumerable<File>> GetAll(Guid siteId, CancellationToken cancellationToken = default)
//    {
//        return await fileRepository.GetAllForSite(siteId, cancellationToken);
//    }

//    public async Task<File> GetById(Guid id, CancellationToken cancellationToken = default)
//    {
//        var file = await fileRepository.GetById(id, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FileNotFound);

//        return file;
//    }

//    public async Task<File> GetByName(Guid folderId, string fileName, CancellationToken cancellationToken = default)
//    {
//        var folder = await folderRepository.GetById(folderId, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FolderNotFound);

//        var normalizedFileName = GetNormalizedFileName(fileName);

//        return await fileRepository.GetByName(folder.SiteId, folder.Id, normalizedFileName, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FileNotFound);
//    }

//    public async Task<string> GetFilePath(File file, CancellationToken cancellationToken = default)
//    {
//        var folders = await folderService.GetParentFolders(file.FolderId, cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FolderNotFound);

//        return string.Join("/", folders.Select(x => x.Name)) + "/" + file.Name;
//    }

//    public async Task<Stream> GetStream(Guid id, CancellationToken cancellationToken = default)
//    {
//        return await fileStorageProvider.Download(id.ToString(), cancellationToken) ??
//            throw new EnhancedException(ExceptionCodes.FileNotFound);
//    }

//    private static readonly Regex _fileNameRegex = new(@"[a-zA-Z0-9_\-\.\(\)\s]+(\.[a-zA-Z0-9]{2,6})?");

//    private static bool IsValidFileName(string fileName)
//    {
//        if (string.IsNullOrWhiteSpace(fileName))
//            return false; // Folder name should not be empty or whitespace

//        return _fileNameRegex.IsMatch(fileName);
//    }

//    private static string GetNormalizedFileName(string fileName)
//    {
//        var normalized = fileName.Trim().ToLower();
//        return normalized;
//    }
//}
