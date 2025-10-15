using FluentCMS.Infrastructure.Providers.Abstractions;

namespace FluentCMS.Api.Providers.FileStorage.Abstractions;

public interface IFileStorageProvider : IProvider
{
    public const string Area = "FileStorage";
    Task Upload(string fileName, Stream fileContent, CancellationToken cancellationToken = default);
    Task<Stream?> Download(string fileName, CancellationToken cancellationToken = default);
    Task Delete(string fileName, CancellationToken cancellationToken = default);
}
