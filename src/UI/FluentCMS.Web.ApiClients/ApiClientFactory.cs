using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.ApiClients;

// factory pattern implementation to return IApiClient
public class ApiClientFactory(IServiceProvider serviceProvider)
{
    private T Get<T>() where T : IApiClient
    {
        return serviceProvider.GetRequiredService<T>();
    }

    public IPagesClient Pages => Get<IPagesClient>();
    public ISitesClient Sites => Get<ISitesClient>();
    // public IApiTokenClient ApiToken => Get<IApiTokenClient>();
    public IRolesClient Roles => Get<IRolesClient>();
    public IUsersClient Users => Get<IUsersClient>();
    // public ISettingsClient Settings => Get<ISettingsClient>();
    // public IPluginClient Plugin => Get<IPluginClient>();
    // public IPluginContentClient PluginContent => Get<IPluginContentClient>();
    // public IPluginDefinitionClient PluginDefinition => Get<IPluginDefinitionClient>();
    public ILayoutsClient Layouts => Get<ILayoutsClient>();
    public IAccountsClient Accounts => Get<IAccountsClient>();
    // public IContentsClient Content => Get<IContentsClient>();
    // public IContentTypeClient ContentType => Get<IContentTypeClient>();
    public IFoldersClient Folders => Get<IFoldersClient>();
    public IFilesClient Files => Get<IFilesClient>();
    // public IGlobalSettingsClient GlobalSettings => Get<IGlobalSettingsClient>();
    // public ISetupClient Setup => Get<ISetupClient>();
    // public IUserRolesClient UserRole => Get<IUserRolesClient>();
    public IBlocksClient Blocks => Get<IBlocksClient>();

}
