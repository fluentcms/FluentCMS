using FluentCMS.Providers.FileStorageProviders;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

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

        // Sanitize SVG content before persisting to remove potential XSS payloads
        if (IsSvgFile(file))
        {
            fileContent = SanitizeSvg(fileContent);
            file.Size = fileContent.Length;
        }

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

    private static bool IsSvgFile(File file)
    {
        return string.Equals(file.Extension, ".svg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(file.ContentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase);
    }

    // Dangerous SVG element local names
    private static readonly HashSet<string> _dangerousElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script",
        "foreignObject",
    };

    // URL-bearing attribute local names whose values must not use dangerous schemes
    private static readonly HashSet<string> _urlAttributeLocalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "href",
        "action",
        "src",
    };

    // Dangerous URI schemes (allowlist approach would be better but this covers the known vectors)
    private static readonly string[] _dangerousSchemes = ["javascript:", "vbscript:", "data:"];

    private static readonly XNamespace _xlinkNs = "http://www.w3.org/1999/xlink";

    private static System.IO.MemoryStream SanitizeSvg(System.IO.Stream svgStream)
    {
        XDocument doc;
        try
        {
            // Disable DTD processing to prevent XXE attacks
            var readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(svgStream, readerSettings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            // If the SVG cannot be parsed as XML return an empty SVG
            var empty = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
            return new System.IO.MemoryStream(empty);
        }

        // Remove dangerous elements (e.g. <script>, <foreignObject>)
        var elementsToRemove = doc.Descendants()
            .Where(e => _dangerousElements.Contains(e.Name.LocalName))
            .ToList();

        foreach (var element in elementsToRemove)
            element.Remove();

        // Remove event-handler attributes (on*) and URL attributes with dangerous schemes
        var attributesToRemove = doc.Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a =>
                a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                IsUrlAttributeWithDangerousScheme(a))
            .ToList();

        foreach (var attribute in attributesToRemove)
            attribute.Remove();

        var ms = new System.IO.MemoryStream();
        doc.Save(ms);
        ms.Position = 0;
        return ms;
    }

    private static bool IsUrlAttributeWithDangerousScheme(XAttribute attribute)
    {
        // Check both local href and xlink:href
        bool isUrlAttr = _urlAttributeLocalNames.Contains(attribute.Name.LocalName) ||
                         attribute.Name == _xlinkNs + "href";

        if (!isUrlAttr)
            return false;

        var value = attribute.Value.TrimStart();
        return _dangerousSchemes.Any(scheme => value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));
    }
}
