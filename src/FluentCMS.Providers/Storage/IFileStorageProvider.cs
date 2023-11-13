namespace FluentCMS.Providers.Storage;

public interface IFileStorageProvider
{
    Task<Stream> GetFileStream(string filePath);
    Task SaveFile(Stream inputStream, string filePath);
    Task DeleteFile(string filePath);
}
