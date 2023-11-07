using Microsoft.Extensions.Configuration;

namespace FluentCMS.Services.Pluginses;

public class FileSystemStoragePlugin : IFileStoragePlugin
{
    public string Name => "File System Storage";
    public string Description => "Stores files in local file system";
    public string BasePath { get; set; }

    public FileSystemStoragePlugin(IConfiguration configuration)
    {
        var options = configuration.GetSection("FileSystemStorage").Get<FileSystemStoragePluginOptions>();
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
