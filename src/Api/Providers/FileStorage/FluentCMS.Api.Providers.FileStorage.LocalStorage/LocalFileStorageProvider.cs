using FluentCMS.Api.Providers.FileStorage.Abstractions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Api.Providers.FileStorage.LocalStorage;

public class LocalFileStorageProvider : IFileStorageProvider
{
    private readonly LocalFileStorageOptions _config;

    public LocalFileStorageProvider(IOptions<LocalFileStorageOptions> options)
    {
        _config = options.Value;

        // the files will be stored in the "files" directory
        // check the folder exists or not, if not create it
        if (!Directory.Exists(_config.Path))
            Directory.CreateDirectory(_config.Path);
    }

    public async Task Upload(string fileName, Stream fileContent, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_config.Path, fileName);
        using var fileStream = File.Create(filePath);
        await fileContent.CopyToAsync(fileStream, cancellationToken);
    }

    public Task Delete(string fileName, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_config.Path, fileName);
        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    public Task<Stream?> Download(string fileName, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_config.Path, fileName);
        if (File.Exists(filePath))
            return Task.FromResult<Stream?>(File.OpenRead(filePath));
        else
            return Task.FromResult<Stream?>(null);
    }
}
