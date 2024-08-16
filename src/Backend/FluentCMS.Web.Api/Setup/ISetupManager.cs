namespace FluentCMS.Web.Api.Setup;

public interface ISetupManager
{
    Task<PageFullDetailResponse> GetSetupPage();
    Task<bool> IsInitialized();
    Task Reset();
    Task<bool> Start(SetupRequest request);
}
