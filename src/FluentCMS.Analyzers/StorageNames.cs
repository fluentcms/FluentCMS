namespace FluentCMS.Analyzers;

// The names and limits that schema validation checks against, from ContentItem.ReservedFieldNames and the StorageNames of
// FluentCMS.Core, which a generator on netstandard2.0 cannot reference. A test holds them to the originals.
internal static class StorageNames
{
    public const string Prefix = "cms_";
    public const string LocaleSuffix = "_i18n";
    public const int MaxIdentifierLength = 63;
    public const int NameLength = 64;

    public static readonly HashSet<string> ReservedFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "status", "locale", "version", "createdAt", "createdBy", "updatedAt", "updatedBy", "publishedAt", "fallbacks",
        "scheduledPublishAt", "scheduledUnpublishAt", "deletedAt", "deletedBy",
    };

    public static readonly HashSet<string> SystemColumns = new(StringComparer.Ordinal) { "id", "document_id", "site_id", "state" };

    public static readonly HashSet<string> LocaleTableColumns = new(SystemColumns, StringComparer.Ordinal) { "locale" };

    public static readonly HashSet<string> SystemTables = new(new[]
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
        "document", "reference", "version",
    }.Select(name => Prefix + name), StringComparer.Ordinal);

    public static string Table(string typeName) => Prefix + typeName.Replace('-', '_');

    public static string LocaleTable(string typeName) => Table(typeName) + LocaleSuffix;

    public static string Column(string fieldName) => FluentCMS.Generators.SchemaNames.ToSnakeCase(fieldName);

    public static bool IsValidTypeName(string name)
    {
        if (name.Length == 0 || !IsLower(name[0]) || name[name.Length - 1] == '-')
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '-' ? name[i - 1] == '-' : !IsLower(c) && !IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsValidFieldName(string name) =>
        name.Length > 0 && IsLower(name[0]) && name.All(c => IsLower(c) || c is >= 'A' and <= 'Z' || IsDigit(c));

    private static bool IsLower(char c) => c is >= 'a' and <= 'z';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
