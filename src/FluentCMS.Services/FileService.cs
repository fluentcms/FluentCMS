using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.VisualBasic;
using File = FluentCMS.Entities.File;

namespace FluentCMS.Services;

public interface IFileService : IAutoRegisterService
{
    public Task<File?> Create(string? slug, IFormFile formFile, CancellationToken cancellationToken = default);
    public Task<IEnumerable<File?>> GetAll(CancellationToken cancellationToken = default);
    public Task<File?> GetById(Guid id, CancellationToken cancellationToken = default);
    public Task<File?> GetBySlug(string slug, CancellationToken cancellationToken = default);
    public Task<File?> DeleteById(Guid id, CancellationToken cancellationToken = default);
    public Task<File?> DeleteBySlug(string slug, CancellationToken cancellationToken = default);
}
public class FileService : IFileService
{
    private readonly IFileRepository _fileRepository;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private const string _uploadsFolderName = "Uploads";

    private string UploadPath => Path.Join(_webHostEnvironment.ContentRootPath, _uploadsFolderName);
    public FileService(IFileRepository fileRepository, IWebHostEnvironment webHostEnvironment)
    {
        _fileRepository = fileRepository;
        _webHostEnvironment = webHostEnvironment;

        // if UploadPath does not exist create it
        if (!Directory.Exists(UploadPath))
        {
            Directory.CreateDirectory(UploadPath);
        }
    }

    public async Task<File?> Create(string? slug, IFormFile formFile, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid();
        var localFilePath = Path.Join(UploadPath, fileId.ToString("D"));
        slug ??= formFile.FileName.Slugify();
        await GuardAgainstDuplicateSlug(slug, cancellationToken);
        var sourceStream = formFile.OpenReadStream();
        var destinationStream = System.IO.File.OpenWrite(localFilePath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        var fileModel = new File()
        {
            Id = fileId,
            LocalPath = localFilePath,
            Name = formFile.FileName,
            Extension = Path.GetExtension(formFile.FileName).ToLower(),
            MimeType = formFile.ContentType,
            Size = sourceStream.Length,
            Slug = slug
        };
        await _fileRepository.Create(fileModel, cancellationToken);
        await destinationStream.FlushAsync(cancellationToken);
        destinationStream.Close();
        return fileModel;
    }

    private async Task GuardAgainstDuplicateSlug(string slug, CancellationToken cancellationToken)
    {
        if ((await _fileRepository.GetBySlug(slug, cancellationToken)) is not null)
        {
            throw new AppException(ExceptionCodes.FileDuplicateSlug);
        }
    }

    public Task<IEnumerable<File?>> GetAll(CancellationToken cancellationToken = default)
    {
        return _fileRepository.GetAll(cancellationToken)!;
    }

    public Task<File?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return _fileRepository.GetById(id, cancellationToken);
    }

    public Task<File?> GetBySlug(string slug, CancellationToken cancellationToken = default)
    {
        return _fileRepository.GetBySlug(slug, cancellationToken);
    }

    public async Task<File?> DeleteById(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await _fileRepository.Delete(id, cancellationToken);
        DeleteFromFileSystem(file);
        return file;
    }

    private static void DeleteFromFileSystem(File? file)
    {
        if (file != null && System.IO.File.Exists(file.LocalPath))
        {
            System.IO.File.Delete(file.LocalPath);
        }
    }

    public async Task<File?> DeleteBySlug(string slug, CancellationToken cancellationToken = default)
    {
        var file = await _fileRepository.Delete(slug, cancellationToken);
        DeleteFromFileSystem(file);
        return file;
    }
}
