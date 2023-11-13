namespace FluentCMS.Providers.Storage;

public interface IFileStorageProvider
{
    Task SaveFile(Stream inputStream, string filePath);
}
