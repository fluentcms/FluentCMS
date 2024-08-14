namespace FluentCMS.Web.Api;

public interface IConfigManager
{
    public ApiServerConfig GetApiServerConfig();
    public void UpdateApiServerConfig(ApiServerConfig apiServerConfig);
    public bool IsConfigured();
}
