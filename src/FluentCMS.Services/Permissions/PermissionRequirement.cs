using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Services.Permissions;

internal class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; private set; } = permission;
}
