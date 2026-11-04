using System.Collections.Frozen;
using System.Text.Json;
using FluentCMS.Schema;

namespace FluentCMS.Core.Storage;

internal static class StorageNames
{
    public const string Prefix = "cms_";
    public const string HistoryTableSuffix = "__migrations";
    public const string Site = "cms_site";
    public const string SiteLocale = "cms_site_locale";
    public const string Document = "cms_document";
    public const string Reference = "cms_reference";
    public const string Version = "cms_version";
    public const string Role = "cms_role";
    public const string RolePermission = "cms_role_permission";
    public const string Job = "cms_job";
    public const string JobAttempt = "cms_job_attempt";
    public const string RecurringJob = "cms_recurring_job";
    public const string Outbox = "cms_outbox";
    public const string Activity = "cms_activity";
    public const string Schedule = "cms_schedule";
    public const string Setting = "cms_setting";

    public const string Id = "id";
    public const string DocumentId = "document_id";
    public const string SiteId = "site_id";
    public const string Locale = "locale";
    public const string State = "state";

    public const int LocaleLength = 16;

    // Columns every content table has, and the one a locale table adds.
    public static readonly FrozenSet<string> SystemColumns = new[] { Id, DocumentId, SiteId, State }.ToFrozenSet(StringComparer.Ordinal);

    public static readonly FrozenSet<string> LocaleTableColumns = SystemColumns.Append(Locale).ToFrozenSet(StringComparer.Ordinal);

    // Every table of spec Appendix C, the first-party plugins' included, so a content type cannot take a name
    // a later release maps. Add a table here when the spec does.
    public static readonly FrozenSet<string> SystemTables = new[]
    {
        "site", "site_locale", "schedule", "schema_history",
        "user", "user_role", "user_claim", "user_login", "user_token", "invitation", "role", "role_permission",
        "oidc_application", "oidc_authorization", "oidc_scope", "oidc_token", "signing_key", "data_protection_key",
        "api_key",
        "member", "member_login", "member_token", "member_group", "member_group_member",
        "activity",
        "job", "job_attempt", "recurring_job", "lock", "outbox",
        "setting", "plugin_site",
        "media_asset", "media_folder", "media_variant", "media_preset",
        "webhook", "webhook_delivery",
        "mcp_instruction", "mcp_prompt",
        "ai_usage",
        "site_host",
    }.Select(name => Prefix + name).Concat([Document, Reference, Version]).ToFrozenSet(StringComparer.Ordinal);

    public const string PublishedOnly = "state = 1";

    public static string Table(SchemaType type) => Table(type.Name);

    public static string Table(string typeName) => Prefix + typeName.Replace('-', '_');

    public static string LocaleTable(SchemaType type) => LocaleTable(type.Name);

    public static string LocaleTable(string typeName) => Table(typeName) + "_i18n";

    public static string Column(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
