using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.ApiClients;

// factory pattern implementation to return IApiClient
public class ApiClientFactory(IServiceProvider serviceProvider)
{
    private T Get<T>() where T : IApiClient
    {
        return serviceProvider.GetRequiredService<T>();
    }

    public IPageClient Page => Get<IPageClient>();
    public ISiteClient Site => Get<ISiteClient>();
    public IApiTokenClient ApiToken => Get<IApiTokenClient>();
    public IRoleClient Role => Get<IRoleClient>();
    public IUserClient User => Get<IUserClient>();
    public IPluginClient Plugin => Get<IPluginClient>();
    public IPluginContentClient PluginContent => Get<IPluginContentClient>();
    public IPluginDefinitionClient PluginDefinition => Get<IPluginDefinitionClient>();
    public ILayoutClient Layout => Get<ILayoutClient>();
    public IAccountClient Account => Get<IAccountClient>();
    public IContentClient Content => Get<IContentClient>();
    public IContentTypeClient ContentType => Get<IContentTypeClient>();
    public IFolderClient Folder => Get<IFolderClient>();
    public IFileClient File => Get<IFileClient>();
    public IGlobalSettingsClient GlobalSettings => Get<IGlobalSettingsClient>();
    public ISetupClient Setup => Get<ISetupClient>();
    public IUserRoleClient UserRole => Get<IUserRoleClient>();
    public IBlockClient Block => Get<IBlockClient>();

}
