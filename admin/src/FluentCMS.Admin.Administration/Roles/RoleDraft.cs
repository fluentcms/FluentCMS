using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Administration.Roles;

// One grant of the role as the editor holds it. Constraints are the object the server answered, kept whole: the editor
// changes only the members the user changes and sends the rest back as it was read, and a grant whose constraints change is
// another grant.
public sealed class GrantDraft
{
    // The members of the constraints the server knows, in the order of the identity.
    private static readonly string[] Members = ["sites", "locales", "fields", "rowRule", "resources"];

    internal GrantDraft(string permission, JsonObject? constraints, string? origin = null)
    {
        Permission = permission;
        Constraints = constraints;
        Origin = origin;
    }

    // Tells the rows apart for rendering and for the server's refusal of one of them.
    public Guid Key { get; } = Guid.NewGuid();

    public string Permission { get; }

    public JsonObject? Constraints { get; set; }

    // The identity of the grant as it was read, which the merge after FCMS3045 starts from; null for a grant the user added.
    public string? Origin { get; }

    // The paths below the grant the server refused on its last save, such as constraints.locales; empty when none.
    public IReadOnlyList<string> Refusals { get; set; } = [];

    public bool Refused => Refusals.Count > 0;

    public string Identity => IdentityOf(Permission, Constraints);

    // The grant as the server tells grants apart once it has stored them: the known members in a fixed order, each list
    // as a set as the server compares it (sites as ids, locales in any case, every list distinct), and a member that
    // is null, or an object with none, as no constraint, which the server stores as null. A member it does not know is kept
    // as written, since the server refuses it rather than drop it.
    public static string IdentityOf(string permission, JsonObject? constraints)
    {
        var canonical = new JsonObject();
        if (constraints is not null)
        {
            foreach (var member in Members)
            {
                if (constraints[member] is { } value)
                {
                    canonical[member] = Normalized(member, value);
                }
            }

            foreach (var (name, value) in constraints.Where(pair => pair.Value is not null && !Members.Contains(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                canonical[name] = value!.DeepClone();
            }
        }

        return canonical.Count == 0 ? permission : $"{permission} {canonical.ToJsonString()}";
    }

    private static JsonNode Normalized(string member, JsonNode value)
    {
        if (value is not JsonArray items)
        {
            return value.DeepClone();
        }

        var texts = items.Select(item => item is JsonValue text && text.TryGetValue<string>(out var said) ? said : item?.ToJsonString() ?? "null");
        texts = member switch
        {
            "sites" => texts.Select(text => Guid.TryParse(text, out var site) ? site.ToString("D") : text),
            "locales" => texts.Select(text => text.ToLowerInvariant()),
            _ => texts,
        };

        return new JsonArray([.. texts.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(text => (JsonNode)JsonValue.Create(text))]);
    }

    internal static GrantDraft From(RoleGrantRecord grant) =>
        new(grant.Permission, grant.Constraints?.DeepClone().AsObject(), IdentityOf(grant.Permission, grant.Constraints));

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
    // narrowed is kept as it saved it. A grant whose constraints the user changed takes the change only where the other
    // change left that grant as it was read: where it changed or took it away, its version stays and the user's change is
    // dropped and named in Dropped, since the two together could reach further than either meant.
    public static (RoleDraft Draft, IReadOnlyList<string> Dropped) Rebase(RoleRecord read, RoleDraft mine, RoleRecord current)
    {
        var rebased = new RoleDraft
        {
            Name = mine.Name != read.Name ? mine.Name : current.Name,
            Description = Same(mine.Description, read.Description) ? current.Description : mine.Description,
            RequireTwoFactor = mine.RequireTwoFactor != read.RequireTwoFactor ? mine.RequireTwoFactor : current.RequireTwoFactor,
        };

        // The grants read, less those the user kept or changed, are those the user took away.
        var takenAway = Counted(read.Grants.Select(GrantDraft.From));
        var added = new List<GrantDraft>();
        var changed = new List<GrantDraft>();
        foreach (var grant in mine.Grants)
        {
            if (grant.Origin is { } origin && takenAway.GetValueOrDefault(origin) > 0)
            {
                takenAway[origin]--;
                if (grant.Identity != origin)
                {
                    changed.Add(grant);
                }
            }
            else
            {
                added.Add(grant);
            }
        }

        var theirs = current.Grants.Select(GrantDraft.From).ToList();
        var left = Counted(theirs);
        var replacing = new Dictionary<string, Queue<GrantDraft>>(StringComparer.Ordinal);
        var dropped = new List<string>();
        foreach (var grant in changed)
        {
            var origin = grant.Origin!;
            if (left.GetValueOrDefault(origin) - takenAway.GetValueOrDefault(origin) > 0)
            {
                left[origin]--;
                if (!replacing.TryGetValue(origin, out var queue))
                {
                    replacing[origin] = queue = new Queue<GrantDraft>();
                }

                queue.Enqueue(grant);
            }
            else if (theirs.All(kept => kept.Identity != grant.Identity))
            {
                dropped.Add(grant.Permission);
            }
        }

        foreach (var grant in theirs)
        {
            if (takenAway.GetValueOrDefault(grant.Identity) > 0)
            {
                takenAway[grant.Identity]--;
            }
            else if (replacing.TryGetValue(grant.Identity, out var queue) && queue.Count > 0)
            {
                rebased.Grants.Add(queue.Dequeue());
            }
            else
            {
                rebased.Grants.Add(grant);
            }
        }

        // A grant both changes added is there once.
        foreach (var grant in added)
        {
            if (rebased.Grants.All(kept => kept.Identity != grant.Identity))
            {
                rebased.Grants.Add(grant);
            }
        }

        return (rebased, [.. dropped.Distinct(StringComparer.Ordinal)]);
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
