using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Administration.Roles;

// One grant of the role as the editor holds it. Constraints are the object the server answered, kept whole: the editor sends
// it back as it was read, and a grant whose constraints change is another grant.
public sealed class GrantDraft
{
    internal GrantDraft(string permission, JsonObject? constraints)
    {
        Permission = permission;
        Constraints = constraints;
    }

    // Tells the rows apart for rendering and for the server's refusal of one of them.
    public Guid Key { get; } = Guid.NewGuid();

    public string Permission { get; }

    public JsonObject? Constraints { get; set; }

    // Set when the server refused this grant on its last save.
    public bool Refused { get; set; }

    // The grant as the server compares grants: its permission and its constraints as written.
    public string Identity => Constraints is null ? Permission : $"{Permission} {Constraints.ToJsonString()}";

    internal static GrantDraft From(RoleGrantRecord grant) => new(grant.Permission, grant.Constraints?.DeepClone().AsObject());

    internal JsonObject ToJson()
    {
        var grant = new JsonObject { ["permission"] = Permission };
        if (Constraints is not null)
        {
            grant["constraints"] = Constraints.DeepClone();
        }

        return grant;
    }
}

// The role as the editor holds it until a save. A save replaces every grant (PUT), so the body carries each grant as it was
// read, with its constraints, beside those the user added.
internal sealed class RoleDraft
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool RequireTwoFactor { get; set; }

    public List<GrantDraft> Grants { get; } = [];

    public static RoleDraft From(RoleRecord role)
    {
        var draft = new RoleDraft { Name = role.Name, Description = role.Description, RequireTwoFactor = role.RequireTwoFactor };
        draft.Grants.AddRange(role.Grants.Select(GrantDraft.From));
        return draft;
    }

    // The body of PUT /roles/{id}; a create adds the site.
    public JsonObject ToJson() => new()
    {
        ["name"] = Name.Trim(),
        ["description"] = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        ["requireTwoFactor"] = RequireTwoFactor,
        ["grants"] = new JsonArray([.. Grants.Select(grant => (JsonNode)grant.ToJson())]),
    };

    // After another change of the role was saved during this one (FCMS3045): the role as it is now, with what the user
    // changed since the read put back on it. A field the user changed keeps the user's value; a grant the user took away is
    // taken from the role as it is now, and one the user added is added to it, so that a grant the other change added or
    // narrowed is kept as it saved it.
    public static RoleDraft Rebase(RoleRecord read, RoleDraft mine, RoleRecord current)
    {
        var rebased = new RoleDraft
        {
            Name = mine.Name != read.Name ? mine.Name : current.Name,
            Description = Same(mine.Description, read.Description) ? current.Description : mine.Description,
            RequireTwoFactor = mine.RequireTwoFactor != read.RequireTwoFactor ? mine.RequireTwoFactor : current.RequireTwoFactor,
        };

        // The grants read, less those the user kept, are those the user took away.
        var removed = Counted(read.Grants.Select(GrantDraft.From));
        var added = new List<GrantDraft>();
        foreach (var grant in mine.Grants)
        {
            if (removed.TryGetValue(grant.Identity, out var count) && count > 0)
            {
                removed[grant.Identity] = count - 1;
            }
            else
            {
                added.Add(grant);
            }
        }

        foreach (var grant in current.Grants.Select(GrantDraft.From))
        {
            if (removed.TryGetValue(grant.Identity, out var count) && count > 0)
            {
                removed[grant.Identity] = count - 1;
            }
            else
            {
                rebased.Grants.Add(grant);
            }
        }

        // A grant both changes added is there once.
        rebased.Grants.AddRange(added.Where(grant => rebased.Grants.All(kept => kept.Identity != grant.Identity)));
        return rebased;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(string.IsNullOrWhiteSpace(left) ? null : left.Trim(), string.IsNullOrWhiteSpace(right) ? null : right.Trim(), StringComparison.Ordinal);

    private static Dictionary<string, int> Counted(IEnumerable<GrantDraft> grants)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            counts[grant.Identity] = counts.GetValueOrDefault(grant.Identity) + 1;
        }

        return counts;
    }
}
