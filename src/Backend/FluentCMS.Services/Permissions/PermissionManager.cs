namespace FluentCMS.Services.Permissions;

public class PermissionManager<T>(IAuthContext authContext) : IPermissionManager<T>
{
    public async Task<bool> HasAccess(T data, string action)
    {
        switch (typeof(T).Name)
        {
            case nameof(Page): await CheckPageAccess(data as Page, action); break;

            default: break;
        }

        return true;
    }

    private async Task<bool> CheckPageAccess(Page page, string action)
    {
        return true;
        //
    }
}
