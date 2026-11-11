using System.Collections.Frozen;
using System.Globalization;
using FluentCMS.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Schema;

namespace FluentCMS.Core.Authorization;

// Every permission a role can be granted: the core ones, one per content type and operation, and the ones that
// IPermissionProvider implementations declare. Built once from the schema the host starts with.
public sealed class PermissionCatalogue
{
    private const string CoreOwner = "FluentCMS";

    private static readonly (string Name, string Description)[] CoreEntries =
    [
        (CorePermissions.UsersManage, "Invite, edit and deactivate users."),
        (CorePermissions.RolesManage, "Create and edit roles and their grants."),
        (CorePermissions.ApiKeysManage, "Create and revoke API keys."),
        (CorePermissions.SitesManage, "Create and edit sites and their locales."),
        (CorePermissions.JobsManage, "See, retry and cancel background jobs, and retry or remove failed event deliveries."),
        (CorePermissions.ActivityRead, "Read the activity log."),
        (CorePermissions.SchemaRead, "Read the content schema."),
        (CorePermissions.PluginsManage, "Enable and disable plugins per site."),
        (CorePermissions.TransferManage, "Export and import content."),
        (CorePermissions.SystemRead, "Read the startup report and the detailed health of the instance."),
        (CorePermissions.MembersManage, "Create, edit, deactivate and delete members, and manage member groups."),
    ];

    private static readonly (ContentOperation Operation, string Description)[] ContentOperations =
    [
        (ContentOperation.Create, "Create {0} entries."),
        (ContentOperation.Read, "Read {0} entries."),
        (ContentOperation.Update, "Edit {0} entries."),
        (ContentOperation.Delete, "Move {0} entries to the trash."),
        (ContentOperation.Publish, "Publish {0} entries."),
        (ContentOperation.Unpublish, "Unpublish {0} entries."),
        (ContentOperation.Restore, "Restore {0} entries from the trash."),
        (ContentOperation.Purge, "Delete {0} entries for good."),
    ];

    private readonly FrozenDictionary<string, PermissionEntry> _byName;

    // settingsAreas are the areas of the settings classes of the host, each of which has its own settings.{area}.manage.
    public PermissionCatalogue(SchemaModel schema, IEnumerable<IPermissionProvider> providers, IEnumerable<string>? settingsAreas = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(providers);

        var entries = new List<PermissionEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, description) in CoreEntries)
        {
            Add(entries, names, new PermissionEntry(name, description, PermissionSource.Core), CoreOwner);
        }

        foreach (var type in schema.ContentTypes)
        {
            foreach (var (operation, description) in ContentOperations)
            {
                var text = string.Format(CultureInfo.InvariantCulture, description, type.Name);
                Add(entries, names, new PermissionEntry(CorePermissions.Content(type.Name, operation), text, PermissionSource.Content), CoreOwner);
            }
        }

        foreach (var area in settingsAreas ?? [])
        {
            Add(entries, names, new PermissionEntry(CorePermissions.Settings(area), $"Change the {area} settings.", PermissionSource.Settings), CoreOwner);
        }

        foreach (var provider in providers)
        {
            var owner = provider.GetType().FullName ?? provider.GetType().Name;
            foreach (var permission in provider.GetPermissions())
            {
                if (permission.Name.StartsWith(Permission.ContentPrefix + ".", StringComparison.Ordinal))
                {
                    throw new FluentCmsConfigurationException(
                        AuthorizationErrorCodes.ReservedPermission,
                        $"{owner} declares '{permission.Name}', but the '{Permission.ContentPrefix}.' permissions are generated for each content type; pick another prefix.");
                }

                Add(entries, names, new PermissionEntry(permission.Name, permission.Description, PermissionSource.Plugin), owner);
            }
        }

        Entries = entries;
        _byName = entries.ToFrozenDictionary(entry => entry.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<PermissionEntry> Entries { get; }

    public bool Contains(string name) => _byName.ContainsKey(name);

    public PermissionEntry? Find(string name) => _byName.GetValueOrDefault(name);

    // Whether a grant, as stored in cms_role_permission, names at least one permission of the catalogue.
    public bool IsGrantable(string grant) =>
        PermissionPattern.IsWellFormed(grant) && Entries.Any(entry => PermissionPattern.Matches(grant, entry.Name));

    private static void Add(List<PermissionEntry> entries, HashSet<string> names, PermissionEntry entry, string owner)
    {
        if (!names.Add(entry.Name))
        {
            throw new FluentCmsConfigurationException(
                AuthorizationErrorCodes.DuplicatePermission,
                $"{owner} declares the permission '{entry.Name}', which the catalogue already has.");
        }

        entries.Add(entry);
    }
}
