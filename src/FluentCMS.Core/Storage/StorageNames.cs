using System.Text.Json;
using FluentCMS.Schema;

namespace FluentCMS.Core.Storage;

internal static class StorageNames
{
    public const string Prefix = "cms_";
    public const string Document = "cms_document";
    public const string Reference = "cms_reference";
    public const string Version = "cms_version";

    public const string Id = "id";
    public const string DocumentId = "document_id";
    public const string SiteId = "site_id";
    public const string Locale = "locale";
    public const string State = "state";

    public const string PublishedOnly = "state = 1";

    public static string Table(SchemaType type) => Prefix + type.Name.Replace('-', '_');

    public static string LocaleTable(SchemaType type) => Table(type) + "_i18n";

    public static string Column(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
