using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using File = FluentCMS.Entities.File;

namespace FluentCMS.Services;

public interface IFileService : IAutoRegisterService
{
    public Task<File> Create(IFormFile formFile, CancellationToken cancellationToken = default);
    public Task<IEnumerable<File>> GetAll(CancellationToken cancellationToken = default);
    public Task<File?> GetById(Guid id, CancellationToken cancellationToken = default);
    public Task<File?> DeleteById(Guid id, CancellationToken cancellationToken = default);
    string GetFilePath(Guid fileId);
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

    public async Task<File> Create(IFormFile formFile, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid();
        var localFilePath = GetFilePath(fileId);
        var sourceStream = formFile.OpenReadStream();
        var destinationStream = System.IO.File.OpenWrite(localFilePath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        var fileModel = new File()
        {
            Id = fileId,
            Name = formFile.FileName,
            Extension = Path.GetExtension(formFile.FileName).ToLower(),
            MimeType = formFile.ContentType,
            Size = sourceStream.Length,
        };
        await _fileRepository.Create(fileModel, cancellationToken);
        await destinationStream.FlushAsync(cancellationToken);
        destinationStream.Close();
        return fileModel;
    }

    public string GetFilePath(Guid fileId)
    {
        return Path.Join(UploadPath, fileId.ToString("D"));
    }

    public Task<IEnumerable<File>> GetAll(CancellationToken cancellationToken = default)
    {
        return _fileRepository.GetAll(cancellationToken)!;
    }

    public Task<File?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return _fileRepository.GetById(id, cancellationToken);
    }

    public async Task<File?> DeleteById(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await _fileRepository.Delete(id, cancellationToken);
        DeleteFromFileSystem(file);
        return file;
    }

    private void DeleteFromFileSystem(File? file)
    {
        if (file != null && GetFilePath(file.Id) is var filePath && System.IO.File.Exists(filePath))
        {
            System.IO.File.Delete(filePath);
        }
    }

}
