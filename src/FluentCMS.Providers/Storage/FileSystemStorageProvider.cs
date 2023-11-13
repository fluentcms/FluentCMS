using Microsoft.Extensions.Configuration;

namespace FluentCMS.Providers.Storage;

internal class FileSystemStorageProvider : IFileStorageProvider
{
    public string BasePath { get; set; }

    public FileSystemStorageProvider(IConfiguration configuration)
    {
        var options = configuration.GetSection("FileSystemStorage").Get<FileSystemStorageProviderOptions>();
        BasePath = (string.IsNullOrWhiteSpace(options?.BasePath) ? null : options?.BasePath)
            ?? Path.Combine(Path.GetTempPath(), "fluentcms-uploads");
    }

    public Task<Stream> GetFileStream(string filePath)
    {
        var physicalFilePath = GetPhysicalFilePath(filePath);
        if (File.Exists(physicalFilePath) == false)
            throw new FileNotFoundException(filePath);

        var fileStream = File.OpenRead(physicalFilePath);
        return Task.FromResult(fileStream as Stream);
    }

    public async Task SaveFile(Stream inputStream, string filePath)
    {
        var physicalFilePath = GetPhysicalFilePath(filePath);
        var dir = Path.GetDirectoryName(physicalFilePath)!;
        Directory.CreateDirectory(dir);

        using (var fileStream = File.OpenWrite(physicalFilePath))
        {
            inputStream.Seek(0, SeekOrigin.Begin);
            await inputStream.CopyToAsync(fileStream);
        }
    }

    public Task DeleteFile(string filePath)
    {
        var physicalFilePath = GetPhysicalFilePath(filePath);

        File.Delete(physicalFilePath);
        return Task.CompletedTask;
    }

    private string GetPhysicalFilePath(string filePath)
    {
        var physicalFilePath = Path.Combine(BasePath, filePath);
        physicalFilePath = Path.GetFullPath(physicalFilePath);
        return physicalFilePath;
    }
}
