namespace FluentCMS.Services.Pluginses;

public interface IFileStoragePlugin : IPlugin
{
    Task SaveFile(Stream inputStream, string filePath);
}
