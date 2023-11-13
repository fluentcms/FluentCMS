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

    public async Task SaveFile(Stream inputStream, string filePath)
    {
        var standardFilePath = Path.Combine(BasePath, filePath);
        standardFilePath = Path.GetFullPath(standardFilePath);

        var dir = Path.GetDirectoryName(standardFilePath)!;
        Directory.CreateDirectory(dir);

        using (var fileStream = File.OpenWrite(standardFilePath))
        {
            inputStream.Seek(0, SeekOrigin.Begin);
            await inputStream.CopyToAsync(fileStream);
        }
    }
}
