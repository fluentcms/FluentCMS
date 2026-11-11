using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
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
    public const string Lock = "cms_lock";
    public const string Outbox = "cms_outbox";
    public const string Activity = "cms_activity";
    public const string Schedule = "cms_schedule";
    public const string Setting = "cms_setting";
    public const string PluginSite = "cms_plugin_site";
    public const string SchemaHistory = "cms_schema_history";
    public const string User = "cms_user";
    public const string UserRole = "cms_user_role";
    public const string UserClaim = "cms_user_claim";
    public const string UserLogin = "cms_user_login";
    public const string UserToken = "cms_user_token";
    public const string Invitation = "cms_invitation";
    public const string ApiKey = "cms_api_key";
    public const string Idempotency = "cms_idempotency_key";
    public const string Member = "cms_member";
    public const string MemberLogin = "cms_member_login";
    public const string MemberToken = "cms_member_token";
    public const string MemberGroup = "cms_member_group";
    public const string MemberGroupMember = "cms_member_group_member";
    public const string OidcApplication = "cms_oidc_application";
    public const string OidcAuthorization = "cms_oidc_authorization";
    public const string OidcScope = "cms_oidc_scope";
    public const string OidcToken = "cms_oidc_token";
    public const string SigningKey = "cms_signing_key";
    public const string DataProtectionKey = "cms_data_protection_key";

    public const string Id = "id";
    public const string DocumentId = "document_id";
    public const string SiteId = "site_id";
    public const string Locale = "locale";
    public const string State = "state";

    public const int LocaleLength = 16;

    // PostgreSQL keeps the first 63 bytes of an identifier and drops the rest without a word; SQL Server allows 128.
    public const int MaxIdentifierLength = 63;

    // Type names and field paths are stored in the name columns of cms_document, cms_reference and cms_activity.
    public const int NameLength = 64;

    private const int HashLength = 8;

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
        "webhooks_subscription", "webhooks_delivery",
        "mcp_instruction", "mcp_prompt",
        "ai_usage",
        "site_host",
    }.Select(name => Prefix + name).Concat([Document, Reference, Version]).ToFrozenSet(StringComparer.Ordinal);

    // The tables core maps itself: every constant above that names one.
    public static readonly FrozenSet<string> CoreTables = typeof(StorageNames)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(field => field.IsLiteral && field.GetRawConstantValue() is string value && value.StartsWith(Prefix, StringComparison.Ordinal) && value != Prefix)
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToFrozenSet(StringComparer.Ordinal);

    public const string PublishedOnly = "state = 1";

    public static string Table(SchemaType type) => Table(type.Name);

    public static string Table(string typeName) => Prefix + typeName.Replace('-', '_');

    public static string LocaleTable(SchemaType type) => LocaleTable(type.Name);

    public static string LocaleTable(string typeName) => Table(typeName) + "_i18n";

    public static string Column(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

    // As EF shortens the names it makes, but with a hash of the whole name in place of a counter, so that the result
    // does not depend on the order of the model and two long names with the same start stay apart. Names are ASCII.
    public static string Identifier(string name)
    {
        if (name.Length <= MaxIdentifierLength)
        {
            return name;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..HashLength];
        return $"{name[..(MaxIdentifierLength - HashLength - 1)].TrimEnd('_')}_{hash}";
    }
}
