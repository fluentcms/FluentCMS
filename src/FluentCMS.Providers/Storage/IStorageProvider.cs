namespace FluentCMS.Providers.Storage;

public interface IStorageProvider
{
    Task SaveFile(Stream inputStream, string filePath);
}
