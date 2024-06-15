using System.IO;

namespace FluentCMS.Providers;

public interface IFileStorageProvider
{
    Task Upload(string fileName, Stream fileContent, CancellationToken cancellationToken = default);
    Task<Stream?> Download(string fileName, CancellationToken cancellationToken = default);
    Task Delete(string fileName, CancellationToken cancellationToken = default);
}
