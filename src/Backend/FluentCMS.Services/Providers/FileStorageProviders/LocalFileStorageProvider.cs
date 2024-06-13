namespace FluentCMS.Providers;

public class LocalFileStorageProvider : IFileStorageProvider
{
    public const string UPLOAD_FOLDER = "files";

    public LocalFileStorageProvider()
    {
        // the files will be stored in the "files" directory
        // check the folder exists or not, if not create it
        if (!Directory.Exists(UPLOAD_FOLDER))
            Directory.CreateDirectory(UPLOAD_FOLDER);
    }

    public async Task Upload(string fileName, Stream fileContent, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(UPLOAD_FOLDER, fileName);
        using var fileStream = File.Create(filePath);
        await fileContent.CopyToAsync(fileStream, cancellationToken);
    }

    public Task Delete(string fileName, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(UPLOAD_FOLDER, fileName);
        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    public Task<Stream?> Download(string fileName, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(UPLOAD_FOLDER, fileName);
        if (File.Exists(filePath))
            return Task.FromResult<Stream?>(File.OpenRead(filePath));
        else
            return Task.FromResult<Stream?>(null);
    }
}
