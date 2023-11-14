using FluentCMS.Providers.Storage;

namespace FluentCMS.Tests.Helpers;
public class FakeFileStorageProvider : IFileStorageProvider
{
    public Task DeleteFile(string filePath)
    {
        return Task.CompletedTask;
    }

    public Task<Stream> GetFileStream(string filePath)
    {
        var mem = new MemoryStream();
        return Task.FromResult(mem as Stream);
    }

    public Task SaveFile(Stream inputStream, string filePath)
    {
        return Task.CompletedTask;
    }
}
