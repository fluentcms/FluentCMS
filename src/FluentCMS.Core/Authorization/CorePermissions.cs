using System.Collections.Frozen;
using System.Globalization;
using FluentCMS.Authorization;

namespace FluentCMS.Core.Authorization;

// The permission names of spec 4.8. settings.{area}.manage is one per settings area, so it is built by Settings.
public static class CorePermissions
{
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string ApiKeysManage = "apikeys.manage";
    public const string SitesManage = "sites.manage";
    public const string JobsManage = "jobs.manage";
    public const string ActivityRead = "activity.read";
    public const string SchemaRead = "schema.read";
    public const string PluginsManage = "plugins.manage";
    public const string TransferManage = "transfer.manage";
    public const string SystemRead = "system.read";

    // What a grant of this name means: every permission there is, plugins' and later types' included.
    public const string All = "*";

    public static readonly FrozenSet<string> Fixed = new[]
    {
        UsersManage, RolesManage, ApiKeysManage, SitesManage, JobsManage, ActivityRead, SchemaRead, PluginsManage, TransferManage, SystemRead,
    }.ToFrozenSet(StringComparer.Ordinal);

    public static string Settings(string area) => string.Create(CultureInfo.InvariantCulture, $"settings.{area}.manage");

    public static string Content(string type, ContentOperation operation) =>
        string.Create(CultureInfo.InvariantCulture, $"{Permission.ContentPrefix}.{type}.{OperationName(operation)}");

    public static string OperationName(ContentOperation operation) => operation switch
    {
        ContentOperation.Create => "create",
        ContentOperation.Read => "read",
        ContentOperation.Update => "update",
        ContentOperation.Delete => "delete",
        ContentOperation.Publish => "publish",
        ContentOperation.Unpublish => "unpublish",
        ContentOperation.Restore => "restore",
        ContentOperation.Purge => "purge",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown content operation."),
    };
}
