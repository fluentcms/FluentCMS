namespace FluentCMS.Services.Permissions;

public interface IPermissionManager<TData>
{
    Task<bool> HasAccess(TData data, string action);
}
