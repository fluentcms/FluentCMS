using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Sites;

namespace FluentCMS.Core.Authorization;

// What narrows a grant in cms_role_permission.constraints. A null member does not narrow: null sites means every
// site, null locales every locale, null fields every field, null row rule every row. Grants only add, so these
// never deny anything.
public sealed class PermissionConstraints
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // A constraint this version does not know must fail, not be ignored, because ignoring one widens the grant.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static PermissionConstraints None { get; } = new();

    public IReadOnlyList<SiteId>? Sites { get; init; }

    public IReadOnlyList<string>? Locales { get; init; }

    public IReadOnlyList<string>? Fields { get; init; }

    public string? RowRule { get; init; }

    public bool IsEmpty => Sites is null && Locales is null && Fields is null && RowRule is null;

    // Null for a grant without constraints, which is stored as NULL.
    public string? ToJson() => IsEmpty
        ? null
        : JsonSerializer.Serialize(new Stored
        {
            Sites = Sites?.Select(site => site.Value).ToList(),
            Locales = Locales?.ToList(),
            Fields = Fields?.ToList(),
            RowRule = RowRule,
        }, Options);

    public static PermissionConstraints FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return None;
        }

        var stored = JsonSerializer.Deserialize<Stored>(json, Options) ?? new Stored();
        return new PermissionConstraints
        {
            Sites = stored.Sites?.Select(id => new SiteId(id)).ToList(),
            Locales = stored.Locales,
            Fields = stored.Fields,
            RowRule = stored.RowRule,
        };
    }

    private sealed class Stored
    {
        public List<Guid>? Sites { get; set; }

        public List<string>? Locales { get; set; }

        public List<string>? Fields { get; set; }

        public string? RowRule { get; set; }
    }
}
