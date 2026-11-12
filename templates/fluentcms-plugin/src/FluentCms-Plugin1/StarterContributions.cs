using System.ComponentModel.DataAnnotations;
using FluentCMS.Authorization;
using FluentCMS.Settings;

namespace FluentCms_Plugin1;

// The permissions join the catalogue, so a role can be granted them; the plugin checks them with IPermissionChecker.
public sealed class StarterPermissions : IPermissionProvider
{
    public const string NotesManage = "starter-plugin.notes.manage";

    public IEnumerable<Permission> GetPermissions() => [new Permission(NotesManage, "Read and add the notes of a site.")];
}

// Stored per site, with instance defaults, and edited through /api/management/v1/settings/starter-plugin, which needs
// settings.starter-plugin.manage.
[Settings(StarterPlugin.Name)]
public sealed class StarterSettings
{
    [Range(1, 10000)]
    public int MaxNotes { get; set; } = 100;
}
