using Microsoft.AspNetCore.Hosting;

namespace FluentCMS.Services;

public interface IFileProvider
{
    public Task<FileEntity> Create(FileEntity file, Stream stream, CancellationToken cancellationToken = default);
    public Task<IEnumerable<FileEntity>> GetAll(CancellationToken cancellationToken = default);
    public Task<FileEntity> GetById(Guid fileId, CancellationToken cancellationToken = default);
    public Task<Stream> GetStream(Guid fileId, CancellationToken cancellationToken = default);
    public Task<FileEntity> Delete(Guid fileId, CancellationToken cancellationToken = default);
}

public class LocalFileSystemProvider(IWebHostEnvironment environment, IFileRepository repository) : IFileProvider
{
    private const string PATH = "/uploads";

    private string AbsolutePath => Path.Join([environment.ContentRootPath, PATH]);

    private string BuildFilePath(FileEntity file) => Path.Join([AbsolutePath, file.Id.ToString("D")]);

    public async Task<FileEntity> Create(FileEntity file, Stream stream, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(AbsolutePath))
        {
            Directory.CreateDirectory(AbsolutePath);
        }
        var createdFile = await repository.Create(file, cancellationToken);
        if (createdFile is null) throw new AppException(ExceptionCodes.FileUnableToCreate);
        await using var fileStream = File.Create(BuildFilePath(createdFile));
        try
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
            await fileStream.FlushAsync(cancellationToken);
            return createdFile;
        }
        finally
        {
            fileStream.Close();
        }
    }

    public async Task<IEnumerable<FileEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        return await repository.GetAll(cancellationToken);
    }

    public async Task<FileEntity> GetById(Guid fileId, CancellationToken cancellationToken = default)
    {
        return await repository.GetById(fileId, cancellationToken) ??
               throw new AppException(ExceptionCodes.FileNotFound);
    }

    public async Task<Stream> GetStream(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await EnsureFileExists(fileId, cancellationToken);
        var fileStream = File.Open(BuildFilePath(file), FileMode.Open);
        return fileStream;
    }

    private async Task<FileEntity> EnsureFileExists(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await repository.GetById(fileId, cancellationToken);
        if (file is null) throw new AppException(ExceptionCodes.FileNotFound);

        return file;
    }

    public async Task<FileEntity> Delete(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await EnsureFileExists(fileId, cancellationToken);

        return await repository.Delete(file.Id, cancellationToken)
               ?? throw new AppException(ExceptionCodes.FileUnableToDelete);
    }
}
